using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Read-only look at the system disk, for the "the exe opened once, then 0xc000003e" kind of failure:
///   - the System event log of the last 7 days: bad blocks, controller and paging errors (disk 7/11/51/153),
///     NTFS corruption (Ntfs 55/98/137/140), warnings and errors only;
///   - the volume "dirty" bit (Windows found file-system damage and wants chkdsk).
/// No other programs are started (an installer that spawns PowerShell looks suspicious to antivirus heuristics).
/// Nothing is repaired or changed; each source that cannot be read is skipped and named in <see cref="DiskHealthInfo.Errors"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DiskHealth
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private const string EventQuery =
        "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {0}] and ("
        + "(Provider[@Name='disk'] and (EventID=7 or EventID=11 or EventID=51 or EventID=153))"
        + " or (Provider[@Name='Ntfs' or @Name='Microsoft-Windows-Ntfs'] and (EventID=55 or EventID=98 or EventID=137 or EventID=140))"
        + ")]]";

    private static readonly object Gate = new();
    private static (DateTime At, DiskHealthInfo Info)? _latest;
    private static Task? _refresh;

    /// <summary>The Windows drive (where the setup exe, GameClass and its database usually live).</summary>
    public static string SystemRoot => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>Reads now (preflight, "check again") and remembers the result for the dashboard.</summary>
    public static DiskHealthInfo ReadNow()
    {
        var info = Read(SystemRoot);
        lock (Gate)
        {
            _latest = (DateTime.UtcNow, info);
        }

        return info;
    }

    /// <summary>
    /// For the 4-second dashboard poll: returns the last result at once and, when it is older than
    /// <paramref name="maxAge"/>, refreshes it in the background (a read takes a few seconds).
    /// </summary>
    public static DiskHealthInfo? Latest(TimeSpan maxAge)
    {
        lock (Gate)
        {
            var stale = _latest is not { } latest || DateTime.UtcNow - latest.At > maxAge;
            if (stale && (_refresh is null || _refresh.IsCompleted))
            {
                _refresh = Task.Run(() =>
                {
                    try
                    {
                        ReadNow();
                    }
                    catch (Exception)
                    {
                        // A failed refresh keeps the previous result; the next poll tries again after maxAge.
                        lock (Gate)
                        {
                            if (_latest is { } previous)
                            {
                                _latest = (DateTime.UtcNow, previous.Info);
                            }
                        }
                    }
                });
            }

            return _latest?.Info;
        }
    }

    public static DiskHealthInfo Read(string volumeRoot)
    {
        var findings = new List<string>();
        var errors = new List<string>();
        var events = 0;
        DateTimeOffset? lastEvent = null;

        try
        {
            var counts = new Dictionary<string, int>();
            var query = new EventLogQuery("System", PathType.LogName, string.Format(System.Globalization.CultureInfo.InvariantCulture, EventQuery, (long)Window.TotalMilliseconds));
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    events++;
                    var key = $"{record.ProviderName} {record.Id}";  // e.g. "disk 7"
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    if (record.TimeCreated is { } at && (lastEvent is null || at > lastEvent))
                    {
                        lastEvent = at;
                    }
                }
            }

            findings.AddRange(counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} x{c.Value}"));
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            errors.Add($"eventlog: {ex.Message}");
        }

        var dirty = false;
        try
        {
            dirty = IsVolumeDirty(volumeRoot);
            if (dirty)
            {
                findings.Add($"{volumeRoot.TrimEnd('\\')} dirty (chkdsk)");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            errors.Add($"dirty bit: {ex.Message}");
        }

        // "Retried" (disk 153) and NTFS flush/rollback warnings also show up on healthy but busy disks (and in VMs):
        // listed, but only real read/write failures, the dirty bit or the drive's own verdict make it a problem.
        var serious = findings.Any(f => SeriousEvents.Any(e => f.StartsWith(e + " ", StringComparison.Ordinal)));
        return new DiskHealthInfo(serious || dirty, events, dirty, findings, lastEvent, errors);
    }

    /// <summary>Bad blocks, controller errors, paging read failures, NTFS corruption / "run chkdsk".</summary>
    private static readonly string[] SeriousEvents = ["disk 7", "disk 11", "disk 51", "Ntfs 55", "Ntfs 98", "Microsoft-Windows-Ntfs 55", "Microsoft-Windows-Ntfs 98"];

    public static CheckResult ToCheck(DiskHealthInfo info)
    {
        var last = info.LastEventAt is { } at ? $" (last {at:yyyy-MM-dd HH:mm})" : "";
        if (info.Problem)
        {
            return new CheckResult("disk_health", CheckLevel.Warn, string.Join("; ", info.Findings) + last);
        }

        var note = info.Errors.Count > 0 ? " | partly checked: " + string.Join("; ", info.Errors) : "";
        return info.Findings.Count > 0
            ? new CheckResult("disk_health", CheckLevel.Ok, "minor: " + string.Join("; ", info.Findings) + last + note)
            : new CheckResult("disk_health", CheckLevel.Ok, "7 days, no disk errors" + note);
    }

    /// <summary>FSCTL_IS_VOLUME_DIRTY on \\.\C: (needs administrator rights, which the setup window has).</summary>
    private static bool IsVolumeDirty(string volumeRoot)
    {
        var device = @"\\.\" + volumeRoot.TrimEnd('\\');
        using var handle = CreateFile(device, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException($"open {device}: error {Marshal.GetLastWin32Error()}");
        }

        if (!DeviceIoControl(handle, FsctlIsVolumeDirty, IntPtr.Zero, 0, out var flags, sizeof(uint), out _, IntPtr.Zero))
        {
            throw new IOException($"FSCTL_IS_VOLUME_DIRTY: error {Marshal.GetLastWin32Error()}");
        }

        return (flags & VolumeIsDirty) != 0;
    }

    private const uint FsctlIsVolumeDirty = 0x00090078;
    private const uint VolumeIsDirty = 0x1;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint GenericRead = 0x80000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
        out uint outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
