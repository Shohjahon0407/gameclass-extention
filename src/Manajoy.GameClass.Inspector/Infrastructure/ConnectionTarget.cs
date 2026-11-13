namespace Manajoy.GameClass.Inspector.Infrastructure;

internal enum DataProvider
{
    SqlClient,

    /// <summary>Windows built-in "SQL Server" ODBC driver (sqlsrv32.dll); the only in-box client that still speaks to SQL Server 2000.</summary>
    OdbcLegacy,
}

internal enum AuthenticationMode
{
    Windows,
    SqlLogin,
}

/// <summary>How to reach an instance. Deliberately holds no connection string or secret.</summary>
internal sealed record ConnectionTarget(string Server, DataProvider Provider, AuthenticationMode Authentication)
{
    public string ProviderDisplayName => Provider switch
    {
        DataProvider.SqlClient => "SqlClient",
        DataProvider.OdbcLegacy => "ODBC {SQL Server}",
        _ => Provider.ToString(),
    };

    public string AuthenticationDisplayName => Authentication switch
    {
        AuthenticationMode.Windows => "Windows",
        AuthenticationMode.SqlLogin => "SQL login",
        _ => Authentication.ToString(),
    };
}
