namespace Manajoy.GameClass.Bridge.Setup;

// Wire model of the setup window's local API (camelCase JSON). Human-readable texts live in the UI
// (uz/ru dictionaries keyed by `id` / error `code`); the server only adds technical `detail`.

/// <summary>Serialized camelCase ("ok", "warn", ...) by the setup host's JSON options.</summary>
public enum CheckLevel
{
    Ok,
    Warn,
    Fail,
    Info,
}

public sealed record CheckResult(string Id, CheckLevel Level, string Detail = "");

public sealed record PreflightReport(IReadOnlyList<CheckResult> Checks, CheckLevel Verdict);

public sealed record DatabaseRequest(
    string? AdminUser,
    string? AdminPassword,
    bool WindowsAuth,
    string? Server,
    string? Database);

public sealed record DatabaseResult(bool Success, string Login, IReadOnlyList<CheckResult> Checks);

public sealed record InstallResult(bool Success, IReadOnlyList<CheckResult> Steps, LocalSummary? Local);

public sealed record PairRequest(string Code);

public sealed record PairResult(int SellerId, string ClubName, bool LiveEnabled, PushInfo Push);

public sealed record UninstallRequest(bool DropLogin, string? AdminUser, string? AdminPassword, bool WindowsAuth);

public sealed record StepsResult(bool Success, IReadOnlyList<CheckResult> Steps);

public sealed record ReportResult(string Path, long SizeBytes);

public sealed record SetupState(
    string Version,
    string MachineName,
    string OsName,
    bool IsWindows,
    bool IsAdmin,
    bool Demo,
    bool Installed,
    string? ServiceStatus,
    string InstallDir,
    bool RunningFromInstallDir,
    bool DatabaseReady,
    bool SqlLoginConfigured,
    bool Paired,
    int? SellerId,
    string? ClubName,
    string ApiBase,
    SelfCheckInfo? SelfCheck = null,
    bool? AutoStart = null);

/// <summary>
/// «Hisobot» sent to Manajoy (ManaJoy Biznes): on by default since 0.9.0. <see cref="Report"/> is the service's last
/// push (null when the service is not reachable or older than 0.9.0).
/// </summary>
public sealed record ReportsPushInfo(bool Paired, bool Enabled, Hosting.AnalyticsPushReport? Report);

public sealed record ReportsPushRequest(bool Enabled);

/// <summary>"Open the panel when the PC starts" on or off.</summary>
public sealed record AutoStartRequest(bool Enabled);

/// <summary>
/// The setup exe's own copy and the file it was first started from (see <c>SetupCopy</c>).
/// CopyStatus: created / updated / in_place / running_copy / installed / source_unreadable / verify_failed / failed.
/// DownloadStatus: none / ok / unreadable (disk) / changed / missing (antivirus).
/// </summary>
public sealed record SelfCheckInfo(
    string CopyStatus,
    string? CopyPath,
    string? CopyDetail,
    string DownloadStatus,
    string? DownloadPath,
    string? DownloadDetail,
    string ExePath,
    string? ExeSha256);

/// <summary>System disk: recent disk/NTFS errors in the event log, the dirty bit, the drives' own health (see <c>DiskHealth</c>).</summary>
public sealed record DiskHealthInfo(
    bool Problem,
    int Events,
    bool Dirty,
    IReadOnlyList<string> Findings,
    DateTimeOffset? LastEventAt,
    IReadOnlyList<string> Errors);

public sealed record DashboardData(
    string? ServiceStatus,
    LocalSummary? Local,
    PushInfo? Push,
    ManajoyInfo? Manajoy,
    DateTimeOffset GeneratedAt,
    GameClassWindowInfo? GameClassWindow = null,
    DiskHealthInfo? Disk = null);

/// <summary>
/// GCServer as seen from the operator's desktop (read-only: the process list, nothing is touched).
/// GCServer hides itself in the notification area when minimised; operators then think it closed, start it
/// again, and the second copy fails with "UDP port 3775" (really 3773, held by the first).
/// </summary>
public sealed record GameClassWindowInfo(int Running, bool Hidden);

/// <summary>What the local bridge service reports (health + a compact PC list for the grid).</summary>
public sealed record LocalSummary(
    bool Reachable,
    string Status,
    int? AgeSeconds,
    string? LastError,
    string? DbProvider,
    bool? ServerAlive,
    int? HeartbeatAgeSeconds,
    bool HeartbeatMissing,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<PcTile> Pcs,
    string? BridgeVersion);

public sealed record PcTile(
    int Number,
    string Name,
    string Zone,
    string State,
    string GcState,
    int? TimeLeftMinutes,
    bool Postpay,
    DateTimeOffset? NextReservationAt);

/// <summary>Window preferences that must survive restarts (the port, hence localStorage, changes every launch).</summary>
public sealed record SetupPrefs(string? Lang, string? DashTab, string? Rail = null);

public sealed record PushInfo(
    bool Enabled,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    int? LastHttpStatus,
    string? LastError,
    int ConsecutiveFailures);

/// <summary>Manajoy's view of this club, fetched with the bridge key.</summary>
public sealed record ManajoyInfo(
    bool Reachable,
    string? ClubName,
    int? SellerId,
    bool? LiveEnabled,
    int? StationsTotal,
    int? StationsConfirmed,
    int? StationsUnmapped,
    string? LiveState,
    int? LiveAgeSeconds,
    string? Error);

/// <summary>A failure the UI explains in the user's language (by <see cref="Code"/>); <see cref="Detail"/> is technical.</summary>
public sealed class SetupException(string code, string? detail = null, Exception? inner = null)
    : Exception(detail ?? code, inner)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}

internal interface ISetupActions
{
    Task<SetupState> GetStateAsync(CancellationToken cancellationToken);

    Task<PreflightReport> RunPreflightAsync(CancellationToken cancellationToken);

    Task<DatabaseResult> ConnectDatabaseAsync(DatabaseRequest request, CancellationToken cancellationToken);

    Task<InstallResult> InstallAsync(CancellationToken cancellationToken);

    /// <summary>Replace an older installed bridge with this exe, keeping its login, secrets and pairing.</summary>
    Task<InstallResult> UpdateAsync(CancellationToken cancellationToken);

    Task<PairResult> PairAsync(PairRequest request, CancellationToken cancellationToken);

    Task<DashboardData> GetDashboardAsync(CancellationToken cancellationToken);

    Task<StepsResult> DisablePushAsync(CancellationToken cancellationToken);

    Task<StepsResult> RestartServiceAsync(CancellationToken cancellationToken);

    Task<ReportResult> CreateReportAsync(CancellationToken cancellationToken);

    Task<StepsResult> UninstallAsync(UninstallRequest request, CancellationToken cancellationToken);

    /// <summary>The club map, pulled from Manajoy when paired (layout.json otherwise).</summary>
    Task<LayoutView> GetLayoutAsync(CancellationToken cancellationToken);

    Task<LayoutView> SaveLayoutAsync(LayoutSaveRequest request, CancellationToken cancellationToken);

    /// <summary>After a conflict: take Manajoy's map (<c>UseServer</c>) or overwrite it with this one.</summary>
    Task<LayoutView> ResolveLayoutAsync(LayoutResolveRequest request, CancellationToken cancellationToken);

    /// <summary>The cashier assistant: what each tariff of this PC costs now (read-only, GameClass's own pricing).</summary>
    Task<Pricing.PricingInfo> GetPricingAsync(int pc, CancellationToken cancellationToken);

    Task<Pricing.QuoteResult> QuoteAsync(Pricing.QuoteRequest request, CancellationToken cancellationToken);

    /// <summary>Turns the sign-in task that opens the panel on or off; returns the new state.</summary>
    Task<SetupState> SetAutoStartAsync(AutoStartRequest request, CancellationToken cancellationToken);

    /// <summary>GameClass history for the owner's reports (read-only, over the service's own read-only login).</summary>
    Analytics.IAnalyticsSource AnalyticsSource();

    /// <summary>The bar price list and sales, read-only (the same connection as the reports).</summary>
    Bar.IBarSource BarSource();

    /// <summary>The stock book Manajoy keeps for this club (after a PC change); null when not paired or unreachable.</summary>
    Task<Bar.BarBackup?> FetchBarBackupAsync(CancellationToken cancellationToken);

    Task<ReportsPushInfo> GetReportsPushAsync(CancellationToken cancellationToken);

    /// <summary>Turns the daily report push on or off (service environment) and restarts the service.</summary>
    Task<ReportsPushInfo> SetReportsPushAsync(ReportsPushRequest request, CancellationToken cancellationToken);
}
