using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Read-only readiness checks (the C# twin of field-kit/scripts/preflight.ps1). Nothing is changed.
/// Check ids are stable; the UI turns them into uz/ru sentences.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Preflight
{
    private const int MinimumWindowsBuild = 14393;
    private const long MinimumFreeDiskBytes = 500L * 1024 * 1024;
    private const int LocalApiPort = 8787;

    public static async Task<PreflightReport> RunAsync(SetupEnvironment environment, ManajoyApiClient manajoy, CancellationToken cancellationToken)
    {
        var checks = new List<CheckResult>();
        Add(checks, CheckWindows);
        Add(checks, CheckAdministrator);
        AddMany(checks, CheckSqlServices);
        Add(checks, CheckSqlVersion);
        Add(checks, CheckOdbcDriver);
        Add(checks, CheckGameClassServer);
        Add(checks, CheckGcServerProcess);
        Add(checks, CheckLocale);
        Add(checks, CheckPort);
        Add(checks, () => CheckDisk(environment));
        Add(checks, () => DiskHealth.ToCheck(DiskHealth.ReadNow()));
        checks.Add(await CheckInternetAsync(environment, manajoy, cancellationToken));

        return new PreflightReport(checks, Verdict(checks));
    }

    public static CheckLevel Verdict(IEnumerable<CheckResult> checks)
    {
        var levels = checks.Select(check => check.Level).ToList();
        return levels.Contains(CheckLevel.Fail) ? CheckLevel.Fail
            : levels.Contains(CheckLevel.Warn) ? CheckLevel.Warn
            : CheckLevel.Ok;
    }

    private static void Add(List<CheckResult> checks, Func<CheckResult> check) => AddMany(checks, () => [check()]);

    private static void AddMany(List<CheckResult> checks, Func<IEnumerable<CheckResult>> check)
    {
        try
        {
            checks.AddRange(check());
        }
        catch (Exception ex)
        {
            // One unreadable source (WMI, registry ACL) must not hide the other checks.
            checks.Add(new CheckResult("check_error", CheckLevel.Warn, ex.Message));
        }
    }

    private static CheckResult CheckWindows()
    {
        var build = Environment.OSVersion.Version.Build;
        var name = $"{OsName()} (build {build}, {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)";
        return new CheckResult("windows", build >= MinimumWindowsBuild ? CheckLevel.Ok : CheckLevel.Fail, name);
    }

    private static CheckResult CheckAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        return new CheckResult("administrator", elevated ? CheckLevel.Ok : CheckLevel.Fail, identity.Name);
    }

    private static IEnumerable<CheckResult> CheckSqlServices()
    {
        var services = ServiceController.GetServices();
        try
        {
            var sql = services
                .Where(s => s.ServiceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                            || s.ServiceName.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sql.Count == 0)
            {
                return [new CheckResult("sql_service", CheckLevel.Fail, "MSSQLSERVER / MSSQL$*")];
            }

            return sql.Select(s => new CheckResult(
                    "sql_service",
                    s.Status == ServiceControllerStatus.Running ? CheckLevel.Ok : CheckLevel.Fail,
                    $"{s.ServiceName}: {s.Status}"))
                .ToList();
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    private static CheckResult CheckSqlVersion()
    {
        var version = ReadRegistry(@"SOFTWARE\Microsoft\MSSQLServer\MSSQLServer\CurrentVersion", "CurrentVersion");
        if (version is null)
        {
            return new CheckResult("sql_version", CheckLevel.Warn, "not in registry");
        }

        var major = int.TryParse(version.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 0;
        return new CheckResult("sql_version", CheckLevel.Ok, major is > 0 and <= 8 ? $"{version} (SQL Server 2000 / MSDE)" : version);
    }

    /// <summary>The driver of this process's bitness is the one the bridge loads.</summary>
    private static CheckResult CheckOdbcDriver()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI\SQL Server", writable: false);
        var driver = key?.GetValue("Driver") as string;
        return driver is null
            ? new CheckResult("odbc_driver", CheckLevel.Fail, "\"SQL Server\" ODBC driver")
            : new CheckResult("odbc_driver", CheckLevel.Ok, driver);
    }

    private static CheckResult CheckGameClassServer()
    {
        var version = ReadRegistry(@"SOFTWARE\GameClass\Server", "CurrentVersion");
        var directory = ReadRegistry(@"SOFTWARE\GameClass\Server", "InstallDirectory");
        return version is null && directory is null
            ? new CheckResult("gameclass_server", CheckLevel.Warn, "not installed on this PC")
            : new CheckResult("gameclass_server", CheckLevel.Ok, $"{version} {directory}".Trim());
    }

    private static CheckResult CheckGcServerProcess()
    {
        var processes = Process.GetProcessesByName("GCServer");
        try
        {
            return processes.Length > 0
                ? new CheckResult("gcserver_running", CheckLevel.Ok, $"PID {string.Join(", ", processes.Select(p => p.Id))}")
                : new CheckResult("gcserver_running", CheckLevel.Warn, "GCServer.exe");
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static CheckResult CheckLocale()
    {
        var system = SystemLocaleName();
        var user = CultureInfo.CurrentCulture.Name;
        var mismatch = system.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                       && !user.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        return new CheckResult("locale", mismatch ? CheckLevel.Warn : CheckLevel.Ok, $"system {system}, user {user}");
    }

    private static CheckResult CheckPort()
    {
        var busy = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == LocalApiPort);
        var bridgeInstalled = SetupEnvironment.ServiceExePath() is not null;
        if (!busy)
        {
            return new CheckResult("port", CheckLevel.Ok, $"{LocalApiPort}");
        }

        return bridgeInstalled
            ? new CheckResult("port", CheckLevel.Ok, $"{LocalApiPort} (Manajoy bridge)")
            : new CheckResult("port", CheckLevel.Fail, $"{LocalApiPort}");
    }

    private static CheckResult CheckDisk(SetupEnvironment environment)
    {
        var root = Path.GetPathRoot(environment.ResolveInstallDir()) ?? "C:\\";
        var drive = new DriveInfo(root);
        var freeMb = drive.AvailableFreeSpace / (1024 * 1024);
        return new CheckResult("disk", drive.AvailableFreeSpace >= MinimumFreeDiskBytes ? CheckLevel.Ok : CheckLevel.Fail, $"{freeMb:N0} MB {root}");
    }

    private static async Task<CheckResult> CheckInternetAsync(SetupEnvironment environment, ManajoyApiClient manajoy, CancellationToken cancellationToken)
    {
        var reachable = await manajoy.IsReachableAsync(environment.ApiBase, cancellationToken);
        return new CheckResult("internet", reachable ? CheckLevel.Ok : CheckLevel.Warn, environment.ApiBase);
    }

    public static string OsName()
    {
        var product = ReadRegistry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? RuntimeInformation.OSDescription;
        // Windows 11 still reports "Windows 10" in ProductName; the build number tells them apart.
        return Environment.OSVersion.Version.Build >= 22000 ? product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : product;
    }

    /// <summary>Reads HKLM in the 32-bit view first (MSDE and GameClass are 32-bit), then the 64-bit view.</summary>
    private static string? ReadRegistry(string path, string name)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(path, writable: false);
            if (key?.GetValue(name) is { } value && value.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string SystemLocaleName()
    {
        var buffer = new char[85];
        var length = GetSystemDefaultLocaleName(buffer, buffer.Length);
        return length > 1 ? new string(buffer, 0, length - 1) : CultureInfo.InstalledUICulture.Name;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetSystemDefaultLocaleName([Out] char[] localeName, int localeNameLength);
}
