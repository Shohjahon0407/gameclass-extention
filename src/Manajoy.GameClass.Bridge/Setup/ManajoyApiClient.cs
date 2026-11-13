using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Bridge.Hosting;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>The Manajoy backend calls the setup window makes: pairing (code → key), status and the club map (with the key).</summary>
internal sealed class ManajoyApiClient(HttpClient http)
{
    public const string HttpClientName = "manajoy-setup";

    public sealed record PairResponse(
        [property: JsonPropertyName("seller_id")] int SellerId,
        [property: JsonPropertyName("club_name")] string ClubName,
        [property: JsonPropertyName("push_path")] string PushPath,
        [property: JsonPropertyName("bridge_key")] string BridgeKey,
        [property: JsonPropertyName("live_enabled")] bool LiveEnabled);

    private sealed record StatusResponse(
        [property: JsonPropertyName("seller_id")] int SellerId,
        [property: JsonPropertyName("club_name")] string? ClubName,
        [property: JsonPropertyName("live_enabled")] bool LiveEnabled,
        [property: JsonPropertyName("stations_total")] int StationsTotal,
        [property: JsonPropertyName("stations_confirmed")] int StationsConfirmed,
        [property: JsonPropertyName("stations_unmapped")] int StationsUnmapped,
        [property: JsonPropertyName("live")] StatusLive? Live);

    private sealed record StatusLive(
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("age_seconds")] int? AgeSeconds);

    /// <summary>Any HTTP answer from the backend counts as reachable (we only test the network path).</summary>
    public async Task<bool> IsReachableAsync(string apiBase, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await http.GetAsync($"{apiBase}/health", timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<PairResponse> PairAsync(string apiBase, string code, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(
                $"{apiBase}/integrations/gameclass/pair",
                new { code, bridge_version = BridgeInfo.Version, machine_name = Environment.MachineName },
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SetupException("pair_network", ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<PairResponse>(body)
                       ?? throw new SetupException("pair_failed", "empty response");
            }

            var (errorCode, message) = ParseError(body);
            throw response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => new SetupException("pair_rate_limited", message),
                _ when errorCode == "invalid_code" => new SetupException("pair_invalid_code", message),
                _ when errorCode == "other_provider" => new SetupException("pair_other_provider", message),
                _ => new SetupException("pair_failed", $"HTTP {(int)response.StatusCode}: {message}"),
            };
        }
    }

    public async Task<ManajoyInfo> GetStatusAsync(string statusUrl, string bridgeKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            request.Headers.Add("X-Bridge-Key", bridgeKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable(response.StatusCode == HttpStatusCode.Unauthorized ? "key_rejected" : $"HTTP {(int)response.StatusCode}: {ParseError(body).Message}");
            }

            var status = JsonSerializer.Deserialize<StatusResponse>(body)!;
            return new ManajoyInfo(true, status.ClubName, status.SellerId, status.LiveEnabled, status.StationsTotal,
                status.StationsConfirmed, status.StationsUnmapped, status.Live?.State ?? "unavailable", status.Live?.AgeSeconds, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return Unreachable(ex.Message);
        }
    }

    /// <summary>The stock book Manajoy keeps (GET /integrations/gameclass/{id}/bar/entries, X-Bridge-Key).</summary>
    public async Task<Bar.BarBackup?> GetBarBackupAsync(string url, string bridgeKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Bridge-Key", bridgeKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new SetupException("bar_restore", $"HTTP {(int)response.StatusCode}");
            }

            return JsonSerializer.Deserialize<Bar.BarBackup>(await response.Content.ReadAsStringAsync(timeout.Token), Hosting.BridgeJson.SnakeCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            throw new SetupException("bar_restore", ex.Message, ex);
        }
    }

    private sealed record LayoutResponse(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("pcs")] List<LayoutResponsePc>? Pcs,
        [property: JsonPropertyName("other_placed")] int OtherPlaced);

    private sealed record LayoutResponsePc(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("x")] int? X,
        [property: JsonPropertyName("y")] int? Y);

    private sealed record LayoutSaveResponse(
        [property: JsonPropertyName("saved")] int Saved,
        [property: JsonPropertyName("skipped")] List<string>? Skipped,
        [property: JsonPropertyName("version")] string Version);

    /// <summary>Manajoy's club map for this bridge (GET /integrations/gameclass/{id}/layout, X-Bridge-Key).</summary>
    public async Task<RemoteLayout> GetLayoutAsync(string layoutUrl, string bridgeKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, layoutUrl);
        var body = await SendLayoutAsync(request, bridgeKey, cancellationToken);
        return ToRemote(JsonSerializer.Deserialize<LayoutResponse>(body)
                        ?? throw new LayoutRemoteException("failed", "empty response"));
    }

    /// <summary>Saves the PC points; 409 means the map changed elsewhere since <paramref name="baseVersion"/>.</summary>
    public async Task<RemoteSaveResult> PutLayoutAsync(string layoutUrl, string bridgeKey, IReadOnlyDictionary<string, LayoutPoint> pcs,
        string? baseVersion, bool force, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, layoutUrl)
        {
            Content = JsonContent.Create(new
            {
                points = pcs.Select(pair => new { name = pair.Key, x = pair.Value.X, y = pair.Value.Y }).ToArray(),
                base_version = baseVersion,
                force,
            }),
        };
        var body = await SendLayoutAsync(request, bridgeKey, cancellationToken);
        var saved = JsonSerializer.Deserialize<LayoutSaveResponse>(body) ?? throw new LayoutRemoteException("failed", "empty response");
        return new RemoteSaveResult(saved.Saved, saved.Skipped ?? [], saved.Version);
    }

    private async Task<string> SendLayoutAsync(HttpRequestMessage request, string bridgeKey, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Bridge-Key", bridgeKey);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            if (response.StatusCode == HttpStatusCode.Conflict && ParseConflictServer(body) is { } server)
            {
                throw new LayoutConflictException(server);
            }

            throw response.StatusCode == HttpStatusCode.Unauthorized
                ? new LayoutRemoteException("key_rejected", "HTTP 401")
                : new LayoutRemoteException("failed", $"HTTP {(int)response.StatusCode}: {ParseError(body).Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new LayoutRemoteException("offline", ex.Message);
        }
        catch (JsonException ex)
        {
            throw new LayoutRemoteException("failed", ex.Message);
        }
    }

    /// <summary>409 body: {"success":false,"error":{"code":"layout_conflict","message","server":{...}}}.</summary>
    internal static RemoteLayout? ParseConflictServer(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var name in new[] { "error", "detail" })
            {
                if (document.RootElement.TryGetProperty(name, out var error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object)
                {
                    var layout = server.Deserialize<LayoutResponse>();
                    return layout is null ? null : ToRemote(layout);
                }
            }
        }
        catch (JsonException)
        {
            // Not the conflict we know: treated as a plain error.
        }

        return null;
    }

    private static RemoteLayout ToRemote(LayoutResponse layout) => new(
        layout.Version,
        (layout.Pcs ?? []).Select(pc => new RemotePc(pc.Name, pc.X, pc.Y)).ToList(),
        layout.OtherPlaced);

    private static ManajoyInfo Unreachable(string error) => new(false, null, null, null, null, null, null, null, null, error);

    /// <summary>
    /// Backend errors look like {"success":false,"error":{"code","message"}} or {"error":"text"}
    /// (FastAPI handlers) or {"detail":...} (validation / middleware).
    /// </summary>
    internal static (string? Code, string Message) ParseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            foreach (var name in new[] { "error", "detail" })
            {
                if (!root.TryGetProperty(name, out var element))
                {
                    continue;
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    return (null, element.GetString() ?? string.Empty);
                }

                if (element.ValueKind == JsonValueKind.Object)
                {
                    var code = element.TryGetProperty("code", out var c) ? c.GetString() : null;
                    var message = element.TryGetProperty("message", out var m) ? m.GetString() : element.ToString();
                    return (code, message ?? string.Empty);
                }
            }

            if (root.TryGetProperty("message", out var plain) && plain.ValueKind == JsonValueKind.String)
            {
                return (null, plain.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // Not JSON (proxy error page): fall through to the raw text.
        }

        return (null, body.Length > 200 ? body[..200] : body);
    }
}

/// <summary>The paired club's map endpoint, bound to the bridge key.</summary>
internal sealed class ManajoyLayoutRemote(ManajoyApiClient client, string layoutUrl, string bridgeKey) : ILayoutRemote
{
    public Task<RemoteLayout> GetAsync(CancellationToken cancellationToken) =>
        client.GetLayoutAsync(layoutUrl, bridgeKey, cancellationToken);

    public Task<RemoteSaveResult> PutAsync(IReadOnlyDictionary<string, LayoutPoint> pcs, string? baseVersion, bool force,
        CancellationToken cancellationToken) =>
        client.PutLayoutAsync(layoutUrl, bridgeKey, pcs, baseVersion, force, cancellationToken);
}
