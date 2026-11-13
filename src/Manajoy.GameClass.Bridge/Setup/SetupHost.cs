using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Manajoy.GameClass.Bridge.Hosting;
using Microsoft.AspNetCore.Http.Json;

namespace Manajoy.GameClass.Bridge.Setup;

public sealed record SetupLaunchOptions(bool Demo, bool Headless, int? Port, string? ApiBase);

/// <summary>
/// The setup / dashboard window: a tiny web app on 127.0.0.1 (random port) opened in an Edge app window.
///
/// Security: the listener is loopback-only; every /api call needs the one-time token that is passed to the
/// browser in the URL fragment (never sent over HTTP, never logged), and the Host / Origin headers must name
/// this listener, so another web page in the same browser cannot drive the installer (CSRF / DNS rebinding).
/// The installed service's own API on :8787 stays GET-only; all changes happen in this elevated process.
/// </summary>
internal static class SetupHost
{
    private const string ResourcePrefix = "SetupUi/";
    private static readonly TimeSpan FirstContactTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(75);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public static async Task<int> RunAsync(SetupLaunchOptions launch)
    {
        SingleInstance? instance = null;
        if (!launch.Demo && !launch.Headless && OperatingSystem.IsWindows())
        {
            instance = SingleInstance.TryAcquire(out var existingUrl);
            if (instance is null && SingleInstance.CloseOlderWindows() > 0)
            {
                // An older version's window (e.g. a previous download still open) must not stay in charge:
                // it would offer to "update" the service back to its own, older version.
                instance = SingleInstance.TryAcquire(out existingUrl);
            }

            if (instance is null)
            {
                if (existingUrl is not null)
                {
                    BrowserLauncher.Open(existingUrl, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
                }

                return 0;
            }
        }

        using (instance)
        {
            return await RunHostAsync(launch, instance);
        }
    }

    private static async Task<int> RunHostAsync(SetupLaunchOptions launch, SingleInstance? instance)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Production",
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{launch.Port ?? 0}");
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        // HttpClient logs 4 lines per request at Information; the dashboard polls every 4 s.
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.Services.AddSingleton<ILoggerProvider>(_ =>
            new FileLoggerProvider(Path.Combine(SetupEnvironment.DataDirectory, "logs"), "setup"));

        builder.Services.ConfigureHttpJsonOptions(options => Configure(options.SerializerOptions));
        builder.Services.AddHttpClient(ManajoyApiClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"ManajoyGameClassSetup/{BridgeInfo.Version}");
        });
        builder.Services.AddHttpClient(LocalBridgeClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddTransient(sp => new ManajoyApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ManajoyApiClient.HttpClientName)));
        builder.Services.AddTransient(sp => new LocalBridgeClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(LocalBridgeClient.HttpClientName)));
        builder.Services.AddSingleton(new SetupEnvironment(launch.ApiBase ?? ResolveApiBase()));
        builder.Services.AddSingleton(new SetupSession(token));
        builder.Services.AddHttpClient("releases", client => client.Timeout = TimeSpan.FromMinutes(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton(sp => new OnlineUpdater(sp.GetRequiredService<IHttpClientFactory>().CreateClient("releases"), launch.Demo));
        builder.Services.AddSingleton(new PrefsStore(launch.Demo || !OperatingSystem.IsWindows()
            ? null
            : Path.Combine(SetupEnvironment.DataDirectory, SetupEnvironment.PrefsFile)));
        builder.Services.AddSingleton(new Analytics.AnalyticsAccess(launch.Demo || !OperatingSystem.IsWindows()
            ? null
            : Analytics.AnalyticsAccess.DefaultPath));
        var barStore = new Bar.BarStore(launch.Demo || !OperatingSystem.IsWindows() ? null : Bar.BarStore.DefaultPath);
        if (launch.Demo)
        {
            DemoSetupActions.SeedBar(barStore);
        }

        builder.Services.AddSingleton(barStore);
        builder.Services.AddSingleton(sp => new Bar.BarService(() => sp.GetRequiredService<ISetupActions>().BarSource(),
            sp.GetRequiredService<Bar.BarStore>()));
        builder.Services.AddSingleton(sp => new Analytics.AnalyticsService(() => sp.GetRequiredService<ISetupActions>().AnalyticsSource()));
        if (launch.Demo)
        {
            builder.Services.AddSingleton<ISetupActions, DemoSetupActions>();
        }
        else if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton<ServiceManager>();
            builder.Services.AddSingleton<ISetupActions, WindowsSetupActions>();
        }

        var app = builder.Build();
        var session = app.Services.GetRequiredService<SetupSession>();
        app.Use((context, next) => Guard(context, next, session));
        MapStaticFiles(app);
        MapApi(app, session);

        await app.StartAsync();
        var baseUrl = app.Urls.First().TrimEnd('/');
        session.Port = new Uri(baseUrl).Port;
        var windowUrl = $"{baseUrl}/#k={token}";
        app.Logger.LogInformation("{Name} {Version} setup window on {Url} ({Mode})", BridgeInfo.DisplayName, BridgeInfo.Version, baseUrl,
            launch.Demo ? "demo" : "live");

        if (launch.Headless)
        {
            // For remote checks (SSH tunnel) and automation; the token is printed only to this console.
            Console.WriteLine($"SETUP_URL={windowUrl}");
        }
        else
        {
            if (OperatingSystem.IsWindows())
            {
                instance?.Publish(windowUrl);
            }

            BrowserLauncher.Open(windowUrl, app.Logger);
            NativeConsole.HideIfOwned();
        }

        _ = WatchHeartbeatAsync(app, session, launch.Headless);
        await app.WaitForShutdownAsync();
        return 0;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>An explicit --api-base wins; otherwise keep the backend the bridge is already paired with.</summary>
    private static string ResolveApiBase()
    {
        if (OperatingSystem.IsWindows())
        {
            var env = SetupEnvironment.ReadServiceEnvironment();
            if (env.TryGetValue(SetupEnvironment.EnvPushUrl, out var pushUrl))
            {
                var index = pushUrl.IndexOf("/integrations/gameclass/", StringComparison.Ordinal);
                if (index > 0)
                {
                    return pushUrl[..index];
                }
            }
        }

        return SetupEnvironment.DefaultApiBase;
    }

    private static async Task Guard(HttpContext context, Func<Task> next, SetupSession session)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cache-Control"] = "no-store";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        if (!session.IsAllowedHost(context.Request.Host.Value))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if ((origin.Length > 0 && !session.IsAllowedOrigin(origin)) || !session.IsValidToken(context.Request.Headers["X-Setup-Token"]))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            session.Touch();
        }

        await next();
    }

    private static void MapStaticFiles(WebApplication app)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var files = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..], StringComparer.OrdinalIgnoreCase);

        IResult Serve(string file)
        {
            if (!files.TryGetValue(file, out var resource) || !ContentTypes.TryGetValue(Path.GetExtension(file), out var contentType))
            {
                return Results.NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resource)!;
            return Results.Stream(stream, contentType);
        }

        app.MapGet("/", () => Serve("index.html"));
        app.MapGet("/{file}", (string file) => Serve(file));
    }

    private static void MapApi(WebApplication app, SetupSession session)
    {
        var api = app.MapGroup("/api/setup");
        api.MapGet("/state", (ISetupActions a, CancellationToken ct) => Run(session, () => a.GetStateAsync(ct), exclusive: false));
        api.MapPost("/preflight", (ISetupActions a, CancellationToken ct) => Run(session, () => a.RunPreflightAsync(ct)));
        api.MapPost("/database", (DatabaseRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.ConnectDatabaseAsync(r, ct)));
        api.MapPost("/install", (ISetupActions a, CancellationToken ct) => Run(session, () => a.InstallAsync(ct)));
        api.MapPost("/update", (ISetupActions a, CancellationToken ct) => Run(session, () => a.UpdateAsync(ct)));
        api.MapGet("/releases/status", (OnlineUpdater u) => Results.Json(u.Status));
        api.MapPost("/releases/check", (OnlineUpdater u, CancellationToken ct) => Run(session, () => u.CheckAsync(ct)));
        api.MapPost("/releases/download", (OnlineUpdater u, CancellationToken ct) => Run(session, () => u.DownloadAsync(ct)));
        api.MapPost("/releases/apply", (OnlineUpdater u, IHostApplicationLifetime lifetime, CancellationToken ct) => Run(session, async () =>
        {
            var result = await u.ApplyAsync(ct);
            if (result.State == "installing")
                _ = Task.Delay(2000).ContinueWith(_ => lifetime.StopApplication(), TaskScheduler.Default);
            return result;
        }));
        api.MapPost("/pair", (PairRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.PairAsync(r, ct)));
        api.MapGet("/dashboard", (ISetupActions a, CancellationToken ct) => Run(session, () => a.GetDashboardAsync(ct), exclusive: false));
        api.MapPost("/push/disable", (ISetupActions a, CancellationToken ct) => Run(session, () => a.DisablePushAsync(ct)));
        api.MapPost("/service/restart", (ISetupActions a, CancellationToken ct) => Run(session, () => a.RestartServiceAsync(ct)));
        api.MapPost("/report", (ISetupActions a, CancellationToken ct) => Run(session, () => a.CreateReportAsync(ct)));
        api.MapPost("/uninstall", (UninstallRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.UninstallAsync(r, ct)));
        // The map has its own lock (LayoutCoordinator), so saving it never blocks the wizard ("busy").
        api.MapGet("/layout", (ISetupActions a, CancellationToken ct) => Run(session, () => a.GetLayoutAsync(ct), exclusive: false));
        api.MapPut("/layout", (LayoutSaveRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.SaveLayoutAsync(r, ct), exclusive: false));
        api.MapPost("/layout/resolve", (LayoutResolveRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.ResolveLayoutAsync(r, ct), exclusive: false));
        // Cashier assistant: price quotes only (nothing is written to GameClass or anywhere else).
        api.MapGet("/pricing", (int pc, ISetupActions a, CancellationToken ct) => Run(session, () => a.GetPricingAsync(pc, ct), exclusive: false));
        api.MapPost("/quote", (Pricing.QuoteRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.QuoteAsync(r, ct), exclusive: false));
        api.MapPost("/autostart", (AutoStartRequest r, ISetupActions a, CancellationToken ct) => Run(session, () => a.SetAutoStartAsync(r, ct)));
        MapAnalytics(api, session);
        api.MapGet("/prefs", (PrefsStore p) => Results.Json(p.Load()));
        api.MapPut("/prefs", (SetupPrefs r, PrefsStore p) => Results.Json(p.Save(r)));
        api.MapPost("/ping", () => Results.NoContent());
        api.MapPost("/close", (IHostApplicationLifetime lifetime) =>
        {
            _ = Task.Delay(300).ContinueWith(_ => lifetime.StopApplication(), TaskScheduler.Default);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// «Hisobot»: computed here from GameClass's history, shown only in this window, sent nowhere. Everything but
    /// the status and the PIN itself needs the PIN token (X-Analytics-Token).
    /// </summary>
    private static void MapAnalytics(RouteGroupBuilder api, SetupSession session)
    {
        static string? Token(HttpContext c) => c.Request.Headers[Analytics.AnalyticsAccess.TokenHeader];

        api.MapGet("/analytics/status", (HttpContext c, Analytics.AnalyticsAccess access) =>
            Run(session, () => Task.FromResult(access.Status(Token(c))), exclusive: false));
        api.MapPost("/analytics/unlock", (Analytics.AnalyticsPinRequest r, Analytics.AnalyticsAccess access) =>
            Run(session, () => Task.FromResult(access.Unlock(r)), exclusive: false));
        api.MapPost("/analytics/pin", (Analytics.AnalyticsPinChangeRequest r, Analytics.AnalyticsAccess access) =>
            Run(session, () => Task.FromResult(access.SetPin(r)), exclusive: false));
        api.MapPut("/analytics/settings", (Analytics.AnalyticsSettingsRequest r, HttpContext c, Analytics.AnalyticsAccess access) =>
            Run(session, () => Task.FromResult(access.SetDayStart(r, Token(c))), exclusive: false));
        // «Bar»: the owner's stock book (PIN). GameClass is only read; its sales are subtracted automatically.
        // No PIN: only names and counts of items running low (no money), for the hall page and the cashier.
        api.MapGet("/bar/alerts", (Analytics.AnalyticsAccess access, Bar.BarService bar, CancellationToken ct) =>
            Run(session, () => bar.AlertsAsync(access.DayStartHour, ct), exclusive: false));
        api.MapGet("/bar", (HttpContext c, Analytics.AnalyticsAccess access, Bar.BarService bar, CancellationToken ct) =>
            Run(session, () =>
            {
                access.Require(Token(c));
                return bar.OverviewAsync(access.DayStartHour, ct);
            }, exclusive: false));
        api.MapPost("/bar/entry", (Bar.BarEntryRequest r, HttpContext c, Analytics.AnalyticsAccess access, Bar.BarService bar,
            CancellationToken ct) => Run(session, () =>
            {
                access.Require(Token(c));
                return bar.AddAsync(r, access.DayStartHour, ct);
            }, exclusive: false));
        api.MapPut("/bar/item", (Bar.BarItemRequest r, HttpContext c, Analytics.AnalyticsAccess access, Bar.BarService bar,
            CancellationToken ct) => Run(session, () =>
            {
                access.Require(Token(c));
                return bar.SetItemAsync(r, access.DayStartHour, ct);
            }, exclusive: false));
        api.MapGet("/bar/history", (int itemId, HttpContext c, Analytics.AnalyticsAccess access, Bar.BarService bar,
            CancellationToken ct) => Run(session, () =>
            {
                access.Require(Token(c));
                return bar.HistoryAsync(itemId, access.DayStartHour, ct);
            }, exclusive: false));
        api.MapPost("/bar/restore", (HttpContext c, Analytics.AnalyticsAccess access, Bar.BarStore store, ISetupActions a,
            CancellationToken ct) => Run(session, async () =>
            {
                access.Require(Token(c));
                var hadLocal = store.Load().Entries.Count > 0;
                var backup = await a.FetchBarBackupAsync(ct) ?? throw new SetupException("bar_restore", "not paired");
                var restored = store.Restore(backup.Entries, backup.Items);
                c.RequestServices.GetRequiredService<Bar.BarService>().Changed();
                return new Bar.BarRestoreResult(restored, hadLocal);
            }));
        api.MapGet("/analytics/push", (HttpContext c, Analytics.AnalyticsAccess access, ISetupActions a, CancellationToken ct) =>
            Run(session, () =>
            {
                access.Require(Token(c));
                return a.GetReportsPushAsync(ct);
            }, exclusive: false));
        api.MapPost("/analytics/push", (ReportsPushRequest r, HttpContext c, Analytics.AnalyticsAccess access, ISetupActions a,
            CancellationToken ct) => Run(session, () =>
            {
                // Only the owner (PIN) decides whether the club's money leaves this PC.
                access.Require(Token(c));
                return a.SetReportsPushAsync(r, ct);
            }));
        api.MapGet("/analytics", (string? from, string? to, string? group, HttpContext c, Analytics.AnalyticsAccess access,
            Analytics.AnalyticsService reports, CancellationToken ct) => Run(session, () =>
            {
                access.Require(Token(c));
                return reports.ReportAsync(from, to, group, access.DayStartHour, ct);
            }, exclusive: false));
        api.MapGet("/analytics/day", (string? date, HttpContext c, Analytics.AnalyticsAccess access,
            Analytics.AnalyticsService reports, CancellationToken ct) => Run(session, () =>
            {
                access.Require(Token(c));
                return reports.DayAsync(date, access.DayStartHour, ct);
            }, exclusive: false));
    }

    /// <summary>Runs one action; long-running changes are serialized (a double click cannot start two installs).</summary>
    private static async Task<IResult> Run<T>(SetupSession session, Func<Task<T>> action, bool exclusive = true)
    {
        if (exclusive && !await session.Gate.WaitAsync(0))
        {
            return Results.Json(new { error = new { code = "busy" } }, statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            return Results.Json(await action());
        }
        catch (SetupException ex)
        {
            return Results.Json(new { error = new { code = ex.Code, detail = ex.Detail } }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new { error = new { code = "unexpected", detail = ex.Message } }, statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            if (exclusive)
            {
                session.Gate.Release();
            }
        }
    }

    /// <summary>
    /// The window pings every 10 s; when it has been closed for a while the process exits. Nothing to do
    /// for the bridge itself: the installed Windows Service keeps running independently.
    /// </summary>
    private static async Task WatchHeartbeatAsync(WebApplication app, SetupSession session, bool headless)
    {
        if (headless)
        {
            return;
        }

        var started = DateTime.UtcNow;
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var quiet = session.LastContact is { } last ? DateTime.UtcNow - last > HeartbeatTimeout : DateTime.UtcNow - started > FirstContactTimeout;
            if (quiet && session.Gate.CurrentCount > 0)
            {
                app.Logger.LogInformation("Setup window closed; exiting");
                app.Lifetime.StopApplication();
                return;
            }
        }
    }
}

internal sealed class SetupSession(string token)
{
    private readonly byte[] _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public int Port { get; set; }

    public DateTime? LastContact { get; private set; }

    public void Touch() => LastContact = DateTime.UtcNow;

    public bool IsValidToken(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && CryptographicOperations.FixedTimeEquals(_tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));

    public bool IsAllowedHost(string? host) =>
        host is not null && (host == $"127.0.0.1:{Port}" || host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase));

    public bool IsAllowedOrigin(string origin) =>
        origin == $"http://127.0.0.1:{Port}" || origin.Equals($"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase);
}
