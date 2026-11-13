using System.Runtime.Versioning;
using Manajoy.GameClass.Bridge.Configuration;
using Microsoft.Win32;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>Names and locations shared by the installer steps (one place, no scattered literals).</summary>
internal sealed class SetupEnvironment(string apiBase)
{
    public const string ServiceName = "ManajoyGameClassBridge";
    public const string ServiceDisplayName = "Manajoy GameClass Bridge (read-only)";
    public const string ServiceDescription = "Reads the GameClass database (read-only) and shows PC status in Manajoy.";
    public const string ExeName = "Manajoy.GameClass.Bridge.exe";
    public const string ReadOnlyLogin = "gcbridge_ro";
    public const string DefaultServer = "(local)";
    public const string DefaultDatabase = "GameClass";
    public const string DefaultApiBase = "https://api.manajoy.uz/api";
    public const string LocalApiBase = "http://127.0.0.1:8787";
    public const string AppSettingsFile = "appsettings.json";
    public const string ClubInfoFile = "manajoy.json";
    public const string PrefsFile = "setup-prefs.json";
    public const string ShortcutName = "Manajoy GameClass.lnk";

    // Service environment variable names (read by the bridge through IConfiguration).
    public const string EnvSqlUser = "Bridge__SqlLogin__UserName";
    public const string EnvPushUrl = "Bridge__Push__Url";
    public const string EnvServer = "Bridge__Server";
    public const string EnvDatabase = "Bridge__Database";
    public const string EnvAnalyticsEnabled = "Bridge__Analytics__Enabled";

    private const string ServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\" + ServiceName;

    /// <summary>Written by the Windows installer (Inno Setup, 32-bit registry view): the folder the owner chose.</summary>
    public const string InstallerKeyPath = @"SOFTWARE\Manajoy\GameClassBridge";

    /// <summary>The uninstaller the Windows installer leaves in the install folder.</summary>
    public const string InstallerUninstallExe = "unins000.exe";

    public string ApiBase { get; } = apiBase.TrimEnd('/');

    public string SqlSecretFile => new SqlLoginOptions().PasswordFile;

    public string PushSecretFile => new PushOptions().KeyFile;

    /// <summary>Setup logs and temporary files: %ProgramData%\Manajoy\GameClassBridge (admin-writable, survives reinstall).</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Manajoy", "GameClassBridge");

    public static string CurrentExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ExeName);

    /// <summary>
    /// Keeps an existing installation where it is (older kits used the 64-bit Program Files); otherwise
    /// "C:\Program Files\Manajoy\GameClassBridge" — also from this 32-bit process, which would get "(x86)".
    /// </summary>
    [SupportedOSPlatform("windows")]
    public string ResolveInstallDir()
    {
        var existing = ServiceExePath();
        if (existing is not null)
        {
            return Path.GetDirectoryName(existing)!;
        }

        if (InstallerDir() is { } chosen)
        {
            return chosen;
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432");
        if (string.IsNullOrEmpty(programFiles))
        {
            programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        return Path.Combine(programFiles, "Manajoy", "GameClassBridge");
    }

    /// <summary>
    /// The folder chosen in the Windows installer, when this copy was installed by it (null for the single
    /// Telegram exe). Then the installer owns the files and the shortcuts; the window only registers the service.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? InstallerDir()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(InstallerKeyPath, writable: false);
            if (key?.GetValue("InstallDir") is string dir && dir.Length > 0 && Directory.Exists(dir))
            {
                return dir.TrimEnd('\\');
            }
        }

        return null;
    }

    /// <summary>This exe was put in place by the Windows installer.</summary>
    [SupportedOSPlatform("windows")]
    public static bool RunningFromInstaller() =>
        InstallerDir() is { } dir
        && string.Equals(Path.GetDirectoryName(CurrentExePath)?.TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    public static string? ServiceExePath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: false);
        if (key?.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        imagePath = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (imagePath.StartsWith('"'))
        {
            var end = imagePath.IndexOf('"', 1);
            return end > 1 ? imagePath[1..end] : null;
        }

        var exeEnd = imagePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? imagePath[..(exeEnd + 4)] : imagePath;
    }

    [SupportedOSPlatform("windows")]
    public static Dictionary<string, string> ReadServiceEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: false);
        if (key?.GetValue("Environment") is string[] lines)
        {
            foreach (var line in lines)
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                {
                    result[line[..separator]] = line[(separator + 1)..];
                }
            }
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    public static void WriteServiceEnvironment(IReadOnlyDictionary<string, string> values)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: true)
            ?? throw new SetupException("service_missing", $"Registry key {ServiceKeyPath} not found");
        var lines = values
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();
        if (lines.Length == 0)
        {
            key.DeleteValue("Environment", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("Environment", lines, RegistryValueKind.MultiString);
        }
    }

    /// <summary>Seller id from a push URL ".../integrations/gameclass/{id}/snapshot".</summary>
    public static int? SellerIdFromPushUrl(string? pushUrl)
    {
        if (string.IsNullOrEmpty(pushUrl))
        {
            return null;
        }

        var parts = pushUrl.TrimEnd('/').Split('/');
        return parts.Length >= 2 && parts[^1] == "snapshot" && int.TryParse(parts[^2], out var id) ? id : null;
    }

    /// <summary>The bridge-key status endpoint that sits next to the push endpoint.</summary>
    public static string? StatusUrlFromPushUrl(string? pushUrl) => SiblingOfPushUrl(pushUrl, "status");

    /// <summary>The bridge-key club map endpoint that sits next to the push endpoint.</summary>
    public static string? LayoutUrlFromPushUrl(string? pushUrl) => SiblingOfPushUrl(pushUrl, "layout");

    private static string? SiblingOfPushUrl(string? pushUrl, string name) =>
        pushUrl is not null && pushUrl.EndsWith("/snapshot", StringComparison.Ordinal)
            ? pushUrl[..^"snapshot".Length] + name
            : null;
}
