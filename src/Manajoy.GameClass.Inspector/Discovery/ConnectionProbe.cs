using System.Data.Odbc;
using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Discovery;

/// <summary>
/// Tries to log into an instance with at most one attempt per provider and authentication mode.
/// Windows authentication first; an explicitly configured SQL login second. No credential guessing.
/// </summary>
internal sealed class ConnectionProbe(
    ReadOnlyQueryExecutor executor,
    IOptions<InspectorOptions> options,
    InspectionErrors errors,
    ILogger<ConnectionProbe> logger)
{
    // Login failed, untrusted domain, account disabled/locked/expired/must change password.
    private static readonly HashSet<int> LoginFailureNumbers = [18456, 18452, 18470, 18486, 18487, 18488];

    // Named pipes / TCP "server not found", timeouts, instance lookup and socket errors.
    private static readonly HashSet<int> NotReachableNumbers = [-2, -1, 2, 26, 35, 40, 53, 258, 10060, 10061, 11001];

    private const string InvalidAuthorizationSqlState = "28000";
    private const string ClientUnableToConnectSqlState = "08001";

    private readonly InspectorOptions _options = options.Value;

    public async Task<ProbeOutcome> ProbeAsync(SqlInstanceInfo instance, bool? isElevated, CancellationToken cancellationToken)
    {
        if (instance.IsRunning == false)
        {
            var reason = $"Service {instance.ServiceName} is {instance.ServiceStatus}. The inspector never starts services.";
            logger.LogWarning("Skipping {Server}: {Reason}", instance.ServerName, reason);
            errors.Warning(InspectionStage.ConnectionProbe, instance.ServerName, reason);
            return ProbeOutcome.Failed(new ConnectionProbeResult { Success = false, FailureReason = reason });
        }

        var attempts = new List<ConnectionAttempt>();
        var failures = new List<(AuthenticationMode Authentication, Exception Error)>();

        foreach (var authentication in AuthenticationModes())
        {
            foreach (var provider in Providers(instance))
            {
                var target = new ConnectionTarget(instance.ServerName, provider, authentication);
                logger.LogInformation(
                    "Probing {Server} via {Provider} ({Authentication} authentication)",
                    target.Server, target.ProviderDisplayName, target.AuthenticationDisplayName);

                try
                {
                    await executor.QueryAsync(target, null, SqlQueries.Ping, reader => reader.GetValue(0), cancellationToken);
                    attempts.Add(new ConnectionAttempt(target.ProviderDisplayName, target.AuthenticationDisplayName, true, null));
                    var serverInfo = await TryReadServerInfoAsync(target, cancellationToken);

                    logger.LogInformation(
                        "Connected to {Server} as {Login} ({Product} {Version})",
                        target.Server, serverInfo?.CurrentLogin, serverInfo?.ProductFamily, serverInfo?.ProductVersion);

                    return ProbeOutcome.Connected(target, new ConnectionProbeResult
                    {
                        Success = true,
                        Provider = target.ProviderDisplayName,
                        Authentication = target.AuthenticationDisplayName,
                        Server = serverInfo,
                        Attempts = attempts,
                    });
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    attempts.Add(new ConnectionAttempt(target.ProviderDisplayName, target.AuthenticationDisplayName, false, ex.Message));
                    failures.Add((authentication, ex));
                    logger.LogWarning(
                        "Connection to {Server} via {Provider} ({Authentication}) failed: {Message}",
                        target.Server, target.ProviderDisplayName, target.AuthenticationDisplayName, ex.Message);

                    if (IsLoginFailure(ex))
                    {
                        // The server answered and rejected the login; another client library will not change that.
                        break;
                    }
                }
            }
        }

        var failureReason = attempts.Count == 0
            ? "No usable connection method on this OS (Windows authentication requires Windows; no SQL login configured)."
            : attempts[^1].Error;
        var hint = BuildHint(failures, isElevated);

        errors.Warning(InspectionStage.ConnectionProbe, instance.ServerName, hint is null ? $"{failureReason}" : $"{failureReason} Hint: {hint}");
        return ProbeOutcome.Failed(new ConnectionProbeResult
        {
            Success = false,
            Attempts = attempts,
            FailureReason = failureReason,
            Hint = hint,
        });
    }

    private IEnumerable<AuthenticationMode> AuthenticationModes()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return AuthenticationMode.Windows;
        }

        if (_options.SqlLogin.IsConfigured)
        {
            yield return AuthenticationMode.SqlLogin;
        }
    }

    private IEnumerable<DataProvider> Providers(SqlInstanceInfo instance)
    {
        var odbcAvailable = _options.EnableOdbcFallback && OperatingSystem.IsWindows();

        // SqlClient cannot talk to SQL Server 2000 at all (TDS 7.1), so don't waste an attempt on it.
        if (odbcAvailable && instance.RegistryMajorVersion is <= SqlVersion.SqlServer2000)
        {
            yield return DataProvider.OdbcLegacy;
            yield break;
        }

        yield return DataProvider.SqlClient;

        if (odbcAvailable)
        {
            yield return DataProvider.OdbcLegacy;
        }
    }

    private async Task<ServerInfo?> TryReadServerInfoAsync(ConnectionTarget target, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await executor.QueryAsync(
                target,
                null,
                SqlQueries.ServerInfo,
                reader => new ServerInfo
                {
                    ProductVersion = reader.GetStringOrNull("ProductVersion"),
                    ProductLevel = reader.GetStringOrNull("ProductLevel"),
                    Edition = reader.GetStringOrNull("Edition"),
                    EngineEdition = reader.GetInt32OrNull("EngineEdition"),
                    ServerName = reader.GetStringOrNull("ServerName"),
                    CurrentLogin = reader.GetStringOrNull("CurrentLogin"),
                },
                cancellationToken);
            return rows.FirstOrDefault();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Connected to {Server} but could not read server properties: {Message}", target.Server, ex.Message);
            errors.Warning(InspectionStage.ConnectionProbe, target.Server, $"Server properties unavailable: {ex.Message}");
            return null;
        }
    }

    private string? BuildHint(IReadOnlyList<(AuthenticationMode Authentication, Exception Error)> failures, bool? isElevated)
    {
        var windowsRejected = failures.Any(f => f.Authentication == AuthenticationMode.Windows && IsLoginFailure(f.Error));
        var sqlLoginRejected = failures.Any(f => f.Authentication == AuthenticationMode.SqlLogin && IsLoginFailure(f.Error));

        if (sqlLoginRejected)
        {
            return "The server rejected the configured SQL login. Verify the credentials with the club; the inspector will not try others.";
        }

        if (windowsRejected)
        {
            var configureLogin = _options.SqlLogin.IsConfigured
                ? string.Empty
                : " Alternatively ask which SQL login GameClass uses and pass it via the Inspection__SqlLogin__UserName / "
                + "Inspection__SqlLogin__Password environment variables.";

            return isElevated == false
                ? "The Windows login was rejected and the inspector is not running elevated. On MSDE / SQL Server 2000-2005 local "
                + "administrators are admitted only from an elevated process: right-click the exe and choose 'Run as administrator'." + configureLogin
                : "The Windows account is not mapped to a SQL Server login." + configureLogin;
        }

        var exceptions = failures.Select(f => f.Error).ToList();
        if (exceptions.Exists(IsTlsFailure))
        {
            return "TLS handshake failed. Old SQL Server builds only support TLS 1.0, which modern Windows may disable. "
                 + "Report this; do not change system TLS settings for the inspector.";
        }

        if (exceptions.Exists(IsUnsupportedServer))
        {
            return OperatingSystem.IsWindows() && _options.EnableOdbcFallback
                ? "The server speaks a protocol SqlClient does not support (typically SQL Server 2000 / MSDE) and the ODBC fallback also failed. "
                + "Check that the Windows 'SQL Server' ODBC driver is present (odbcad32.exe → Drivers)."
                : "The server looks like SQL Server 2000 / MSDE, which SqlClient cannot talk to. Enable Inspection:EnableOdbcFallback and run on Windows.";
        }

        if (exceptions.Exists(IsNotReachable))
        {
            return "The instance did not answer. Check that the SQL Server service is running and the instance name is right. "
                 + "Local connections use shared memory / named pipes, so TCP/IP does not need to be enabled.";
        }

        return null;
    }

    private static bool IsLoginFailure(Exception ex) => ex switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(error => LoginFailureNumbers.Contains(error.Number)),
        OdbcException odbc => odbc.Errors.Cast<OdbcError>().Any(error =>
            LoginFailureNumbers.Contains(error.NativeError) || error.SQLState == InvalidAuthorizationSqlState),
        _ => false,
    };

    private static bool IsNotReachable(Exception ex) => ex switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(error => NotReachableNumbers.Contains(error.Number))
            || sql.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase),
        OdbcException odbc => odbc.Errors.Cast<OdbcError>().Any(error => error.SQLState == ClientUnableToConnectSqlState),
        _ => false,
    };

    private static bool IsTlsFailure(Exception ex) =>
        ex.ToString() is var text
        && (text.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TLS", StringComparison.Ordinal)
            || text.Contains("handshake", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AuthenticationException", StringComparison.Ordinal));

    private static bool IsUnsupportedServer(Exception ex) =>
        ex.Message.Contains("Internal connection fatal error", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("TDS", StringComparison.Ordinal)
        || ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase);
}

internal sealed record ProbeOutcome(ConnectionProbeResult Result, ConnectionTarget? Target)
{
    public static ProbeOutcome Connected(ConnectionTarget target, ConnectionProbeResult result) => new(result, target);

    public static ProbeOutcome Failed(ConnectionProbeResult result) => new(result, null);
}
