using System.Data.Common;
using System.Data.Odbc;
using Manajoy.GameClass.Bridge.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Data;

/// <summary>
/// Opens connections to the GameClass database. Connection strings never leave this class,
/// so they cannot end up in logs, snapshots or API responses.
/// In Auto mode the first provider that connects is remembered; SQL Server 2000 / MSDE only
/// works through the Windows built-in "SQL Server" ODBC driver.
/// </summary>
internal sealed class GameClassConnectionFactory(IOptions<BridgeOptions> options, ILogger<GameClassConnectionFactory> logger)
{
    public const string SqlApplicationName = "Manajoy.GameClass.Bridge";
    private const string LegacyOdbcDriver = "SQL Server";

    private readonly BridgeOptions _options = options.Value;
    private BridgeDataProvider? _resolvedProvider;

    public string? ActiveProviderName => _resolvedProvider?.ToString();

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (_resolvedProvider is { } known)
        {
            return await OpenWithAsync(known, cancellationToken);
        }

        var candidates = _options.Provider switch
        {
            BridgeDataProvider.SqlClient => [BridgeDataProvider.SqlClient],
            BridgeDataProvider.Odbc => [BridgeDataProvider.Odbc],
            _ => OperatingSystem.IsWindows()
                ? new[] { BridgeDataProvider.Odbc, BridgeDataProvider.SqlClient }
                : new[] { BridgeDataProvider.SqlClient },
        };

        Exception? lastError = null;
        foreach (var provider in candidates)
        {
            try
            {
                var connection = await OpenWithAsync(provider, cancellationToken);
                _resolvedProvider = provider;
                logger.LogInformation("Connected to {Server}/{Database} via {Provider} ({Auth})",
                    _options.Server, _options.Database, provider, _options.SqlLogin.IsConfigured ? "SQL login" : "Windows");
                return connection;
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or PlatformNotSupportedException)
            {
                lastError = ex;
                logger.LogDebug("Provider {Provider} could not connect: {Message}", provider, ex.Message);
            }
        }

        throw new GameClassUnavailableException(
            $"Could not connect to {_options.Server}/{_options.Database}: {lastError?.Message}", lastError);
    }

    /// <summary>Forget the remembered provider, e.g. after the server was restarted.</summary>
    public void Reset() => _resolvedProvider = _options.Provider == BridgeDataProvider.Auto ? null : _resolvedProvider;

    private async Task<DbConnection> OpenWithAsync(BridgeDataProvider provider, CancellationToken cancellationToken)
    {
        DbConnection connection = provider switch
        {
            BridgeDataProvider.Odbc => CreateOdbc(),
            _ => new SqlConnection(BuildSqlClientConnectionString()),
        };

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private string BuildSqlClientConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _options.Server,
            InitialCatalog = _options.Database,
            ConnectTimeout = _options.ConnectionTimeoutSeconds,
            CommandTimeout = _options.CommandTimeoutSeconds,
            ApplicationName = SqlApplicationName,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            MultipleActiveResultSets = false,
        };

        if (_options.SqlLogin.IsConfigured)
        {
            builder.UserID = _options.SqlLogin.UserName;
            builder.Password = _options.SqlLogin.Password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private OdbcConnection CreateOdbc()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The legacy SQL Server ODBC driver is only available on Windows.");
        }

        var builder = new OdbcConnectionStringBuilder { Driver = LegacyOdbcDriver };
        builder["Server"] = _options.Server;
        builder["Database"] = _options.Database;
        builder["APP"] = SqlApplicationName;
        // Convert server code page (cp1251 for Russian installs) to Unicode on the client.
        builder["AutoTranslate"] = "Yes";

        if (_options.SqlLogin.IsConfigured)
        {
            builder["UID"] = _options.SqlLogin.UserName;
            builder["PWD"] = _options.SqlLogin.Password;
        }
        else
        {
            builder["Trusted_Connection"] = "Yes";
        }

        return new OdbcConnection(builder.ConnectionString) { ConnectionTimeout = _options.ConnectionTimeoutSeconds };
    }
}

internal sealed class GameClassUnavailableException(string message, Exception? inner) : Exception(message, inner);
