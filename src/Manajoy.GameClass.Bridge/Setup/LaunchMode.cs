using System.Globalization;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// One exe, several roles:
///   started by the Service Control Manager → the bridge service;
///   --once                                 → one diagnostic poll printed to the console;
///   --run                                  → the bridge in this console (development);
///   double-click / no arguments            → the setup and dashboard window;
///   --setup-demo                           → the window with fake data (any OS, changes nothing);
///   --setup-headless [--setup-port=N]      → the window's API only, URL printed (remote checks);
///   --uninstall-service                    → stop and remove the Windows Service (the Windows uninstaller calls it);
///   --autostart-on / --autostart-off       → open the panel at sign-in, or not (the installer's checkbox);
///   --autostart                            → how that sign-in task starts the window (same as a double-click).
/// --api-base=URL points pairing at another backend (lab / staging).
/// </summary>
internal sealed record LaunchMode(bool RunBridge, bool Once, SetupLaunchOptions? Setup, string[] BridgeArgs, bool UninstallService = false, bool? SetAutoStart = null)
{
    public const string UninstallServiceSwitch = "--uninstall-service";
    public const string AutoStartOnSwitch = "--autostart-on";
    public const string AutoStartOffSwitch = "--autostart-off";
    public const string OnceSwitch = "--once";
    public const string RunSwitch = "--run";
    public const string DemoSwitch = "--setup-demo";
    public const string HeadlessSwitch = "--setup-headless";
    public const string PortPrefix = "--setup-port=";
    public const string ApiBasePrefix = "--api-base=";

    public static LaunchMode Parse(string[] args, bool isWindowsService)
    {
        bool Has(string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);
        string? Value(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

        var bridgeArgs = args.Where(a => !a.Equals(OnceSwitch, StringComparison.OrdinalIgnoreCase)
                                         && !a.Equals(RunSwitch, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (isWindowsService || Has(RunSwitch))
        {
            return new LaunchMode(true, false, null, bridgeArgs);
        }

        if (Has(UninstallServiceSwitch))
        {
            return new LaunchMode(false, false, null, [], UninstallService: true);
        }

        if (Has(AutoStartOnSwitch) || Has(AutoStartOffSwitch))
        {
            return new LaunchMode(false, false, null, [], SetAutoStart: Has(AutoStartOnSwitch));
        }

        if (Has(OnceSwitch))
        {
            return new LaunchMode(true, true, null, bridgeArgs);
        }

        int? port = int.TryParse(Value(PortPrefix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p is > 0 and < 65536 ? p : null;
        var apiBase = Value(ApiBasePrefix);
        if (apiBase is not null && !Uri.TryCreate(apiBase, UriKind.Absolute, out _))
        {
            apiBase = null;
        }

        return new LaunchMode(false, false, new SetupLaunchOptions(Has(DemoSwitch), Has(HeadlessSwitch), port, apiBase), []);
    }
}
