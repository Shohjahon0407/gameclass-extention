using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Hosting;

/// <summary>Thread-safe holder of the latest snapshot, poll health and recent status-change events.</summary>
internal sealed class SnapshotStore(IOptions<BridgeOptions> options, TimeProvider time)
{
    private readonly object _sync = new();
    private readonly LinkedList<StatusChangeEvent> _events = new();
    private readonly int _maxEvents = options.Value.MaxEventsInMemory;
    // Seeded from the clock so sequence numbers keep increasing across restarts and a consumer's
    // stored ?since= value never hides newer events.
    private long _sequence = time.GetUtcNow().ToUnixTimeMilliseconds() * 1000;

    private ClubSnapshot? _latest;
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastAttemptAt;
    private string? _lastError;
    private int _consecutiveFailures;
    private string? _provider;

    public ClubSnapshot? Latest
    {
        get
        {
            lock (_sync)
            {
                return _latest;
            }
        }
    }

    public long NextSequence() => Interlocked.Increment(ref _sequence);

    public IReadOnlyList<StatusChangeEvent> Publish(ClubSnapshot snapshot, string? provider)
    {
        lock (_sync)
        {
            var changes = SnapshotDiff.Changes(_latest, snapshot, NextSequence);
            foreach (var change in changes)
            {
                _events.AddLast(change);
                if (_events.Count > _maxEvents)
                {
                    _events.RemoveFirst();
                }
            }

            _latest = snapshot;
            _lastSuccessAt = snapshot.FetchedAt;
            _lastAttemptAt = snapshot.FetchedAt;
            _lastError = null;
            _consecutiveFailures = 0;
            _provider = provider;
            return changes;
        }
    }

    public void RecordFailure(DateTimeOffset at, string error)
    {
        lock (_sync)
        {
            _lastAttemptAt = at;
            _lastError = error;
            _consecutiveFailures++;
        }
    }

    /// <summary>
    /// Events after <paramref name="sinceSequence"/>. <c>Gap</c> is true when events the caller has not seen are
    /// no longer available (evicted from memory, or produced before a bridge restart); the caller should then
    /// re-read the full snapshot and continue from <c>LastSequence</c>.
    /// </summary>
    public EventPage EventsSince(long sinceSequence, int limit)
    {
        lock (_sync)
        {
            var oldestAvailable = _events.First?.Value.Sequence ?? _sequence + 1;
            var gap = sinceSequence > 0 && sinceSequence < oldestAvailable - 1;
            var events = _events.Where(e => e.Sequence > sinceSequence).Take(limit).ToList();
            return new EventPage(events, gap, _events.First?.Value.Sequence, _sequence);
        }
    }

    public bool IsStale(DateTimeOffset now, int pollIntervalSeconds) => Health(now, pollIntervalSeconds).Status == "stale";

    public BridgeHealth Health(DateTimeOffset now, int pollIntervalSeconds)
    {
        lock (_sync)
        {
            int? age = _lastSuccessAt is { } success ? (int)Math.Max(0, (now - success).TotalSeconds) : null;
            // Stale after three missed polls (and never less than 30 s).
            var staleAfter = Math.Max(30, pollIntervalSeconds * 3);
            var status = _latest is null ? "starting" : age > staleAfter ? "stale" : "ok";
            // Polling works but GameClass itself is not live (GCServer closed, manager-only, or heartbeat missing):
            // data is served, every PC state is "unknown".
            if (status == "ok" && _latest!.GameClass is { ServerAlive: not true, HeartbeatDisabled: false })
            {
                status = "degraded";
            }
            if (_latest is null && _consecutiveFailures > 0)
            {
                status = "unavailable";
            }

            return new BridgeHealth(status, age, _lastSuccessAt, _lastAttemptAt, _consecutiveFailures, _lastError, _provider,
                _latest?.Pcs.Count, _sequence);
        }
    }
}

public sealed record BridgeHealth(
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] string Status,
    [property: System.Text.Json.Serialization.JsonPropertyName("age_seconds")] int? AgeSeconds,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("consecutive_failures")] int ConsecutiveFailures,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_error")] string? LastError,
    [property: System.Text.Json.Serialization.JsonPropertyName("db_provider")] string? DbProvider,
    [property: System.Text.Json.Serialization.JsonPropertyName("pc_count")] int? PcCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_event_seq")] long LastEventSequence);

internal sealed record EventPage(IReadOnlyList<StatusChangeEvent> Events, bool Gap, long? OldestSequence, long LastSequence);
