using System.Text;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Api;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Data;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Hosting;
using Manajoy.GameClass.Bridge.Setup;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

TrySetUtf8Console();

var mode = LaunchMode.Parse(args, WindowsServiceHelpers.IsWindowsService());
if (mode.UninstallService)
{
    return OperatingSystem.IsWindows() ? ServiceManager.RemoveForUninstaller() : 2;
}

if (mode.SetAutoStart is { } autoStart)
{
    return OperatingSystem.IsWindows() ? AutoStart.RunSwitch(autoStart) : 2;
}

if (mode.Setup is { } setup)
{
    if (!OperatingSystem.IsWindows() && !setup.Demo)
    {
        Console.Error.WriteLine($"The setup window manages a Windows Service and only runs on Windows. Use {LaunchMode.DemoSwitch} to preview it.");
        return 2;
    }

    return await SetupHost.RunAsync(setup);
}

var once = mode.Once;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = mode.BridgeArgs,
    // appsettings.json next to the exe, also when started as a Windows Service (cwd = System32).
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(service => service.ServiceName = "ManajoyGameClassBridge");

builder.Services.AddOptions<BridgeOptions>().Bind(builder.Configuration.GetSection(BridgeOptions.SectionName)).ValidateOnStart();
builder.Services.AddSingleton<IPostConfigureOptions<BridgeOptions>, SqlPasswordFile>();
builder.Services.AddSingleton<IValidateOptions<BridgeOptions>, BridgeOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameClassConnectionFactory>();
builder.Services.AddSingleton<ReadOnlyGameClassDb>();
builder.Services.AddSingleton<GameClassReader>();
builder.Services.AddSingleton<SnapshotStore>();
builder.Services.AddSingleton<BridgeClock>();
builder.Services.AddSingleton<PollingWorker>();
builder.Services.AddSingleton<PushStatus>();
builder.Services.AddSingleton<AnalyticsPushStatus>();
builder.Services.AddHttpClient(PushWorker.HttpClientName);
if (!once)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PollingWorker>());
    builder.Services.AddHostedService<PushWorker>();
    builder.Services.AddHostedService(sp =>
    {
        // Its own command timeout: a batch of history takes longer than one poll.
        var bridge = sp.GetRequiredService<IOptions<BridgeOptions>>();
        var db = new ReadOnlyGameClassDb(sp.GetRequiredService<GameClassConnectionFactory>(),
            Options.Create(new BridgeOptions { CommandTimeoutSeconds = bridge.Value.Analytics.CommandTimeoutSeconds }));
        var reader = new Manajoy.GameClass.Bridge.Analytics.AnalyticsReader(db);
        var dayStartFile = OperatingSystem.IsWindows() ? Manajoy.GameClass.Bridge.Analytics.AnalyticsAccess.DefaultPath : null;
        return new AnalyticsPushWorker(() => reader, () => Manajoy.GameClass.Bridge.Analytics.AnalyticsAccess.ReadDayStartHour(dayStartFile),
            sp.GetRequiredService<AnalyticsPushStatus>(), sp.GetRequiredService<IHttpClientFactory>(), bridge,
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<AnalyticsPushWorker>>(),
            barLedger: () => Manajoy.GameClass.Bridge.Bar.BarStore.TryRead(OperatingSystem.IsWindows() ? Manajoy.GameClass.Bridge.Bar.BarStore.DefaultPath : null));
    });
}

// Explicit providers only: the default EventLog provider would write to the Windows event log.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
});
var logDirectory = builder.Configuration[$"{BridgeOptions.SectionName}:{nameof(BridgeOptions.LogDirectory)}"];
logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? new BridgeOptions().LogDirectory : logDirectory;
if (!once)
{
    // --once is a console diagnostic; it must not write into the running service's log file.
    builder.Logging.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(logDirectory));
}

// Bridge:HttpUrls (validated: non-loopback needs an ApiKey) is the only allowed binding source.
if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any() || !string.IsNullOrEmpty(builder.Configuration["urls"]))
{
    Console.Error.WriteLine("Configure listening addresses only through Bridge:HttpUrls (Kestrel:Endpoints / urls are not allowed).");
    return 2;
}

var configuredUrls = builder.Configuration[$"{BridgeOptions.SectionName}:{nameof(BridgeOptions.HttpUrls)}"] ?? new BridgeOptions().HttpUrls;
var urls = BridgeOptionsValidator.SplitUrls(configuredUrls);
builder.WebHost.UseUrls(urls.ToArray());
builder.WebHost.PreferHostingUrls(true);
// Host filtering against DNS rebinding: only the configured host names (plus loopback names) are answered.
builder.Configuration["AllowedHosts"] = BridgeHosts.AllowedHosts(urls);

WebApplication app;
try
{
    app = builder.Build();
    _ = app.Services.GetRequiredService<IOptions<BridgeOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    // A Windows Service has no console, so the reason must also reach the log file.
    using var fileLog = new FileLoggerProvider(logDirectory);
    var log = fileLog.CreateLogger("Bridge");
    Console.Error.WriteLine("Invalid configuration (appsettings.json, section 'Bridge'):");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
        log.LogCritical("Invalid configuration: {Failure}", failure);
    }

    return 2;
}

if (once)
{
    // Diagnostic mode: one poll, print the snapshot, exit. No HTTP listener is started.
    var worker = app.Services.GetRequiredService<PollingWorker>();
    var store = app.Services.GetRequiredService<SnapshotStore>();
    await worker.PollOnceAsync(CancellationToken.None, writeFiles: false);
    if (store.Latest is null)
    {
        var health = store.Health(DateTimeOffset.Now, 5);
        Console.Error.WriteLine($"Poll failed: {health.LastError}");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(store.Latest, BridgeJson.Indented));
    return 0;
}

app.MapBridgeApi();
app.Logger.LogInformation("{Name} {Version} starting (read-only) on {Urls}", BridgeInfo.DisplayName, BridgeInfo.Version, configuredUrls);
await app.RunAsync();
return 0;

static void TrySetUtf8Console()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // No console attached (service or redirected output).
    }
}
