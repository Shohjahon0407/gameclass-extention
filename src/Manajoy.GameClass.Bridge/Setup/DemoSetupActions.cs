using Manajoy.GameClass.Bridge.Hosting;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// --setup-demo: the whole wizard and dashboard with realistic fake data, on any OS, changing nothing.
/// Used to review the UI (screenshots) and to show the flow to club owners. Pairing code TEST-2345 succeeds.
/// </summary>
internal sealed class DemoSetupActions : ISetupActions
{
    public const string DemoPairingCode = "TEST2345";

    private static readonly string[] Zones = ["VIP", "VIP", "VIP", "VIP", "Standard", "Standard", "Standard", "Standard",
        "Standard", "Standard", "Standard", "Standard", "Bootcamp", "Bootcamp", "Bootcamp", "Bootcamp", "Bootcamp",
        "PlayStation", "PlayStation", "Standard", "Standard", "Standard", "Standard", "Standard"];

    private readonly DateTimeOffset _started = DateTimeOffset.Now;
    private readonly DemoLayoutRemote _manajoyMap = new();
    private readonly LayoutCoordinator _layout;
    private bool _databaseReady;
    private bool _installed;
    private bool _paired;

    public DemoSetupActions()
    {
        _layout = new LayoutCoordinator(new MemoryLayoutStore(DemoLayout.Doc()), () => _paired ? _manajoyMap : null);
    }

    public Task<SetupState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(new SetupState(
        BridgeInfo.Version, "CYBER-ADMIN", "Windows 10 Pro (demo)", OperatingSystem.IsWindows(), true, true,
        _installed, _installed ? "Running" : null, @"C:\Program Files\Manajoy\GameClassBridge", false,
        _databaseReady, _installed, _paired, _paired ? 26 : null, _paired ? "Cyber Arena" : null, SetupEnvironment.DefaultApiBase,
        new SelfCheckInfo("created", @"C:\ProgramData\Manajoy\GameClassBridge\setup\ManajoyGameClass-Setup.exe", null,
            "none", null, null, @"C:\Users\operator\Downloads\ManajoyGameClass-Setup.exe", null),
        _autoStart));

    private bool _autoStart = true;

    private readonly Analytics.DemoAnalyticsSource _analytics = new(Zones);

    public Analytics.IAnalyticsSource AnalyticsSource() => _analytics;

    public Bar.IBarSource BarSource() => _analytics;

    /// <summary>A stock book that shows every state: plenty, running low, empty and "count again".</summary>
    public static void SeedBar(Bar.BarStore store)
    {
        var now = DateTime.Now;
        store.Add(1, "Coca-Cola 0.5", Bar.BarEntryKind.Count, 60, null, now.AddDays(-5));
        store.Add(1, "Coca-Cola 0.5", Bar.BarEntryKind.Receive, 48, "Yetkazib beruvchi", now.AddDays(-2));
        store.SetItem(1, 20, null);
        store.Add(2, "Flash", Bar.BarEntryKind.Count, 120, null, now.AddDays(-5));
        store.SetItem(2, 15, null);
        store.Add(5, "Lays", Bar.BarEntryKind.Count, 40, null, now.AddDays(-2));
        store.SetItem(5, 10, null);
        store.Add(6, "Snickers", Bar.BarEntryKind.Count, 80, null, now.AddDays(-4));
        store.Add(6, "Snickers", Bar.BarEntryKind.WriteOff, 2, "Muddati o'tgan", now.AddDays(-1));
        store.SetItem(6, 10, null);
        store.Add(8, "Burger", Bar.BarEntryKind.Count, 6, null, now.AddDays(-1));
        store.SetItem(8, 5, null);
        store.Add(3, "Qahva", Bar.BarEntryKind.Count, 200, null, now.AddDays(-6));
    }

    public Task<Bar.BarBackup?> FetchBarBackupAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Bar.BarBackup?>(_paired ? new Bar.BarBackup([], []) : null);

    private bool _reportsPush = true;

    public Task<ReportsPushInfo> GetReportsPushAsync(CancellationToken cancellationToken) => Task.FromResult(new ReportsPushInfo(
        _paired, _reportsPush, _paired && _reportsPush
            ? new Hosting.AnalyticsPushReport(true, DateTimeOffset.Now.AddMinutes(-3), DateTimeOffset.Now.AddMinutes(-3), 200, null, 0, true, null, 120)
            : null));

    public Task<ReportsPushInfo> SetReportsPushAsync(ReportsPushRequest request, CancellationToken cancellationToken)
    {
        _reportsPush = request.Enabled;
        return GetReportsPushAsync(cancellationToken);
    }

    public Task<SetupState> SetAutoStartAsync(AutoStartRequest request, CancellationToken cancellationToken)
    {
        _autoStart = request.Enabled;
        return GetStateAsync(cancellationToken);
    }

    public async Task<PreflightReport> RunPreflightAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(900, cancellationToken);
        CheckResult[] checks =
        [
            new("windows", CheckLevel.Ok, "Windows 10 Pro (build 19045, 64-bit)"),
            new("administrator", CheckLevel.Ok, @"CYBER-ADMIN\operator"),
            new("sql_service", CheckLevel.Ok, "MSSQLSERVER: Running"),
            new("sql_version", CheckLevel.Ok, "8.00.760 (SQL Server 2000 / MSDE)"),
            new("odbc_driver", CheckLevel.Ok, @"C:\Windows\SysWOW64\SQLSRV32.dll"),
            new("gameclass_server", CheckLevel.Ok, @"3.85.6.2 C:\Program Files (x86)\GameClass3\Server"),
            new("gcserver_running", CheckLevel.Ok, "PID 4120"),
            new("locale", CheckLevel.Warn, "system ru-RU, user en-US"),
            new("port", CheckLevel.Ok, "8787"),
            new("disk", CheckLevel.Ok, "41 210 MB C:\\"),
            new("disk_health", CheckLevel.Ok, "7 days, no disk errors"),
            new("internet", CheckLevel.Ok, SetupEnvironment.DefaultApiBase),
            new("setup_copy", CheckLevel.Ok, @"C:\ProgramData\Manajoy\GameClassBridge\setup\ManajoyGameClass-Setup.exe"),
        ];
        return new PreflightReport(checks, Preflight.Verdict(checks));
    }

    public async Task<DatabaseResult> ConnectDatabaseAsync(DatabaseRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(1400, cancellationToken);
        if (!request.WindowsAuth && request.AdminPassword is { Length: > 0 } password && password != "sa")
        {
            throw new SetupException("admin_login_failed", "Login failed for user 'sa'. (18456)");
        }

        _databaseReady = true;
        return new DatabaseResult(true, SetupEnvironment.ReadOnlyLogin,
        [
            new("login_created", CheckLevel.Ok, SetupEnvironment.ReadOnlyLogin),
            new("db_datareader_granted", CheckLevel.Ok, "GameClass"),
            new("execute_denied", CheckLevel.Ok, "105"),
            new("can_read", CheckLevel.Ok),
            new("is_datareader", CheckLevel.Ok),
            new("not_datawriter", CheckLevel.Ok),
            new("not_db_owner", CheckLevel.Ok),
            new("not_sysadmin", CheckLevel.Ok),
            new("write_refused", CheckLevel.Ok),
            new("no_procedure_executable", CheckLevel.Ok, "0"),
            new("procedure_write_refused", CheckLevel.Ok),
        ]);
    }

    public async Task<InstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        if (!_databaseReady)
        {
            throw new SetupException("database_step_required");
        }

        await Task.Delay(2500, cancellationToken);
        _installed = true;
        return new InstallResult(true,
        [
            new("stop_old", CheckLevel.Ok, "new"),
            new("copy_files", CheckLevel.Ok, @"C:\Program Files\Manajoy\GameClassBridge"),
            new("store_secret", CheckLevel.Ok, "DPAPI"),
            new("register_service", CheckLevel.Ok, SetupEnvironment.ServiceName),
            new("start_service", CheckLevel.Ok, "Running"),
            new("read_gameclass", CheckLevel.Ok, $"{Zones.Length} PC"),
            new("shortcut", CheckLevel.Ok, @"C:\Users\Public\Desktop\Manajoy GameClass.lnk"),
        ], Local());
    }

    public async Task<InstallResult> UpdateAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(2000, cancellationToken);
        return new InstallResult(true,
        [
            new("stop_old", CheckLevel.Ok, "stopped"),
            new("copy_files", CheckLevel.Ok, @"C:\Program Files\Manajoy\GameClassBridge"),
            new("register_service", CheckLevel.Ok, SetupEnvironment.ServiceName),
            new("start_service", CheckLevel.Ok, "Running"),
            new("read_gameclass", CheckLevel.Ok, $"{Zones.Length} PC"),
        ], Local());
    }

    public async Task<PairResult> PairAsync(PairRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(1600, cancellationToken);
        var code = new string(request.Code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (code != DemoPairingCode)
        {
            throw new SetupException("pair_invalid_code", "Kod noto'g'ri yoki muddati o'tgan.");
        }

        _paired = true;
        return new PairResult(26, "Cyber Arena", true, Push());
    }

    public Task<DashboardData> GetDashboardAsync(CancellationToken cancellationToken) => Task.FromResult(new DashboardData(
        _installed ? "Running" : null,
        _installed ? Local() : null,
        _installed ? Push() : null,
        _paired ? new ManajoyInfo(true, "Cyber Arena", 26, true, Zones.Length, Zones.Length - 2, 2, "ok", 4, null) : null,
        DateTimeOffset.Now));

    public Task<StepsResult> DisablePushAsync(CancellationToken cancellationToken)
    {
        _paired = false;
        return Task.FromResult(new StepsResult(true, [new CheckResult("push_disabled", CheckLevel.Ok)]));
    }

    public async Task<StepsResult> RestartServiceAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1200, cancellationToken);
        return new StepsResult(true, [new CheckResult("service_restarted", CheckLevel.Ok, "ok")]);
    }

    public async Task<ReportResult> CreateReportAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(800, cancellationToken);
        return new ReportResult(@"C:\Users\operator\Desktop\manajoy-bridge-report-CYBER-ADMIN-20261006-181500.zip", 184_320);
    }

    public async Task<StepsResult> UninstallAsync(UninstallRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(1500, cancellationToken);
        _installed = _paired = _databaseReady = false;
        List<CheckResult> steps = [new("service_removed", CheckLevel.Ok, SetupEnvironment.ServiceName)];
        if (request.DropLogin)
        {
            steps.Add(new("login_dropped", CheckLevel.Ok, SetupEnvironment.ReadOnlyLogin));
        }

        steps.Add(new("files_removed", CheckLevel.Ok, @"C:\Program Files\Manajoy\GameClassBridge"));
        return new StepsResult(true, steps);
    }

    public Task<LayoutView> GetLayoutAsync(CancellationToken cancellationToken) => _layout.LoadAsync(cancellationToken);

    public async Task<LayoutView> SaveLayoutAsync(LayoutSaveRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken);
        return await _layout.SaveAsync(request, cancellationToken);
    }

    public Task<LayoutView> ResolveLayoutAsync(LayoutResolveRequest request, CancellationToken cancellationToken) =>
        _layout.ResolveAsync(request.UseServer, cancellationToken);

    public Task<Pricing.PricingInfo> GetPricingAsync(int pc, CancellationToken cancellationToken) =>
        Task.FromResult(CashierService.Pricing(DemoPricing.Context(Local()), pc, () => DateTime.Now));

    public Task<Pricing.QuoteResult> QuoteAsync(Pricing.QuoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(CashierService.Quote(DemoPricing.Context(Local()), request, () => DateTime.Now));

    private PushInfo Push()
    {
        var now = DateTimeOffset.Now;
        return new PushInfo(_paired, _paired ? now.AddSeconds(-(now.Second % 15)) : null, now.AddSeconds(-(now.Second % 15)), _paired ? 200 : null, null, 0);
    }

    /// <summary>A club that changes a little every few seconds, so the dashboard looks alive.</summary>
    private LocalSummary Local()
    {
        var tick = (int)((DateTimeOffset.Now - _started).TotalSeconds / 6);
        var pcs = new List<PcTile>();
        for (var i = 0; i < Zones.Length; i++)
        {
            var number = i + 1;
            var phase = (number * 7 + tick) % 11;
            var (state, gcState) = number switch
            {
                22 => ("offline", "offline"),
                9 => ("disabled", "maintenance"),
                _ when phase < 6 => ("busy", "in_session"),
                _ => ("available", "free"),
            };
            int? left = state == "busy" ? 5 + (number * 13 + tick) % 170 : null;
            var postpay = state == "busy" && number % 9 == 4;
            DateTimeOffset? reservation = state == "available" && number % 5 == 0 ? DateTimeOffset.Now.AddMinutes(40 + number) : null;
            pcs.Add(new PcTile(number, number.ToString(System.Globalization.CultureInfo.InvariantCulture), Zones[i], state, gcState, postpay ? null : left, postpay, reservation));
        }

        var counts = new Dictionary<string, int> { ["total"] = pcs.Count };
        foreach (var state in new[] { "available", "busy", "offline", "disabled", "unknown" })
        {
            counts[state] = pcs.Count(pc => pc.State == state);
        }

        return new LocalSummary(true, "ok", 2, null, "Odbc", true, 3, false, counts, pcs, BridgeInfo.Version);
    }
}

/// <summary>A drawn 24-PC club for --setup-demo: VIP room, main hall, Bootcamp row, consoles, bar, desk and entrance.</summary>
internal static class DemoLayout
{
    public static ClubLayoutDoc Doc()
    {
        var pcs = new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
        void Put(int number, int x, int y) => pcs[number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new LayoutPoint(x, y);

        // VIP (1-4): a 2x2 room in the corner.
        Put(1, 100, 100); Put(2, 200, 100); Put(3, 100, 200); Put(4, 200, 200);
        // Standard (5-12): two facing rows.
        for (var i = 0; i < 4; i++)
        {
            Put(5 + i, 425 + i * 100, 100);
            Put(9 + i, 425 + i * 100, 200);
        }

        // Bootcamp (13-17): along the right wall.
        for (var i = 0; i < 5; i++)
        {
            Put(13 + i, 925, 100 + i * 100);
        }

        // PlayStation (18-19) by the window, Standard (20-24) third row.
        Put(18, 450, 550); Put(19, 575, 550);
        for (var i = 0; i < 5; i++)
        {
            Put(20 + i, 400 + i * 100, 350);
        }

        List<LayoutObject> objects =
        [
            new("area1", "area", 25, 25, 275, 275, "VIP zal", "purple"),
            new("area2", "area", 350, 25, 500, 375, "Zal", "blue"),
            new("area3", "area", 375, 475, 275, 150, "PlayStation", "teal"),
            new("area4", "area", 700, 475, 150, 150, "Bar", "orange"),
            new("area5", "area", 875, 25, 100, 550, "Bootcamp", "green"),
            new("wall1", "wall", 300, 25, 12, 275, null),
            new("wall2", "wall", 25, 300, 287, 12, null),
            new("desk1", "desk", 50, 450, 175, 75, "Kassa"),
            new("door1", "entrance", 50, 600, 125, 25, "Kirish"),
        ];
        return new ClubLayoutDoc(1, pcs, objects, DateTimeOffset.Now, null, false);
    }
}

/// <summary>Manajoy's side in demo mode: PCs 1-22 are mapped to stations (23-24 are not, like the dashboard says).</summary>
internal sealed class DemoLayoutRemote : ILayoutRemote
{
    private const int Mapped = 22;
    private Dictionary<string, LayoutPoint> _points;

    public DemoLayoutRemote()
    {
        _points = DemoLayout.Doc().Pcs.Where(pair => int.Parse(pair.Key, System.Globalization.CultureInfo.InvariantCulture) <= Mapped)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public Task<RemoteLayout> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Current());

    public Task<RemoteSaveResult> PutAsync(IReadOnlyDictionary<string, LayoutPoint> pcs, string? baseVersion, bool force, CancellationToken cancellationToken)
    {
        if (!force && baseVersion is not null && baseVersion != Current().Version)
        {
            throw new LayoutConflictException(Current());
        }

        var known = Enumerable.Range(1, Mapped).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        _points = pcs.Where(pair => known.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var skipped = pcs.Keys.Where(key => !known.Contains(key)).ToList();
        return Task.FromResult(new RemoteSaveResult(_points.Count, skipped, Current().Version));
    }

    private RemoteLayout Current()
    {
        var pcs = Enumerable.Range(1, Mapped)
            .Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Select(name => _points.TryGetValue(name, out var p) ? new RemotePc(name, p.X, p.Y) : new RemotePc(name, null, null))
            .ToList();
        var version = string.Join(';', pcs.Select(pc => $"{pc.Name}:{pc.X}:{pc.Y}")).GetHashCode(StringComparison.Ordinal).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        return new RemoteLayout(version, pcs, 1);
    }
}

/// <summary>Demo club prices (so'm): day/night rates, a night packet, a VIP hall; sessions consistent with the PC tiles.</summary>
internal static class DemoPricing
{
    private static readonly Pricing.PricingSettings Settings = new(2000, 300000, 50000);

    private static readonly Pricing.PricingTariff[] Tariffs =
    [
        new(1, "Standart", 1, 1, 500, false, 0, 0, 0,
        [
            new(1, "Kunduzgi", TimeSpan.Zero, TimeSpan.Zero, 12000, false, "1234567"),
            new(2, "Tungi", TimeSpan.FromHours(22), TimeSpan.FromHours(8), 8000, false, "1234567"),
            new(3, "3 soat+", TimeSpan.Zero, TimeSpan.Zero, 10000, false, "1234567", ">=/180"),
            new(4, "Tungi paket", TimeSpan.FromHours(22), TimeSpan.FromHours(8), 45000, true, "1234567"),
        ]),
        new(2, "VIP", 2, 1, 1000, false, 0, 0, 0,
        [
            new(5, "VIP", TimeSpan.Zero, TimeSpan.Zero, 20000, false, "1234567"),
            new(6, "VIP tungi paket", TimeSpan.FromHours(22), TimeSpan.FromHours(8), 90000, true, "1234567"),
        ]),
        new(3, "Konsol", 3, 1, 1000, false, 0, 0, 0,
        [
            new(7, "PlayStation", TimeSpan.Zero, TimeSpan.Zero, 25000, false, "1234567"),
        ]),
    ];

    public static Pricing.PricingContext Context(LocalSummary local)
    {
        var now = DateTime.Now;
        var start = now.AddMinutes(-23).AddSeconds(-now.Second);
        var pcs = new Dictionary<int, Pricing.PricingPc>();
        foreach (var pc in local.Pcs)
        {
            var group = pc.Zone switch { "VIP" => 2, "PlayStation" => 3, _ => 1 };
            Pricing.PricingSession? session = null;
            if (pc.State == "busy")
            {
                var tariff = Tariffs.First(t => t.GroupId == group);
                var calc = new Pricing.TariffCalculator(tariff, Settings);
                if (pc.Postpay)
                {
                    session = new(tariff.Id, tariff.Name, start, null, true, false, 0, 0, 0, 0);
                }
                else
                {
                    // Paid so that GameClass's own end lands near the tile's "time left".
                    var paid = calc.CalculateCost(start, now.AddMinutes(pc.TimeLeftMinutes ?? 30), clearSeconds: true);
                    var ends = start + calc.CalculateTimeLength(start, paid);
                    session = new(tariff.Id, tariff.Name, start, ends, false, false, paid, 0, 0, 0);
                }
            }

            IReadOnlyList<DateTime> reservations = pc.NextReservationAt is { } r ? [r.LocalDateTime] : [];
            pcs[pc.Number] = new Pricing.PricingPc(pc.Number, group, pc.State, session, reservations);
        }

        return new Pricing.PricingContext("so'm", Settings, Tariffs, pcs);
    }
}
