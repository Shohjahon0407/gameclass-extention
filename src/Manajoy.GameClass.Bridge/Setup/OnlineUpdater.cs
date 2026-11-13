using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Hosting;

namespace Manajoy.GameClass.Bridge.Setup;

internal sealed record ReleaseManifest(string Version, string File, long Size, string Sha256);
internal sealed record ReleaseStatus(string State, string CurrentVersion, string? LatestVersion = null,
    bool Available = false, long Downloaded = 0, long Size = 0, string? Error = null, string? LastResult = null);

/// <summary>Only signed releases from our HTTPS feed can become executable updates.</summary>
internal sealed class OnlineUpdater(HttpClient client, bool demo)
{
    internal static readonly Uri Feed = new("https://www.manajoy.uz/gameclass/latest.json");
    internal const long MaxInstallerSize = 200_000_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReleaseStatus _status = new("idle", BridgeInfo.Version);
    private ReleaseManifest? _release;
    private string? _installer;
    internal static string UpdateDirectory => Path.Combine(SetupEnvironment.DataDirectory, "updates");

    public ReleaseStatus Status => _status with { LastResult = ReadLastResult() };

    private string? ReadLastResult()
    {
        if (demo || !OperatingSystem.IsWindows()) return null;
        try
        {
            var path = Path.Combine(UpdateDirectory, "last-result.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16_384) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("state").GetString();
        }
        catch { return null; }
    }

    public async Task<ReleaseStatus> CheckAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new SetupException("busy");
        try
        {
            if (_status.State is "ready" or "installing") return Status;
            _status = new("checking", BridgeInfo.Version);
            if (demo)
            {
                await Task.Delay(350, ct);
                _status = new("available", BridgeInfo.Version, "99.0.0", true, Size: 32_000_000);
                return Status;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var checkToken = timeout.Token;
            // Disable redirects on this HttpClient: trust stays on the pinned release origin.
            var bytes = await ReadBoundedAsync(Feed, 65_536, checkToken);
            var signature = await ReadBoundedAsync(new Uri(Feed + ".sig"), 1024, checkToken);
            using var rsa = RSA.Create();
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("release-public.pem")!;
            using var reader = new StreamReader(resource);
            rsa.ImportFromPem(await reader.ReadToEndAsync(ct));
            _release = VerifyManifest(bytes, signature, rsa, Feed);
            var available = Version.Parse(_release.Version) > Version.Parse(BridgeInfo.Version);
            _status = new(available ? "available" : "current", BridgeInfo.Version, _release.Version, available, Size: _release.Size);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { _status = new("idle", BridgeInfo.Version); throw; }
        catch (Exception e) { _release = null; _status = new("failed", BridgeInfo.Version, Error: ErrorCode(e)); }
        finally { _gate.Release(); }
        return Status;
    }

    internal static ReleaseManifest VerifyManifest(byte[] bytes, byte[] signature, RSA key, Uri feed)
    {
        if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new SetupException("online_invalid_release");
        var release = JsonSerializer.Deserialize<ReleaseManifest>(bytes, Json) ?? throw new SetupException("online_invalid_release");
        ValidateManifest(release, feed);
        return release;
    }

    internal static Uri ValidateManifest(ReleaseManifest r, Uri feed)
    {
        if (!Version.TryParse(r.Version, out _) || r.Size is <= 0 or > MaxInstallerSize ||
            r.Sha256 is null || r.Sha256.Length != 64 || !r.Sha256.All(Uri.IsHexDigit) ||
            r.File != $"download/ManajoyGameClass-Setup-{r.Version}.exe")
            throw new SetupException("online_invalid_release");
        var uri = new Uri(feed, r.File);
        if (uri.Scheme != "https" || uri.Authority != feed.Authority || !string.IsNullOrEmpty(uri.Query))
            throw new SetupException("online_invalid_release");
        return uri;
    }

    private async Task<byte[]> ReadBoundedAsync(Uri uri, int limit, CancellationToken ct)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new SetupException("online_invalid_release");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > limit) throw new SetupException("online_invalid_release");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    public async Task<ReleaseStatus> DownloadAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new SetupException("busy");
        string? staging = null;
        try
        {
            if (_status.State == "ready") return Status;
            if (!_status.Available || _status.State == "installing") throw new SetupException("online_check_first");
            if (demo)
            {
                for (var n = 0; n <= 4; n++) { _status = _status with { State = "downloading", Downloaded = _status.Size * n / 4 }; await Task.Delay(150, ct); }
                _status = _status with { State = "ready" }; return Status;
            }
            RequireInstalled();
            var release = _release ?? throw new SetupException("online_check_first");
            var uri = ValidateManifest(release, Feed);
            staging = Path.Combine(UpdateDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            ServiceManager.RestrictFolder(UpdateDirectory);
            _status = _status with { State = "downloading", Downloaded = 0, Error = null };
            var part = Path.Combine(staging, "installer.part");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new SetupException("online_invalid_release");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new SetupException("online_invalid_release");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    _status = _status with { Downloaded = total };
                }
            }
            await VerifyFileAsync(part, release, ct);
            _installer = Path.Combine(staging, "installer.exe");
            File.Move(part, _installer);
            _status = _status with { State = "ready" };
        }
        catch (Exception e)
        {
            if (staging is not null) { try { Directory.Delete(staging, true); } catch { /* retried on next launch */ } }
            _status = _status with { State = "failed", Error = ErrorCode(e) };
        }
        finally { _gate.Release(); }
        return Status;
    }

    internal static async Task VerifyFileAsync(string path, ReleaseManifest release, CancellationToken ct)
    {
        if (new FileInfo(path).Length != release.Size) throw new SetupException("online_invalid_release");
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, ct);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(release.Sha256)))
            throw new SetupException("online_invalid_release");
    }

    public async Task<ReleaseStatus> ApplyAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new SetupException("busy");
        try
        {
            if (_status.State != "ready") throw new SetupException("online_check_first");
            if (demo) { _status = _status with { State = "current", Available = false }; return Status; }
            RequireInstalled();
            var release = _release ?? throw new SetupException("online_check_first");
            await VerifyFileAsync(_installer!, release, ct);
            var staging = Path.GetDirectoryName(_installer!)!;
            var script = Path.Combine(staging, "update.ps1");
            await using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("update-runner.ps1")!)
            await using (var output = File.Create(script)) { await resource.CopyToAsync(output, ct); }
            var plan = Path.Combine(staging, "plan.json");
            var installDir = SetupEnvironment.InstallerDir()!;
            var healthUrl = ResolveHealthUrl(installDir);
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new
            {
                installer = _installer, installDir, parentId = Environment.ProcessId, expectedVersion = release.Version,
                sha256 = release.Sha256, size = release.Size, healthUrl,
                resultPath = Path.Combine(UpdateDirectory, "last-result.json"),
            }, Json), new UTF8Encoding(false), ct);
            var command = $"& '{script.Replace("'", "''")}' -PlanPath '{plan.Replace("'", "''")}'";
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = staging };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new SetupException("online_install_failed");
            var ready = Path.Combine(staging, "runner.ready");
            for (var attempt = 0; attempt < 50 && !File.Exists(ready); attempt++)
            {
                if (process.HasExited) throw new SetupException("online_install_failed");
                await Task.Delay(100, ct);
            }
            if (!File.Exists(ready)) throw new SetupException("online_install_failed");
            _status = _status with { State = "installing" };
            return Status;
        }
        finally { _gate.Release(); }
    }

    private static void RequireInstalled()
    {
        if (!OperatingSystem.IsWindows() || !SetupEnvironment.RunningFromInstaller()) throw new SetupException("online_install_first");
    }

    private static string ResolveHealthUrl(string installDir)
    {
        var configured = SetupEnvironment.ReadServiceEnvironment().GetValueOrDefault("Bridge__HttpUrls");
        if (configured is null)
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(installDir, "appsettings.json")));
            if (json.RootElement.TryGetProperty("Bridge", out var bridge) && bridge.TryGetProperty("HttpUrls", out var urls)) configured = urls.GetString();
        }
        if (!Uri.TryCreate((configured ?? SetupEnvironment.LocalApiBase).Split(';')[0], UriKind.Absolute, out var uri) ||
            uri.Scheme != "http" || !(uri.Host is "localhost" or "127.0.0.1")) throw new SetupException("online_health_url");
        return new Uri(uri, "/health").AbsoluteUri;
    }

    private static string ErrorCode(Exception e) => e is SetupException s ? s.Code : "online_unavailable";
}
