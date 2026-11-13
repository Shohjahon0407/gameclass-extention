namespace Manajoy.GameClass.Inspector.Configuration;

public sealed class InspectorOptions
{
    public const string SectionName = "Inspection";

    /// <summary>Absolute ceiling for sample rows, regardless of configuration.</summary>
    public const int SampleRowsHardLimit = 5;

    public int ConnectionTimeoutSeconds { get; set; } = 5;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool AllowSampleRows { get; set; }

    public int MaxSampleRows { get; set; } = SampleRowsHardLimit;

    /// <summary>Tables explicitly chosen for sampling. Nothing else is ever sampled.</summary>
    public List<SampleTableOptions> SampleTables { get; set; } = [];

    /// <summary>Extra server names to probe, e.g. ".\GAMECLASS" or "localhost,1433".</summary>
    public List<string> AdditionalServers { get; set; } = [];

    /// <summary>Retry through the Windows built-in "SQL Server" ODBC driver (needed for SQL Server 2000 / MSDE).</summary>
    public bool EnableOdbcFallback { get; set; } = true;

    public SqlLoginOptions SqlLogin { get; set; } = new();

    public string OutputDirectory { get; set; } = "output";

    public string LogDirectory { get; set; } = "logs";
}

public sealed class SampleTableOptions
{
    /// <summary>Optional; when empty the table is looked up on every connected instance.</summary>
    public string? Server { get; set; }

    public string Database { get; set; } = string.Empty;

    public string Schema { get; set; } = "dbo";

    public string Table { get; set; } = string.Empty;

    public override string ToString() => $"{Server ?? "*"}/{Database}/{Schema}.{Table}";
}

/// <summary>
/// Optional SQL login, used only when explicitly configured and Windows authentication is rejected.
/// Supply the password through the environment (Inspection__SqlLogin__Password), never commit it.
/// </summary>
public sealed class SqlLoginOptions
{
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(UserName);
}
