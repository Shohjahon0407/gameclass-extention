using System.Text;
using Manajoy.GameClass.Inspector;
using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string NoPauseSwitch = "--no-pause";

TrySetUtf8Console();

var noPause = args.Contains(NoPauseSwitch, StringComparer.OrdinalIgnoreCase);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(arg => !arg.Equals(NoPauseSwitch, StringComparison.OrdinalIgnoreCase)).ToArray(),
    // appsettings.json is read next to the exe, whatever the current directory (also true for a future Windows Service).
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.AddInspectorLogging(builder.Configuration);
builder.Services.AddInspector(builder.Configuration);

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var exitCode = await RunAsync(host.Services, cancellation.Token);

if (!noPause && ConsoleWindow.IsOwnedByThisProcess())
{
    Console.WriteLine("Press any key to close this window...");
    Console.ReadKey(intercept: true);
}

return exitCode;

static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
{
    try
    {
        _ = services.GetRequiredService<IOptions<InspectorOptions>>().Value;
    }
    catch (OptionsValidationException ex)
    {
        Console.Error.WriteLine("Invalid configuration (appsettings.json, section 'Inspection'):");
        foreach (var failure in ex.Failures)
        {
            Console.Error.WriteLine($"  - {failure}");
        }

        return ExitCodes.InvalidConfiguration;
    }

    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Manajoy.GameClass.Inspector");
    try
    {
        await services.GetRequiredService<InspectionRunner>().RunAsync(cancellationToken);
        return ExitCodes.Success;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning("Inspection cancelled by user");
        return ExitCodes.Cancelled;
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Inspection aborted by an unexpected error");
        return ExitCodes.Failure;
    }
}

static void TrySetUtf8Console()
{
    try
    {
        // Needed for ✓/✗ and Cyrillic table names in the classic Windows console.
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // No console attached (e.g. redirected or running as a service); nothing to fix.
    }
}
