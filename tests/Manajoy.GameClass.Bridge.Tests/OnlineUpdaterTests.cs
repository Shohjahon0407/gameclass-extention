using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

public class OnlineUpdaterTests
{
    private static ReleaseManifest Valid => new("9.2.1", "download/ManajoyGameClass-Setup-9.2.1.exe", 128, new string('a', 64));
    private static byte[] Payload(ReleaseManifest value) => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Only_the_pinned_signer_can_authorize_a_manifest()
    {
        using var signer = RSA.Create(2048);
        using var wrong = RSA.Create(2048);
        var bytes = Payload(Valid);
        var signature = signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.Equal(Valid, OnlineUpdater.VerifyManifest(bytes, signature, signer, OnlineUpdater.Feed));
        Assert.Throws<SetupException>(() => OnlineUpdater.VerifyManifest(bytes, signature, wrong, OnlineUpdater.Feed));
        bytes[^2] ^= 1;
        Assert.Throws<SetupException>(() => OnlineUpdater.VerifyManifest(bytes, signature, signer, OnlineUpdater.Feed));
    }

    [Theory]
    [InlineData("https://other.example/installer.exe")]
    [InlineData("//other.example/installer.exe")]
    [InlineData("../installer.exe")]
    [InlineData("download/ManajoyGameClass-Setup-9.2.1.exe?redirect=1")]
    [InlineData("download/ManajoyGameClass-Setup-9.2.0.exe")]
    public void Even_signed_releases_cannot_change_download_origin_or_name(string file)
    {
        Assert.Throws<SetupException>(() => OnlineUpdater.ValidateManifest(Valid with { File = file }, OnlineUpdater.Feed));
    }

    [Fact]
    public void Installer_size_version_and_digest_are_validated()
    {
        foreach (var release in new[] { Valid with { Size = 0 }, Valid with { Size = OnlineUpdater.MaxInstallerSize + 1 },
                     Valid with { Version = "not-a-version" }, Valid with { Sha256 = "abcd" }, Valid with { Sha256 = new string('z', 64) } })
            Assert.Throws<SetupException>(() => OnlineUpdater.ValidateManifest(release, OnlineUpdater.Feed));
        Assert.Equal("https://www.manajoy.uz/gameclass/download/ManajoyGameClass-Setup-9.2.1.exe",
            OnlineUpdater.ValidateManifest(Valid, OnlineUpdater.Feed).AbsoluteUri);
    }

    [Fact]
    public async Task Truncated_or_corrupted_downloads_are_never_installable()
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("installer-fixture");
            await File.WriteAllBytesAsync(file, bytes);
            var manifest = Valid with { Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
            await OnlineUpdater.VerifyFileAsync(file, manifest, default);
            bytes[0] ^= 1;
            await File.WriteAllBytesAsync(file, bytes);
            await Assert.ThrowsAsync<SetupException>(() => OnlineUpdater.VerifyFileAsync(file, manifest, default));
            await File.WriteAllBytesAsync(file, bytes[..^1]);
            await Assert.ThrowsAsync<SetupException>(() => OnlineUpdater.VerifyFileAsync(file, manifest, default));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Unpublished_feed_does_not_offer_an_update()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var updater = new OnlineUpdater(client, false);
        var status = await updater.CheckAsync(default);
        Assert.Equal("failed", status.State);
        Assert.False(status.Available);
        Assert.Equal("online_unavailable", status.Error);
    }

    [Fact]
    public async Task Oversized_manifest_is_rejected_before_parsing()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[65_537]) })));
        var status = await new OnlineUpdater(client, false).CheckAsync(default);
        Assert.Equal("online_invalid_release", status.Error);
        Assert.False(status.Available);
    }

    [Fact]
    public async Task Concurrent_checks_are_serialized()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, ct) =>
        {
            entered.SetResult();
            await finish.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var updater = new OnlineUpdater(client, false);
        var first = updater.CheckAsync(default);
        await entered.Task;
        Assert.Equal("busy", (await Assert.ThrowsAsync<SetupException>(() => updater.CheckAsync(default))).Code);
        finish.SetResult();
        await first;
    }

    [Fact]
    public async Task Demo_exercises_the_UI_flow_without_installing_anything()
    {
        using var client = new HttpClient();
        var updater = new OnlineUpdater(client, true);
        await Assert.ThrowsAsync<SetupException>(() => updater.ApplyAsync(default));
        Assert.True((await updater.CheckAsync(default)).Available);
        Assert.Equal("ready", (await updater.DownloadAsync(default)).State);
        Assert.Equal("current", (await updater.ApplyAsync(default)).State);
        Assert.False(updater.Status.Available);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
