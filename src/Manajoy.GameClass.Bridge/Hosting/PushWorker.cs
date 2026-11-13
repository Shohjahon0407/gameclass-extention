using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Hosting;

/// <summary>
/// Sends the PC states of the latest snapshot to the Manajoy backend: right after a status change,
/// otherwise every Push:IntervalSeconds. The backend cannot reach the club LAN, so the bridge pushes.
/// Only <see cref="PushPayload"/> leaves the machine; nothing is ever received back or written to GameClass.
/// </summary>
internal sealed class PushWorker(
    SnapshotStore store,
    PushStatus status,
    IHttpClientFactory httpClients,
    IOptions<BridgeOptions> options,
    TimeProvider time,
    ILogger<PushWorker> logger) : BackgroundService
{
    public const string HttpClientName = "manajoy-push";
    public const string KeyHeader = "X-Bridge-Key";

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly PushOptions _push = options.Value.Push;
    private DateTimeOffset? _lastPushedFetchedAt;
    private long _lastPushedEventSeq;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private TimeSpan _backoff = TimeSpan.Zero;
    private string? _lastProblem;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_push.IsEnabled)
        {
            return;
        }

        logger.LogInformation("Pushing snapshots to {Host} every {Interval}s", new Uri(_push.Url!).Host, _push.IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PushIfDueAsync(stoppingToken);
        }
    }

    internal async Task PushIfDueAsync(CancellationToken cancellationToken)
    {
        var snapshot = store.Latest;
        var now = time.GetUtcNow();
        if (snapshot is null || now < _notBefore || snapshot.FetchedAt == _lastPushedFetchedAt)
        {
            return;
        }

        var lastEventSeq = store.Health(time.GetLocalNow(), options.Value.PollIntervalSeconds).LastEventSequence;
        var changed = lastEventSeq != _lastPushedEventSeq;
        if (!changed && now - _lastAttempt < TimeSpan.FromSeconds(_push.IntervalSeconds))
        {
            return;
        }

        _lastAttempt = now;
        var outcome = await SendAsync(snapshot, cancellationToken);
        status.Record(now, outcome.Success, outcome.HttpStatus, outcome.Problem);
        if (outcome.Success)
        {
            _lastPushedFetchedAt = snapshot.FetchedAt;
            _lastPushedEventSeq = lastEventSeq;
            _backoff = TimeSpan.Zero;
            if (_lastProblem is not null)
            {
                logger.LogInformation("Push to Manajoy works again");
                _lastProblem = null;
            }

            return;
        }

        // Back off: 5 s, 10 s, 20 s ... up to 5 min. Auth/disabled answers will not fix themselves quickly.
        _backoff = outcome.HttpStatus is 401 or 403 or 409
            ? MaxBackoff
            : TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, Math.Max(TimeSpan.FromSeconds(5).Ticks, _backoff.Ticks * 2)));
        _notBefore = now + _backoff;
        if (outcome.Problem != _lastProblem)
        {
            _lastProblem = outcome.Problem;
            logger.LogWarning("Push to Manajoy failed: {Problem} (retry in {Seconds}s)", outcome.Problem, (int)_backoff.TotalSeconds);
        }
    }

    private async Task<PushOutcome> SendAsync(ClubSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, _push.Url)
            {
                Content = new StringContent(JsonSerializer.Serialize(PushPayload.From(snapshot), BridgeJson.Compact), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(KeyHeader, _push.Key);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ManajoyGameClassBridge", BridgeInfo.Version));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_push.TimeoutSeconds));
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new PushOutcome(true, (int)response.StatusCode, null);
            }

            return new PushOutcome(false, (int)response.StatusCode, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "rejected: bridge key is wrong",
                HttpStatusCode.Conflict => "rejected: live status is disabled for this club in Manajoy",
                HttpStatusCode.UnprocessableEntity => "rejected: snapshot format not accepted",
                _ => $"HTTP {(int)response.StatusCode}",
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PushOutcome(false, null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new PushOutcome(false, null, $"network: {ex.HttpRequestError}");
        }
    }

    private sealed record PushOutcome(bool Success, int? HttpStatus, string? Problem);
}

/// <summary>
/// What leaves the club: whether each PC is free, busy, offline or unknown, and minutes left. Money, tariffs,
/// sessions, reservations and IP addresses are not part of it (daily report totals go separately, see AnalyticsPushWorker).
/// </summary>
internal sealed record PushPayload(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("bridge_version")] string BridgeVersion,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset FetchedAt,
    [property: JsonPropertyName("gameclass")] PushServerInfo GameClass,
    [property: JsonPropertyName("pcs")] IReadOnlyList<PushPc> Pcs)
{
    public static PushPayload From(ClubSnapshot snapshot) => new(
        snapshot.Version,
        snapshot.Provider,
        snapshot.BridgeVersion,
        snapshot.FetchedAt,
        new PushServerInfo(snapshot.GameClass.ServerAlive, snapshot.GameClass.HeartbeatAgeSeconds, snapshot.GameClass.HeartbeatMissing),
        snapshot.Pcs.Select(pc => new PushPc(pc.Number, pc.State, pc.ICafeCompat)).ToList());
}

internal sealed record PushServerInfo(
    [property: JsonPropertyName("server_alive")] bool? ServerAlive,
    [property: JsonPropertyName("heartbeat_age_seconds")] int? HeartbeatAgeSeconds,
    [property: JsonPropertyName("heartbeat_missing")] bool HeartbeatMissing);

internal sealed record PushPc(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("icafe_compat")] ICafeCompatPc ICafeCompat);

/// <summary>Last push result, reported by /health.</summary>
internal sealed class PushStatus(IOptions<BridgeOptions> options)
{
    private readonly object _sync = new();
    private PushReport _report = new(options.Value.Push.IsEnabled, null, null, null, null, 0);

    public PushReport Current
    {
        get
        {
            lock (_sync)
            {
                return _report;
            }
        }
    }

    public void Record(DateTimeOffset at, bool success, int? httpStatus, string? problem)
    {
        lock (_sync)
        {
            _report = success
                ? _report with { LastSuccessAt = at, LastAttemptAt = at, LastHttpStatus = httpStatus, LastError = null, ConsecutiveFailures = 0 }
                : _report with { LastAttemptAt = at, LastHttpStatus = httpStatus, LastError = problem, ConsecutiveFailures = _report.ConsecutiveFailures + 1 };
        }
    }
}

public sealed record PushReport(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt,
    [property: JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: JsonPropertyName("last_http_status")] int? LastHttpStatus,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures);
