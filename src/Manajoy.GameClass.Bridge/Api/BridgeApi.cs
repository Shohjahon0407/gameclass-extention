using System.Security.Cryptography;
using System.Text;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Hosting;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Api;

/// <summary>Read-only HTTP API. There are deliberately no POST/PUT/DELETE routes.</summary>
internal static class BridgeApi
{
    public const string ApiKeyHeader = "X-Api-Key";
    private const int MaxEventsPerRequest = 500;

    public static WebApplication MapBridgeApi(this WebApplication app)
    {
        app.Use(RequireApiKey);

        app.MapGet("/health", (HttpContext context, SnapshotStore store, PushStatus push, AnalyticsPushStatus reports, IOptions<BridgeOptions> options, BridgeClock clock) =>
        {
            var health = store.Health(clock.Now(), clock.PollIntervalSeconds);
            var statusCode = health.Status is "ok" or "degraded" ? 200 : 503;

            // Remote callers without the key only learn whether the bridge is healthy, not why it is not.
            if (!IsLocal(context) && !HasValidKey(context, options.Value.ApiKey))
            {
                return Results.Json(new { status = health.Status, age_seconds = health.AgeSeconds }, BridgeJson.Compact, statusCode: statusCode);
            }

            return Results.Json(new
            {
                service = BridgeInfo.DisplayName,
                version = BridgeInfo.Version,
                mode = "read-only",
                health,
                push = push.Current,
                analytics_push = reports.Current,
            }, BridgeJson.Compact, statusCode: statusCode);
        });

        var api = app.MapGroup("/api/v1");

        api.MapGet("/snapshot", (SnapshotStore store, BridgeClock clock) => WithSnapshot(store, clock, s => s));

        api.MapGet("/pcs", (SnapshotStore store, BridgeClock clock, string? state) =>
            WithSnapshot(store, clock, s => new
            {
                fetched_at = s.FetchedAt,
                counts = s.Counts,
                pcs = string.IsNullOrWhiteSpace(state)
                    ? s.Pcs
                    : s.Pcs.Where(pc => pc.State.Equals(state, StringComparison.OrdinalIgnoreCase)
                        || pc.GameClassState.Equals(state, StringComparison.OrdinalIgnoreCase)).ToList(),
            }));

        api.MapGet("/pcs/{number:int}", (SnapshotStore store, BridgeClock clock, int number) =>
        {
            if (Unavailable(store, clock) is { } unavailable)
            {
                return unavailable;
            }

            var snapshot = store.Latest!;
            var pc = snapshot.Pcs.FirstOrDefault(p => p.Number == number);
            return pc is null
                ? Results.Json(new { error = "pc_not_found", number }, BridgeJson.Compact, statusCode: 404)
                : Results.Json(new { fetched_at = snapshot.FetchedAt, pc }, BridgeJson.Compact);
        });

        api.MapGet("/tariffs", (SnapshotStore store, BridgeClock clock) =>
            WithSnapshot(store, clock, s => new { fetched_at = s.FetchedAt, currency = s.Club.Currency, tariffs = s.Tariffs }));

        api.MapGet("/zones", (SnapshotStore store, BridgeClock clock) =>
            WithSnapshot(store, clock, s => new { fetched_at = s.FetchedAt, zones = s.Zones, groups = s.Groups }));

        api.MapGet("/events", (SnapshotStore store, long? since, int? limit) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, MaxEventsPerRequest);
            var page = store.EventsSince(since ?? 0, take);
            return Results.Json(new
            {
                events = page.Events,
                next_since = page.Events.Count > 0 ? page.Events[^1].Sequence : page.Gap ? page.LastSequence : since ?? 0,
                gap = page.Gap,
                oldest_seq = page.OldestSequence,
                last_seq = page.LastSequence,
            }, BridgeJson.Compact);
        });

        return app;
    }

    private static IResult WithSnapshot<T>(SnapshotStore store, BridgeClock clock, Func<ClubSnapshot, T> project) =>
        Unavailable(store, clock) ?? Results.Json(project(store.Latest!), BridgeJson.Compact);

    /// <summary>
    /// A snapshot that is no longer being refreshed must not be served as current data: consumers such as the
    /// backend's iCafe-style normalizer would otherwise keep showing frozen busy/free states and time left.
    /// </summary>
    private static IResult? Unavailable(SnapshotStore store, BridgeClock clock)
    {
        if (store.Latest is null)
        {
            return NotReady();
        }

        var health = store.Health(clock.Now(), clock.PollIntervalSeconds);
        return health.Status == "stale"
            ? Results.Json(new { error = "stale", detail = "GameClass polling is failing; see /health.", age_seconds = health.AgeSeconds, last_success_at = health.LastSuccessAt }, BridgeJson.Compact, statusCode: 503)
            : null;
    }

    private static IResult NotReady() =>
        Results.Json(new { error = "not_ready", detail = "No successful GameClass poll yet; see /health." }, BridgeJson.Compact, statusCode: 503);

    private static async Task RequireApiKey(HttpContext context, Func<Task> next)
    {
        var expected = context.RequestServices.GetRequiredService<IOptions<BridgeOptions>>().Value.ApiKey;
        if (string.IsNullOrEmpty(expected) || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next();
            return;
        }

        if (!HasValidKey(context, expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }

        await next();
    }

    private static bool HasValidKey(HttpContext context, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        // Comparing fixed-length digests keeps the comparison constant-time regardless of the key length.
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[ApiKeyHeader].ToString()));
        return CryptographicOperations.FixedTimeEquals(supplied, SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    private static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip);
}

/// <summary>Current time plus the poll interval, which together decide staleness.</summary>
internal sealed class BridgeClock(TimeProvider time, IOptions<BridgeOptions> options)
{
    public int PollIntervalSeconds { get; } = options.Value.PollIntervalSeconds;

    public DateTimeOffset Now() => time.GetLocalNow();
}
