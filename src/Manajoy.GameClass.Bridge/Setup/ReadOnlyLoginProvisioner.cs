using System.Data.Common;
using System.Data.Odbc;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Creates the READ-ONLY SQL login the bridge uses (C# twin of field-kit/scripts/create-readonly-login.ps1).
///
/// This is deliberately the only code in the bridge that changes SQL Server, and it bypasses the
/// read-only query guard on purpose: it runs once, as an administrator the club owner names, and only
/// (1) creates/resets one login, (2) maps it as db_datareader in the GameClass database and (3) denies it
/// EXECUTE on every GameClass procedure (GameClass grants those to "public"). It then proves, logged in
/// as that login, that writes are refused. GameClass data is never touched.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ReadOnlyLoginProvisioner
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int PasswordLength = 24;
    private const int ConnectionTimeoutSeconds = 10;
    private const string ApplicationName = "Manajoy.GameClass.Setup";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{2,30}$")]
    private static partial Regex LoginNameRegex();

    [GeneratedRegex("permission|denied|229", RegexOptions.IgnoreCase)]
    private static partial Regex PermissionDeniedRegex();

    public sealed record Outcome(bool Success, string Password, IReadOnlyList<CheckResult> Checks);

    public static Outcome Provision(DatabaseRequest request, string login)
    {
        if (!LoginNameRegex().IsMatch(login))
        {
            throw new SetupException("invalid_login_name", login);
        }

        var server = Value(request.Server, SetupEnvironment.DefaultServer);
        var database = Value(request.Database, SetupEnvironment.DefaultDatabase);
        var checks = new List<CheckResult>();
        var password = GeneratePassword();

        using (var admin = OpenAdmin(request, server, "master"))
        {
            if (Scalar(admin, "SELECT COUNT(*) FROM master.dbo.sysdatabases WHERE name = ?", database) == 0)
            {
                throw new SetupException("database_not_found", $"{server}/{database}");
            }
        }

        using (var admin = OpenAdmin(request, server, database))
        {
            if (Scalar(admin, "SELECT IS_SRVROLEMEMBER('sysadmin')") != 1
                && Scalar(admin, "SELECT IS_SRVROLEMEMBER('securityadmin')") != 1)
            {
                throw new SetupException("admin_not_privileged", Value(request.AdminUser, "sa"));
            }

            if (Scalar(admin, "SELECT COUNT(*) FROM master.dbo.syslogins WHERE name = ?", login) > 0)
            {
                NonQuery(admin, "EXEC master.dbo.sp_password @old = NULL, @new = ?, @loginame = ?", password, login);
                checks.Add(new CheckResult("login_reset", CheckLevel.Ok, login));
            }
            else
            {
                NonQuery(admin, "EXEC master.dbo.sp_addlogin @loginame = ?, @passwd = ?, @defdb = ?", login, password, database);
                checks.Add(new CheckResult("login_created", CheckLevel.Ok, login));
            }

            if (Scalar(admin, "SELECT COUNT(*) FROM sysusers WHERE name = ?", login) == 0)
            {
                NonQuery(admin, "EXEC sp_grantdbaccess @loginame = ?", login);
            }

            NonQuery(admin, "EXEC sp_addrolemember @rolename = 'db_datareader', @membername = ?", login);
            checks.Add(new CheckResult("db_datareader_granted", CheckLevel.Ok, database));

            // GameClass grants EXECUTE on its writer procedures (SessionsInsert, AccountsMoneyPay, ...) to
            // "public". DENY overrides that grant for this login only.
            var routines = Strings(admin, "SELECT name FROM sysobjects WHERE type IN ('P', 'FN') AND OBJECTPROPERTY(id, 'IsMSShipped') = 0");
            foreach (var routine in routines)
            {
                NonQuery(admin, $"DENY EXECUTE ON [{routine.Replace("]", "]]", StringComparison.Ordinal)}] TO [{login}]");
            }

            checks.Add(new CheckResult("execute_denied", CheckLevel.Ok, routines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        checks.AddRange(Verify(server, database, login, password));
        var success = checks.All(check => check.Level == CheckLevel.Ok);
        return new Outcome(success, success ? password : string.Empty, checks);
    }

    /// <summary>Removes the login (uninstall). Missing user/login is not an error.</summary>
    public static void Drop(UninstallRequest request, string login)
    {
        if (!LoginNameRegex().IsMatch(login))
        {
            throw new SetupException("invalid_login_name", login);
        }

        var auth = new DatabaseRequest(request.AdminUser, request.AdminPassword, request.WindowsAuth, null, null);
        using var admin = OpenAdmin(auth, SetupEnvironment.DefaultServer, SetupEnvironment.DefaultDatabase);
        NonQuery(admin, $"IF USER_ID('{login}') IS NOT NULL EXEC sp_revokedbaccess '{login}'");
        NonQuery(admin, $"IF EXISTS (SELECT * FROM master.dbo.syslogins WHERE name = '{login}') EXEC master.dbo.sp_droplogin '{login}'");
    }

    /// <summary>Logged in as the new login: it must read, and every kind of write must be refused by SQL Server.</summary>
    private static List<CheckResult> Verify(string server, string database, string login, string password)
    {
        var checks = new List<CheckResult>();
        using var ro = Open(server, database, login, password, trusted: false);

        checks.Add(Expect("can_read", Scalar(ro, "SELECT COUNT(*) FROM Computers") >= 0));
        checks.Add(Expect("is_datareader", Scalar(ro, "SELECT IS_MEMBER('db_datareader')") == 1));
        checks.Add(Expect("not_datawriter", Scalar(ro, "SELECT IS_MEMBER('db_datawriter')") == 0));
        checks.Add(Expect("not_db_owner", Scalar(ro, "SELECT IS_MEMBER('db_owner')") == 0));
        checks.Add(Expect("not_sysadmin", Scalar(ro, "SELECT IS_SRVROLEMEMBER('sysadmin')") == 0));

        // A write that would change no rows: SQL Server still checks the permission and must refuse it.
        checks.Add(Expect("write_refused", Refused(ro, "UPDATE Registry SET [value] = [value] WHERE 1 = 0")));

        // PERMISSIONS() bit 32 = EXECUTE for the current login.
        var executable = Scalar(ro, "SELECT COUNT(*) FROM sysobjects WHERE type IN ('P', 'FN') AND OBJECTPROPERTY(id, 'IsMSShipped') = 0 AND (PERMISSIONS(id) & 32) = 32");
        checks.Add(Expect("no_procedure_executable", executable == 0, executable.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (Scalar(ro, "SELECT COUNT(*) FROM sysobjects WHERE name = 'SessionsUpdateTimeCost' AND type = 'P'") == 1)
        {
            // Would update no row even if it ran (segment id 0 does not exist).
            checks.Add(Expect("procedure_write_refused", Refused(ro, "EXEC SessionsUpdateTimeCost @idSessionsAdd = 0, @TimeCost = 0")));
        }

        return checks;
    }

    private static CheckResult Expect(string id, bool ok, string detail = "") =>
        new(id, ok ? CheckLevel.Ok : CheckLevel.Fail, detail);

    private static bool Refused(OdbcConnection connection, string sql)
    {
        try
        {
            NonQuery(connection, sql);
            return false;
        }
        catch (OdbcException ex)
        {
            return PermissionDeniedRegex().IsMatch(ex.Message);
        }
    }

    private static OdbcConnection OpenAdmin(DatabaseRequest request, string server, string database)
    {
        try
        {
            return Open(server, database, Value(request.AdminUser, "sa"), request.AdminPassword ?? string.Empty, request.WindowsAuth);
        }
        catch (OdbcException ex)
        {
            // 18456: login failed; 4060: cannot open database; 17 / 08001: server not found.
            var code = ex.Errors.Cast<OdbcError>().Any(e => e.NativeError == 18456 || e.SQLState == "28000") ? "admin_login_failed"
                : ex.Errors.Cast<OdbcError>().Any(e => e.NativeError == 4060) ? "database_not_found"
                : ex.Errors.Cast<OdbcError>().Any(e => e.SQLState == "08001" || e.NativeError == 17) ? "sql_unreachable"
                : "admin_connect_failed";
            // OdbcException.Message repeats every driver message; the first one says it all.
            throw new SetupException(code, ex.Errors.Count > 0 ? ex.Errors[0].Message : ex.Message, ex);
        }
    }

    private static OdbcConnection Open(string server, string database, string user, string password, bool trusted)
    {
        var builder = new OdbcConnectionStringBuilder { Driver = "SQL Server" };
        builder["Server"] = server;
        builder["Database"] = database;
        builder["APP"] = ApplicationName;
        if (trusted)
        {
            builder["Trusted_Connection"] = "Yes";
        }
        else
        {
            builder["UID"] = user;
            builder["PWD"] = password;
        }

        var connection = new OdbcConnection(builder.ConnectionString) { ConnectionTimeout = ConnectionTimeoutSeconds };
        connection.Open();
        return connection;
    }

    // ODBC parameters are positional ('?').
    private static OdbcCommand Command(OdbcConnection connection, string sql, object[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", values[i]);
        }

        return command;
    }

    private static int Scalar(OdbcConnection connection, string sql, params object[] values)
    {
        using var command = Command(connection, sql, values);
        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void NonQuery(OdbcConnection connection, string sql, params object[] values)
    {
        using var command = Command(connection, sql, values);
        command.ExecuteNonQuery();
    }

    private static List<string> Strings(OdbcConnection connection, string sql)
    {
        using var command = Command(connection, sql, []);
        using DbDataReader reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    internal static string GeneratePassword() =>
        string.Create(PasswordLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
            }
        });

    private static string Value(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
