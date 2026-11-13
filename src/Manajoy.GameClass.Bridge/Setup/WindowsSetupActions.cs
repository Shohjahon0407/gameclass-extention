using System.Runtime.Versioning;
using System.Security.Principal;
using Manajoy.GameClass.Bridge.Hosting;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>The real setup steps on the GameClass PC.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSetupActions(
    SetupEnvironment environment,
    ServiceManager service,
    LocalBridgeClient localBridge,
    ManajoyApiClient manajoy,
    IHttpClientFactory httpClients,
    ILogger<WindowsSetupActions> logger) : ISetupActions
{
    private static readonly TimeSpan ManajoyStatusCacheFor = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiskHealthMaxAge = TimeSpan.FromMinutes(15);

    // Taken once, right at start, while the exe that was double-clicked is surely readable (see SetupCopy).
    private readonly Task<SelfCheckInfo> _selfCheck = Task.Run(() => SetupCopy.Run(service.InstallDir, ServiceManager.IsInstalled, logger));

    // The generated password of the read-only login lives only in memory between the
    // "database" and "install" steps; afterwards it exists only DPAPI-encrypted on disk.
    private string? _pendingSqlPassword;
    private DatabaseRequest? _pendingDatabase;
    private (DateTime At, ManajoyInfo Info)? _manajoyCache;
    private LayoutCoordinator? _layout;
    private Analytics.IAnalyticsSource? _analytics;

    private LayoutCoordinator Layout
    {
        get
        {
            if (!ServiceManager.IsInstalled)
            {
                throw new SetupException("not_installed");
            }

            return _layout ??= new LayoutCoordinator(
                new FileLayoutStore(Path.Combine(service.InstallDir, FileLayoutStore.FileName)), CreateLayoutRemote);
        }
    }

    public async Task<SetupState> GetStateAsync(CancellationToken cancellationToken)
    {
        var selfCheck = await _selfCheck;
        var installed = ServiceManager.IsInstalled;
        var env = installed ? SetupEnvironment.ReadServiceEnvironment() : new Dictionary<string, string>();
        env.TryGetValue(SetupEnvironment.EnvPushUrl, out var pushUrl);
        var club = installed ? service.ReadClubInfo() : null;

        using var identity = WindowsIdentity.GetCurrent();
        return new SetupState(
            Version: BridgeInfo.Version,
            MachineName: Environment.MachineName,
            OsName: Preflight.OsName(),
            IsWindows: true,
            IsAdmin: new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            Demo: false,
            Installed: installed,
            ServiceStatus: ServiceManager.ServiceStatus(),
            InstallDir: service.InstallDir,
            RunningFromInstallDir: service.RunningFromInstallDir,
            DatabaseReady: _pendingSqlPassword is not null,
            SqlLoginConfigured: env.ContainsKey(SetupEnvironment.EnvSqlUser) && service.SqlSecretExists,
            Paired: !string.IsNullOrEmpty(pushUrl),
            SellerId: SetupEnvironment.SellerIdFromPushUrl(pushUrl) ?? club?.SellerId,
            ClubName: club?.ClubName,
            ApiBase: environment.ApiBase,
            SelfCheck: selfCheck,
            AutoStart: AutoStart.IsEnabled());
    }

    public Task<SetupState> SetAutoStartAsync(AutoStartRequest request, CancellationToken cancellationToken)
    {
        if (request.Enabled)
        {
            // The installed copy, not a download that may be deleted later.
            AutoStart.Enable(File.Exists(service.InstalledExe) ? service.InstalledExe : SetupEnvironment.CurrentExePath);
        }
        else
        {
            AutoStart.Disable();
        }

        logger.LogInformation("Open the panel at sign-in: {Enabled}", request.Enabled);
        return GetStateAsync(cancellationToken);
    }

    public async Task<PreflightReport> RunPreflightAsync(CancellationToken cancellationToken)
    {
        var report = await Preflight.RunAsync(environment, manajoy, cancellationToken);
        var checks = report.Checks.Concat(SetupCopy.Checks(await _selfCheck)).ToList();
        return new PreflightReport(checks, Preflight.Verdict(checks));
    }

    public Task<DatabaseResult> ConnectDatabaseAsync(DatabaseRequest request, CancellationToken cancellationToken)
    {
        var outcome = ReadOnlyLoginProvisioner.Provision(request, SetupEnvironment.ReadOnlyLogin);
        if (outcome.Success)
        {
            _pendingSqlPassword = outcome.Password;
            _pendingDatabase = request;
        }

        logger.LogInformation("Read-only login {Login}: {Result}", SetupEnvironment.ReadOnlyLogin, outcome.Success ? "verified" : "verification FAILED");
        return Task.FromResult(new DatabaseResult(outcome.Success, SetupEnvironment.ReadOnlyLogin, outcome.Checks));
    }

    public async Task<InstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        var password = _pendingSqlPassword ?? throw new SetupException("database_step_required");
        var result = await service.InstallAsync(SetupEnvironment.ReadOnlyLogin, password,
            _pendingDatabase?.Server, _pendingDatabase?.Database, cancellationToken);
        if (result.Success)
        {
            _pendingSqlPassword = null;
        }

        return result;
    }

    public Task<InstallResult> UpdateAsync(CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled || !service.SqlSecretExists)
        {
            // Nothing to keep: the full wizard (database step) is needed.
            throw new SetupException("database_step_required");
        }

        // Never "update" the service back to an older version (an old download left open).
        if (service.InstalledVersion() is { } installed && Version.TryParse(BridgeInfo.Version.Split('+')[0], out var mine) && installed > mine)
        {
            throw new SetupException("update_older", $"{BridgeInfo.Version} < {installed}");
        }

        return service.UpdateAsync(cancellationToken);
    }

    public async Task<PairResult> PairAsync(PairRequest request, CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled)
        {
            throw new SetupException("not_installed");
        }

        var paired = await manajoy.PairAsync(environment.ApiBase, request.Code, cancellationToken);
        var pushUrl = environment.ApiBase + paired.PushPath;
        logger.LogInformation("Paired with Manajoy club #{SellerId} ({Club})", paired.SellerId, paired.ClubName);

        var push = await service.ConfigurePushAsync(pushUrl, paired.BridgeKey, paired.SellerId, paired.ClubName, cancellationToken);
        _manajoyCache = null;
        return new PairResult(paired.SellerId, paired.ClubName, paired.LiveEnabled, push);
    }

    public async Task<DashboardData> GetDashboardAsync(CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled)
        {
            return new DashboardData(null, null, null, null, DateTimeOffset.Now, Disk: DiskHealth.Latest(DiskHealthMaxAge));
        }

        var reading = await localBridge.ReadAsync(includePcs: true, cancellationToken);
        return new DashboardData(ServiceManager.ServiceStatus(), reading.Local, reading.Push,
            await GetManajoyInfoAsync(cancellationToken), DateTimeOffset.Now, GameClassWindow(), DiskHealth.Latest(DiskHealthMaxAge));
    }

    /// <summary>How many GCServer processes run in this desktop session and whether the only one is hidden in the tray.</summary>
    private static GameClassWindowInfo? GameClassWindow()
    {
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var processes = System.Diagnostics.Process.GetProcessesByName("GCServer");
        try
        {
            var mine = processes.Where(p => p.SessionId == session).ToList();
            return new GameClassWindowInfo(mine.Count, mine.Count == 1 && mine[0].MainWindowHandle == IntPtr.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public Task<StepsResult> DisablePushAsync(CancellationToken cancellationToken)
    {
        _manajoyCache = null;
        return service.DisablePushAsync(cancellationToken);
    }

    public Task<StepsResult> RestartServiceAsync(CancellationToken cancellationToken) => service.RestartServiceAsync(cancellationToken);

    public async Task<ReportResult> CreateReportAsync(CancellationToken cancellationToken)
    {
        var preflight = await RunPreflightAsync(cancellationToken);
        var state = await GetStateAsync(cancellationToken);
        return await DiagnosticReport.CreateAsync(service.InstallDir, httpClients.CreateClient(LocalBridgeClient.HttpClientName),
            preflight, state, cancellationToken);
    }

    public Task<StepsResult> UninstallAsync(UninstallRequest request, CancellationToken cancellationToken) =>
        service.UninstallAsync(request, cancellationToken);

    public Task<LayoutView> GetLayoutAsync(CancellationToken cancellationToken) => Layout.LoadAsync(cancellationToken);

    public Task<LayoutView> SaveLayoutAsync(LayoutSaveRequest request, CancellationToken cancellationToken) =>
        Layout.SaveAsync(request, cancellationToken);

    public Task<LayoutView> ResolveLayoutAsync(LayoutResolveRequest request, CancellationToken cancellationToken) =>
        Layout.ResolveAsync(request.UseServer, cancellationToken);

    public async Task<Pricing.PricingInfo> GetPricingAsync(int pc, CancellationToken cancellationToken) =>
        CashierService.Pricing(await PricingContextAsync(cancellationToken), pc, () => DateTime.Now);

    public async Task<Pricing.QuoteResult> QuoteAsync(Pricing.QuoteRequest request, CancellationToken cancellationToken) =>
        CashierService.Quote(await PricingContextAsync(cancellationToken), request, () => DateTime.Now);

    /// <summary>
    /// From the service's own snapshot. The setup window runs on the GameClass server PC, so its clock is the one
    /// GameClass prices with. Null when the service is down or older than this window (then the UI asks to update).
    /// </summary>
    private async Task<Pricing.PricingContext?> PricingContextAsync(CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled)
        {
            throw new SetupException("not_installed");
        }

        return await localBridge.ReadSnapshotAsync(cancellationToken) is { } snapshot
            ? Pricing.PricingContext.FromSnapshot(snapshot)
            : null;
    }

    public Bar.IBarSource BarSource() => (Bar.IBarSource)AnalyticsSource();

    public async Task<Bar.BarBackup?> FetchBarBackupAsync(CancellationToken cancellationToken)
    {
        var env = SetupEnvironment.ReadServiceEnvironment();
        if (!env.TryGetValue(SetupEnvironment.EnvPushUrl, out var pushUrl) || Hosting.AnalyticsPushWorker.BarEntriesUrlFromPushUrl(pushUrl) is not { } url
            || service.ReadPushKey() is not { } key)
        {
            return null;
        }

        return await manajoy.GetBarBackupAsync(url, key, cancellationToken);
    }

    public async Task<ReportsPushInfo> GetReportsPushAsync(CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled)
        {
            return new ReportsPushInfo(false, false, null);
        }

        var paired = SetupEnvironment.ReadServiceEnvironment().ContainsKey(SetupEnvironment.EnvPushUrl);
        return new ReportsPushInfo(paired, ServiceManager.ReportsPushEnabled(), await localBridge.ReadReportsPushAsync(cancellationToken));
    }

    public async Task<ReportsPushInfo> SetReportsPushAsync(ReportsPushRequest request, CancellationToken cancellationToken)
    {
        if (!ServiceManager.IsInstalled)
        {
            throw new SetupException("not_installed");
        }

        await service.SetReportsPushAsync(request.Enabled, cancellationToken);
        logger.LogInformation("Daily reports to Manajoy: {Enabled}", request.Enabled);
        return await GetReportsPushAsync(cancellationToken);
    }

    /// <summary>
    /// The service's connection (server, database, read-only login and its DPAPI password from the install folder),
    /// opened here in the window: money must not go through the service's :8787 API, which any local user can read.
    /// </summary>
    public Analytics.IAnalyticsSource AnalyticsSource()
    {
        if (_analytics is not null)
        {
            return _analytics;
        }

        if (!ServiceManager.IsInstalled)
        {
            throw new SetupException("not_installed");
        }

        var env = SetupEnvironment.ReadServiceEnvironment();
        var secret = Path.Combine(service.InstallDir, environment.SqlSecretFile);
        if (!env.TryGetValue(SetupEnvironment.EnvSqlUser, out var user) || !File.Exists(secret))
        {
            throw new SetupException("database_step_required");
        }

        var options = new Configuration.BridgeOptions
        {
            Server = env.GetValueOrDefault(SetupEnvironment.EnvServer, SetupEnvironment.DefaultServer),
            Database = env.GetValueOrDefault(SetupEnvironment.EnvDatabase, SetupEnvironment.DefaultDatabase),
            CommandTimeoutSeconds = 60,
            SqlLogin = new Configuration.SqlLoginOptions
            {
                UserName = user,
                Password = Configuration.SqlPasswordFile.Unprotect(File.ReadAllBytes(secret)),
            },
        };
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var connections = new Data.GameClassConnectionFactory(wrapped,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Data.GameClassConnectionFactory>.Instance);
        return _analytics = new Analytics.AnalyticsReader(new Data.ReadOnlyGameClassDb(connections, wrapped));
    }

    /// <summary>Null when not paired (the map then stays local until pairing).</summary>
    private ManajoyLayoutRemote? CreateLayoutRemote()
    {
        var env = SetupEnvironment.ReadServiceEnvironment();
        if (!env.TryGetValue(SetupEnvironment.EnvPushUrl, out var pushUrl) || SetupEnvironment.LayoutUrlFromPushUrl(pushUrl) is not { } layoutUrl)
        {
            return null;
        }

        return service.ReadPushKey() is { } key ? new ManajoyLayoutRemote(manajoy, layoutUrl, key) : null;
    }

    private async Task<ManajoyInfo?> GetManajoyInfoAsync(CancellationToken cancellationToken)
    {
        var env = SetupEnvironment.ReadServiceEnvironment();
        if (!env.TryGetValue(SetupEnvironment.EnvPushUrl, out var pushUrl) || SetupEnvironment.StatusUrlFromPushUrl(pushUrl) is not { } statusUrl)
        {
            return null;
        }

        if (_manajoyCache is { } cached && DateTime.UtcNow - cached.At < ManajoyStatusCacheFor)
        {
            return cached.Info;
        }

        var key = service.ReadPushKey();
        var info = key is null
            ? new ManajoyInfo(false, null, null, null, null, null, null, null, null, "key_missing")
            : await manajoy.GetStatusAsync(statusUrl, key, cancellationToken);
        _manajoyCache = (DateTime.UtcNow, info);
        return info;
    }
}
