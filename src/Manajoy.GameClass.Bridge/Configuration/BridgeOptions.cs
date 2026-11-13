namespace Manajoy.GameClass.Bridge.Configuration;

public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    public const int MinApiKeyLength = 24;

    /// <summary>SQL Server instance that hosts the GameClass database, e.g. "(local)" or ".\GAMECLASS".</summary>
    public string Server { get; set; } = "(local)";

    public string Database { get; set; } = "GameClass";

    /// <summary>Auto tries SqlClient first and falls back to the legacy ODBC driver (needed for SQL Server 2000 / MSDE).</summary>
    public BridgeDataProvider Provider { get; set; } = BridgeDataProvider.Auto;

    /// <summary>
    /// Read-only SQL login (db_datareader). Windows authentication is used when it is not configured.
    /// The password comes from <see cref="SqlLoginOptions.PasswordFile"/> (written by install-service.ps1)
    /// or the environment (Bridge__SqlLogin__Password); never put it in appsettings.json.
    /// </summary>
    public SqlLoginOptions SqlLogin { get; set; } = new();

    public int ConnectionTimeoutSeconds { get; set; } = 5;

    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>How often the GameClass database is polled.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Kestrel URL(s) for the local read-only HTTP API, separated by ';'.</summary>
    public string HttpUrls { get; set; } = "http://127.0.0.1:8787";

    /// <summary>
    /// Optional shared key. When set, every /api request must send it in the X-Api-Key header.
    /// Required (at least <see cref="MinApiKeyLength"/> characters) when HttpUrls listens on anything other than loopback.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Latest snapshot is also written here (atomically) for file-based consumers.</summary>
    public string SnapshotFile { get; set; } = "output/snapshot.json";

    /// <summary>Status-change events are appended here as JSON lines.</summary>
    public string EventLogFile { get; set; } = "output/events.jsonl";

    public string LogDirectory { get; set; } = "logs";

    /// <summary>How many recent status-change events are kept in memory for /api/v1/events.</summary>
    public int MaxEventsInMemory { get; set; } = 1000;

    /// <summary>Optional push of snapshots to the Manajoy backend (the club LAN is not reachable from outside).</summary>
    public PushOptions Push { get; set; } = new();

    /// <summary>Daily GameClass reports sent to Manajoy next to the live push (owner's «Hisobot» in ManaJoy Biznes).</summary>
    public AnalyticsPushOptions Analytics { get; set; } = new();
}

public sealed class AnalyticsPushOptions
{
    /// <summary>On by default since 0.9.0; the owner turns it off in the panel (Bridge__Analytics__Enabled=false).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the last days are recomputed and, when they changed, sent again.</summary>
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>Days per request while the old history is sent (newest first).</summary>
    public int BatchDays { get; set; } = 14;

    /// <summary>A year of history on MSDE can take a while; one batch is far less.</summary>
    public int CommandTimeoutSeconds { get; set; } = 60;
}

public sealed class PushOptions
{
    /// <summary>
    /// e.g. https://api.manajoy.uz/integrations/gameclass/{seller_id}/snapshot. Push is off when empty.
    /// Plain http is accepted only for loopback / private (LAN) addresses.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>Per-club bridge key (sent as X-Bridge-Key). Prefer <see cref="KeyFile"/> or Bridge__Push__Key.</summary>
    public string? Key { get; set; }

    /// <summary>DPAPI (LocalMachine) protected key file next to the exe, written by install-service.ps1.</summary>
    public string KeyFile { get; set; } = "push.secret";

    /// <summary>Push at least this often; a status change is pushed at the next poll.</summary>
    public int IntervalSeconds { get; set; } = 15;

    public int TimeoutSeconds { get; set; } = 10;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Url);
}

public enum BridgeDataProvider
{
    Auto,
    SqlClient,
    Odbc,
}

public sealed class SqlLoginOptions
{
    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// DPAPI (LocalMachine) protected password file, relative to the exe. Used when <see cref="Password"/> is empty.
    /// The install folder is restricted to Administrators and SYSTEM.
    /// </summary>
    public string PasswordFile { get; set; } = "bridge.secret";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(UserName);
}
