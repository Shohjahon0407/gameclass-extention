using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Opens the setup UI as an app window (Microsoft Edge --app, no tabs or address bar).
///
/// This process is elevated (installer), but the browser should not be: the window is started through a
/// temporary shortcut handed to explorer.exe, which launches it with the user's normal, non-elevated rights.
/// Fallbacks: Edge directly, then the default browser.
/// </summary>
internal static class BrowserLauncher
{
    private const int PreferredWidth = 1240;
    private const int PreferredHeight = 860;
    private static readonly TimeSpan ShortcutLifetime = TimeSpan.FromSeconds(20);

    public static void Open(string url, ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogInformation("Open this address in a browser: {Url}", url);
            TryStart(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false }, logger);
            return;
        }

        var edge = FindEdge();
        var arguments = $"--app=\"{url}\" {WindowPlacement()} --no-first-run";
        if (edge is not null && TryOpenUnelevated(edge, arguments, logger))
        {
            return;
        }

        if (edge is not null && TryStart(new ProcessStartInfo(edge, arguments) { UseShellExecute = false }, logger))
        {
            return;
        }

        TryStart(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false }, logger);
    }

    private static bool TryOpenUnelevated(string edge, string arguments, ILogger logger)
    {
        try
        {
            // %TEMP% of this user is readable by the non-elevated explorer of the same user (and nobody else).
            var shortcut = Path.Combine(Path.GetTempPath(), $"manajoy-setup-{Guid.NewGuid():N}.lnk");
            ShellShortcut.Create(shortcut, edge, arguments);

            if (!TryStart(new ProcessStartInfo("explorer.exe", $"\"{shortcut}\"") { UseShellExecute = false }, logger))
            {
                File.Delete(shortcut);
                return false;
            }

            // The link holds the session token: remove it once Edge has read it.
            _ = Task.Delay(ShortcutLifetime).ContinueWith(_ => TryDelete(shortcut), TaskScheduler.Default);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not open the window through explorer: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Fits the window into the screen's work area (club PCs are often 1366x768 or 1280x800) and centres it.
    /// This process is not DPI-aware, so the work area comes back in the same 96-DPI units Edge expects.
    /// </summary>
    private static string WindowPlacement()
    {
        var area = new Rect();
        if (!OperatingSystem.IsWindows() || !SystemParametersInfo(SpiGetWorkArea, 0, ref area, 0))
        {
            return $"--window-size={PreferredWidth},{PreferredHeight}";
        }

        var areaWidth = area.Right - area.Left;
        var areaHeight = area.Bottom - area.Top;
        var width = Math.Min(PreferredWidth, areaWidth - 32);
        var height = Math.Min(PreferredHeight, areaHeight - 16);
        var x = area.Left + (areaWidth - width) / 2;
        var y = area.Top + (areaHeight - height) / 2;
        return $"--window-size={width},{height} --window-position={x},{y}";
    }

    private const uint SpiGetWorkArea = 0x0030;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref Rect rect, uint winIni);

    private static string? FindEdge()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", writable: false);
            if (key?.GetValue(null) is string path && File.Exists(path))
            {
                return path;
            }
        }

        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramW6432") })
        {
            var path = root is null ? null : Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (path is not null && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool TryStart(ProcessStartInfo info, ILogger logger)
    {
        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("Could not start {File}: {Message}", info.FileName, ex.Message);
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Still open in explorer: it is in the user's own %TEMP% and harmless.
        }
    }
}

/// <summary>Hides the console window when it was created only for this process (exe double-clicked).</summary>
internal static class NativeConsole
{
    private const int SwHide = 0;

    public static void HideIfOwned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var processes = new uint[2];
            if (GetConsoleProcessList(processes, (uint)processes.Length) == 1)
            {
                ShowWindow(GetConsoleWindow(), SwHide);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not a classic console host; nothing to hide.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
