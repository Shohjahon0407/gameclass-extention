using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Bridge.Hosting;
using Manajoy.GameClass.Bridge.Snapshot;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>Reads the installed service's own read-only API on 127.0.0.1:8787 (health + snapshot).</summary>
internal sealed class LocalBridgeClient(HttpClient http)
{
    public const string HttpClientName = "local-bridge";

    internal sealed record HealthResponse(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("health")] BridgeHealth? Health,
        [property: JsonPropertyName("push")] PushReport? Push,
        [property: JsonPropertyName("analytics_push")] AnalyticsPushReport? AnalyticsPush = null);

    public sealed record Reading(LocalSummary Local, PushInfo? Push);

    public async Task<Reading> ReadAsync(bool includePcs, CancellationToken cancellationToken)
    {
        HealthResponse? health;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await http.GetAsync($"{SetupEnvironment.LocalApiBase}/health", timeout.Token);
            health = JsonSerializer.Deserialize<HealthResponse>(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return new Reading(Unreachable(ex.Message), null);
        }

        ClubSnapshot? snapshot = null;
        if (includePcs && health?.Health?.Status is "ok" or "degraded")
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var json = await http.GetStringAsync($"{SetupEnvironment.LocalApiBase}/api/v1/snapshot", timeout.Token);
                snapshot = JsonSerializer.Deserialize<ClubSnapshot>(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
            {
                // Health alone is still worth showing.
            }
        }

        return new Reading(Summarize(health, snapshot), ToPushInfo(health?.Push));
    }

    /// <summary>The service's last report push (null when unreachable or older than 0.9.0).</summary>
    public async Task<AnalyticsPushReport?> ReadReportsPushAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var json = await http.GetStringAsync($"{SetupEnvironment.LocalApiBase}/health", timeout.Token);
            return JsonSerializer.Deserialize<HealthResponse>(json)?.AnalyticsPush;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The full local snapshot (tariffs, sessions) for the cashier's price quotes; null when not available.</summary>
    public async Task<ClubSnapshot?> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var json = await http.GetStringAsync($"{SetupEnvironment.LocalApiBase}/api/v1/snapshot", timeout.Token);
            return JsonSerializer.Deserialize<ClubSnapshot>(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static LocalSummary Summarize(HealthResponse? response, ClubSnapshot? snapshot)
    {
        var health = response?.Health;
        var counts = snapshot?.Counts ?? new Dictionary<string, int>();
        var pcs = snapshot?.Pcs
            .OrderBy(pc => pc.Number)
            .Select(pc => new PcTile(
                pc.Number,
                pc.ExternalName,
                pc.AreaName,
                pc.State,
                pc.GameClassState,
                pc.TimeLeftMinutes,
                pc.Session?.Postpay ?? false,
                pc.UpcomingReservations.Count > 0 ? pc.UpcomingReservations.Min(r => r.StartsAt) : null))
            .ToList() ?? [];

        return new LocalSummary(
            Reachable: true,
            Status: health?.Status ?? "starting",
            AgeSeconds: health?.AgeSeconds,
            LastError: health?.LastError,
            DbProvider: health?.DbProvider,
            ServerAlive: snapshot?.GameClass.ServerAlive,
            HeartbeatAgeSeconds: snapshot?.GameClass.HeartbeatAgeSeconds,
            HeartbeatMissing: snapshot?.GameClass.HeartbeatMissing ?? false,
            Counts: counts,
            Pcs: pcs,
            BridgeVersion: response?.Version);
    }

    private static LocalSummary Unreachable(string error) =>
        new(false, "unreachable", null, error, null, null, null, false, new Dictionary<string, int>(), [], null);

    private static PushInfo? ToPushInfo(PushReport? push) => push is null
        ? null
        : new PushInfo(push.Enabled, push.LastSuccessAt, push.LastAttemptAt, push.LastHttpStatus, push.LastError, push.ConsecutiveFailures);
}
