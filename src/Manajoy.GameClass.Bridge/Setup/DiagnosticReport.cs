using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Builds manajoy-bridge-report-&lt;PC&gt;-&lt;time&gt;.zip on the Desktop for support (C# twin of collect-report.ps1).
/// Never includes the *.secret files; any file that still looks like it holds a password is dropped.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DiagnosticReport
{
    private const int LogFilesPerFolder = 3;
    private static readonly string[] LeakMarkers = ["PWD=", "Password=", "X-Bridge-Key:"];

    public static async Task<ReportResult> CreateAsync(
        string installDir, HttpClient http, PreflightReport preflight, SetupState state, CancellationToken cancellationToken)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var work = Directory.CreateTempSubdirectory("manajoy-report-").FullName;
        try
        {
            var json = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(Path.Combine(work, "setup-state.json"), JsonSerializer.Serialize(state, json), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(work, "preflight.json"), JsonSerializer.Serialize(preflight, json), cancellationToken);

            foreach (var (file, path) in new[] { ("health.json", "/health"), ("snapshot.json", "/api/v1/snapshot"), ("events.json", "/api/v1/events?since=0&limit=500") })
            {
                await File.WriteAllTextAsync(Path.Combine(work, file), await FetchAsync(http, path, cancellationToken), cancellationToken);
            }

            await File.WriteAllTextAsync(Path.Combine(work, "system.txt"), SystemText(installDir), cancellationToken);

            CopyIfExists(Path.Combine(installDir, SetupEnvironment.AppSettingsFile), work);
            CopyIfExists(Path.Combine(installDir, SetupEnvironment.ClubInfoFile), work);
            CopyNewest(Path.Combine(installDir, "logs"), Path.Combine(work, "logs"));
            CopyNewest(Path.Combine(SetupEnvironment.DataDirectory, "logs"), Path.Combine(work, "setup-logs"));

            foreach (var file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".secret", StringComparison.OrdinalIgnoreCase)
                    || LeakMarkers.Any(marker => File.ReadAllText(file).Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(file);
                }
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zip = Path.Combine(desktop, $"manajoy-bridge-report-{Environment.MachineName}-{stamp}.zip");
            ZipFile.CreateFromDirectory(work, zip);
            return new ReportResult(zip, new FileInfo(zip).Length);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static async Task<string> FetchAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetStringAsync(SetupEnvironment.LocalApiBase + path, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string SystemText(string installDir)
    {
        var text = new StringBuilder()
            .AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine($"Machine:   {Environment.MachineName}")
            .AppendLine($"OS:        {Preflight.OsName()} {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)")
            .AppendLine($"Culture:   {System.Globalization.CultureInfo.CurrentCulture.Name}")
            .AppendLine();

        text.AppendLine("Services:");
        var services = ServiceController.GetServices();
        foreach (var service in services.Where(s => s.ServiceName is SetupEnvironment.ServiceName or "MSSQLSERVER" or "srvGCCL"
                                                     || s.ServiceName.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)))
        {
            text.AppendLine($"  {service.ServiceName}: {service.Status}");
        }

        foreach (var service in services)
        {
            service.Dispose();
        }

        text.AppendLine().AppendLine("Processes:");
        foreach (var name in new[] { Path.GetFileNameWithoutExtension(SetupEnvironment.ExeName), "GCServer" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    text.AppendLine($"  {name} PID {process.Id}, {process.WorkingSet64 / (1024 * 1024)} MB");
                }
            }
        }

        text.AppendLine().AppendLine($"Install folder: {installDir}");
        if (Directory.Exists(installDir))
        {
            foreach (var file in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
                         .Where(f => !f.EndsWith(".secret", StringComparison.OrdinalIgnoreCase)))
            {
                var info = new FileInfo(file);
                text.AppendLine($"  {Path.GetRelativePath(installDir, file)}  {info.Length:N0} B  {info.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
        }

        return text.ToString();
    }

    private static void CopyIfExists(string file, string targetDir)
    {
        if (File.Exists(file))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
        }
    }

    private static void CopyNewest(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);
        foreach (var file in new DirectoryInfo(sourceDir).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc).Take(LogFilesPerFolder))
        {
            // Copy with sharing: the service keeps today's log open.
            using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = File.Create(Path.Combine(targetDir, file.Name));
            source.CopyTo(target);
        }
    }
}
