using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Hosting;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Keeps a verified copy of the setup exe in %ProgramData%\Manajoy\GameClassBridge\setup and points the
/// desktop / Start menu shortcuts at it (until the service is installed; then they point at Program Files).
///
/// Why: on one club PC the downloaded exe opened once and then failed with 0xc000003e (Windows could not read
/// the file). A freshly downloaded file is still in the file cache, so the first launch can read it even when
/// the disk spot under it is bad, or before an antivirus touches it. This copy is taken at that moment and
/// verified by SHA-256, so later launches from the shortcut do not depend on the downloaded file.
///
/// It also remembers where the download was (origin.json) and re-reads it on later launches: "unreadable"
/// points at the disk, "changed" or "missing" at an antivirus. The result is shown in the preflight checks
/// and lands in the support report.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SetupCopy
{
    public const string FileName = "ManajoyGameClass-Setup.exe";

    public static string Folder => Path.Combine(SetupEnvironment.DataDirectory, "setup");

    public static string ExePath => Path.Combine(Folder, FileName);

    private static string OriginPath => Path.Combine(Folder, "origin.json");

    private sealed record Origin(string Path, string Sha256, long Size, string Version, DateTimeOffset SeenAt);

    /// <summary>Runs once per window start; never throws (a failure is reported, not fatal).</summary>
    public static SelfCheckInfo Run(string installDir, bool installed, ILogger logger)
    {
        var current = SetupEnvironment.CurrentExePath;
        // The previous download first: taking the copy below records this launch as the new origin.
        var (downloadStatus, downloadPath, downloadDetail) = CheckDownload(current, logger);
        var (copyStatus, copyDetail, sha) = EnsureCopy(current, installDir, installed, logger);
        var info = new SelfCheckInfo(copyStatus, ExePath, copyDetail, downloadStatus, downloadPath, downloadDetail, current, sha);
        logger.LogInformation("Setup self-check: copy {CopyStatus} ({CopyDetail}); download {DownloadStatus} ({DownloadDetail}); exe {Exe} sha256 {Sha}",
            copyStatus, copyDetail, downloadStatus, downloadDetail, current, sha);
        return info;
    }

    /// <summary>Preflight rows: the copy (always) and the downloaded file (only when it says something).</summary>
    public static IEnumerable<CheckResult> Checks(SelfCheckInfo info)
    {
        yield return info.CopyStatus switch
        {
            "created" or "updated" or "in_place" or "running_copy" => new CheckResult("setup_copy", CheckLevel.Ok, info.CopyPath ?? ""),
            "installed" => new CheckResult("setup_copy", CheckLevel.Info, info.CopyDetail ?? ""),
            _ => new CheckResult("setup_copy", CheckLevel.Warn, info.CopyDetail ?? ""),
        };

        var where = info.DownloadPath ?? "";
        switch (info.DownloadStatus)
        {
            case "unreadable":
                yield return new CheckResult("download_unreadable", CheckLevel.Warn, $"{where}: {info.DownloadDetail}");
                break;
            case "changed":
                yield return new CheckResult("download_changed", CheckLevel.Warn, $"{where}: {info.DownloadDetail}");
                break;
            case "missing":
                yield return new CheckResult("download_missing", CheckLevel.Info, where);
                break;
            case "ok":
                yield return new CheckResult("download_file", CheckLevel.Ok, where);
                break;
        }
    }

    private static (string Status, string Detail, string? Sha) EnsureCopy(string current, string installDir, bool installed, ILogger logger)
    {
        string? sha = null;
        try
        {
            if (SamePath(current, ExePath))
            {
                return ("running_copy", ExePath, HashFile(current, out _));
            }

            if (SamePath(Path.GetDirectoryName(current), installDir) || SetupEnvironment.RunningFromInstaller())
            {
                return ("installed", installDir, HashFile(current, out _));
            }

            // Read the running exe now, while it can still be read (see the class comment).
            byte[] bytes;
            try
            {
                bytes = ReadAll(current);
            }
            catch (IOException ex)
            {
                return ("source_unreadable", Describe(ex), null);
            }

            sha = Convert.ToHexString(SHA256.HashData(bytes));
            Directory.CreateDirectory(Folder);
            ServiceManager.RestrictFolder(Folder);
            SaveOrigin(new Origin(current, sha, bytes.LongLength, BridgeInfo.Version, DateTimeOffset.Now));

            var existed = File.Exists(ExePath);
            if (existed && HashFile(ExePath, out _) == sha)
            {
                EnsureShortcuts(installed, logger);
                return ("in_place", ExePath, sha);
            }

            // Write next to the target, read it back from disk, compare, then swap: a bad copy never replaces a good one.
            var staging = ExePath + ".new";
            using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            var written = HashFile(staging, out _);
            if (written != sha)
            {
                File.Delete(staging);
                return ("verify_failed", $"wrote {written}, expected {sha}", sha);
            }

            File.Move(staging, ExePath, overwrite: true);
            ServiceManager.AllowUsersToStart(ExePath);
            EnsureShortcuts(installed, logger);
            return (existed ? "updated" : "created", ExePath, sha);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Setup copy failed: {Message}", ex.Message);
            return ("failed", Describe(ex), sha);
        }
    }

    /// <summary>Before installation the shortcuts open this copy; afterwards install points them at Program Files.</summary>
    private static void EnsureShortcuts(bool installed, ILogger logger)
    {
        if (installed)
        {
            return;
        }

        try
        {
            ServiceManager.CreateShortcuts(ExePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Shortcut to the setup copy not created: {Message}", ex.Message);
        }
    }

    /// <summary>Re-reads the file the window was first started from (when this launch is not that file).</summary>
    private static (string Status, string? Path, string? Detail) CheckDownload(string current, ILogger logger)
    {
        Origin? origin;
        try
        {
            origin = File.Exists(OriginPath) ? JsonSerializer.Deserialize<Origin>(File.ReadAllText(OriginPath)) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("origin.json unreadable: {Message}", ex.Message);
            return ("none", null, null);
        }

        if (origin is null || SamePath(origin.Path, current))
        {
            return ("none", origin?.Path, null);
        }

        if (!File.Exists(origin.Path))
        {
            return ("missing", origin.Path, null);
        }

        try
        {
            var sha = HashFile(origin.Path, out var size);
            return sha == origin.Sha256
                ? ("ok", origin.Path, null)
                : ("changed", origin.Path, $"size {origin.Size} → {size}, sha256 {origin.Sha256[..12]}… → {sha[..12]}…");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("unreadable", origin.Path, Describe(ex));
        }
    }

    private static void SaveOrigin(Origin origin) =>
        File.WriteAllText(OriginPath, JsonSerializer.Serialize(origin, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Removed with the bridge (the folder stays when this very copy is running; it is harmless).</summary>
    public static string Remove()
    {
        if (!Directory.Exists(Folder))
        {
            return Folder;
        }

        if (SamePath(SetupEnvironment.CurrentExePath, ExePath))
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & rmdir /s /q \"{Folder}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            return $"{Folder} (after closing)";
        }

        Directory.Delete(Folder, recursive: true);
        return Folder;
    }

    private static byte[] ReadAll(string path)
    {
        // The running exe is open for execution; reading it needs full sharing.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream((int)Math.Min(stream.Length, int.MaxValue));
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    internal static string HashFile(string path, out long size)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        size = stream.Length;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>The Win32 code tells disk errors (0x80070017 CRC, 0x8007045D I/O device) from locks and access denials.</summary>
    private static string Describe(Exception ex) => $"0x{ex.HResult:X8} {ex.Message}";

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
