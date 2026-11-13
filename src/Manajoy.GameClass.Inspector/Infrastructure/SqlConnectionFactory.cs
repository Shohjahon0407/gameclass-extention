using System.Data.Common;
using System.Data.Odbc;
using Manajoy.GameClass.Inspector.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Infrastructure;

/// <summary>
/// Builds provider-specific connections. Connection strings never leave this class,
/// so they cannot end up in logs or reports.
/// </summary>
internal sealed class SqlConnectionFactory(IOptions<InspectorOptions> options)
{
    private const string DefaultDatabase = "master";
    private const string LegacyOdbcDriver = "SQL Server";

    private readonly InspectorOptions _options = options.Value;

    public DbConnection Create(ConnectionTarget target, string? database = null) => target.Provider switch
    {
        DataProvider.SqlClient => new SqlConnection(BuildSqlClientConnectionString(target, database)),
        DataProvider.OdbcLegacy => CreateOdbcConnection(target, database),
        _ => throw new NotSupportedException($"Unknown provider {target.Provider}."),
    };

    private string BuildSqlClientConnectionString(ConnectionTarget target, string? database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = target.Server,
            InitialCatalog = database ?? DefaultDatabase,
            ConnectTimeout = _options.ConnectionTimeoutSeconds,
            CommandTimeout = _options.CommandTimeoutSeconds,
            ApplicationName = AppInfo.SqlApplicationName,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            // Local-only connections to old servers that have no trusted certificate.
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            MultipleActiveResultSets = false,
            // Without pooling the server-side audit trail shows only our SELECTs (no sp_reset_connection).
            Pooling = false,
        };

        if (target.Authentication == AuthenticationMode.Windows)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = _options.SqlLogin.UserName;
            builder.Password = _options.SqlLogin.Password;
        }

        return builder.ConnectionString;
    }

    private OdbcConnection CreateOdbcConnection(ConnectionTarget target, string? database)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The legacy SQL Server ODBC driver is only available on Windows.");
        }

        var builder = new OdbcConnectionStringBuilder { Driver = LegacyOdbcDriver };
        builder["Server"] = target.Server;
        builder["Database"] = database ?? DefaultDatabase;
        builder["APP"] = AppInfo.SqlApplicationName;

        if (target.Authentication == AuthenticationMode.Windows)
        {
            builder["Trusted_Connection"] = "Yes";
        }
        else
        {
            builder["UID"] = _options.SqlLogin.UserName;
            builder["PWD"] = _options.SqlLogin.Password;
        }

        return new OdbcConnection(builder.ConnectionString)
        {
            ConnectionTimeout = _options.ConnectionTimeoutSeconds,
        };
    }
}
