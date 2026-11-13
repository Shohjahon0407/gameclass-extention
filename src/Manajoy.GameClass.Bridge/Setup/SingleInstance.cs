using System.Runtime.Versioning;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// One setup window per Windows session. A second double-click (desktop shortcut or the downloaded exe)
/// does not start another installer next to the first one; it re-opens the window of the running one.
/// The running instance's URL (with its token) is kept in a file only Administrators and SYSTEM can read.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SingleInstance : IDisposable
{
    // A named event, not a Mutex: Mutex ownership is tied to a thread and async code continues on others.
    private const string HandleName = @"Local\ManajoyGameClassSetup";

    private readonly EventWaitHandle _handle;

    private SingleInstance(EventWaitHandle handle) => _handle = handle;

    private static string WindowFile => Path.Combine(SetupEnvironment.DataDirectory, "setup-window.url");

    /// <summary>Null when another setup window is already running (its URL is returned in <paramref name="existingUrl"/>).</summary>
    public static SingleInstance? TryAcquire(out string? existingUrl)
    {
        var handle = new EventWaitHandle(false, EventResetMode.ManualReset, HandleName, out var createdNew);
        if (createdNew)
        {
            existingUrl = null;
            return new SingleInstance(handle);
        }

        handle.Dispose();
        try
        {
            existingUrl = File.Exists(WindowFile) ? File.ReadAllText(WindowFile).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            existingUrl = null;
        }

        return null;
    }

    /// <summary>
    /// Ends setup windows of an OLDER version in this Windows session (the service runs in session 0 and is
    /// never touched). Returns how many were closed; a same or newer window is left alone and re-opened instead.
    /// </summary>
    public static int CloseOlderWindows()
    {
        if (!Version.TryParse(Hosting.BridgeInfo.Version.Split('+')[0], out var mine))
        {
            return 0;
        }

        var self = Environment.ProcessId;
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var closed = 0;
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.SessionId != session)
                    {
                        continue;
                    }

                    var info = process.MainModule?.FileVersionInfo;
                    if (info?.ProductName != Hosting.BridgeInfo.DisplayName
                        || !Version.TryParse((info.ProductVersion ?? "").Split('+')[0], out var theirs)
                        || theirs >= mine)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                    closed++;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Other users' and protected processes cannot be inspected: they are not ours anyway.
                }
            }
        }

        if (closed > 0)
        {
            // The killed window could not remove its handle file; the named event went away with its process.
            TryDelete();
        }

        return closed;
    }

    public void Publish(string windowUrl)
    {
        try
        {
            Directory.CreateDirectory(SetupEnvironment.DataDirectory);
            File.WriteAllText(WindowFile, windowUrl);
            ServiceManager.RunTool("icacls.exe", WindowFile, "/inheritance:r", "/grant:r", "*S-1-5-32-544:F", "*S-1-5-18:F", "/C", "/Q");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SetupException)
        {
            // Only a convenience for a second double-click; the window itself works without it.
            TryDelete();
        }
    }

    public void Dispose()
    {
        TryDelete();
        _handle.Dispose();
    }

    private static void TryDelete()
    {
        try
        {
            File.Delete(WindowFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind only until the next setup window overwrites it.
        }
    }
}
