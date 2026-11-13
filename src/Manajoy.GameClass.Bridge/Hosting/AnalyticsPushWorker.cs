using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Bridge.Analytics;
using Manajoy.GameClass.Bridge.Configuration;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Hosting;

/// <summary>
/// Sends the owner's daily GameClass reports (<see cref="DayRollup"/>) to Manajoy, where ManaJoy Biznes shows them.
/// Every cycle the last 3 business days (7 once a day) are recomputed and only days whose fingerprint changed are
/// sent; meanwhile the whole older history goes up newest-first, one batch per cycle so MSDE is never busy for
/// long. Read-only as everything else: rows come from <see cref="AnalyticsReader"/>; no customer names or phones.
/// </summary>
internal sealed class AnalyticsPushWorker(
    Func<IAnalyticsSource> source,
    Func<int> dayStartHour,
    AnalyticsPushStatus status,
    IHttpClientFactory httpClients,
    IOptions<BridgeOptions> options,
    TimeProvider time,
    ILogger<AnalyticsPushWorker> logger,
    string? statePath = null,
    Func<Bar.BarLedger?>? barLedger = null) : BackgroundService
{
    public const int PayloadVersion = 1;

    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BackfillPause = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DisabledPause = TimeSpan.FromHours(6);

    private readonly PushOptions _push = options.Value.Push;
    private readonly AnalyticsPushOptions _analytics = options.Value.Analytics;
    private readonly string _statePath = statePath ?? BridgePaths.Resolve("output/analytics-sync.json");
    private int _batchDays = options.Value.Analytics.BatchDays;
    private TimeSpan _backoff = TimeSpan.Zero;
    private string? _lastProblem;

    public static string? UrlFromPushUrl(string? pushUrl) => Sibling(pushUrl, "analytics");

    public static string? BarUrlFromPushUrl(string? pushUrl) => Sibling(pushUrl, "bar");

    public static string? BarEntriesUrlFromPushUrl(string? pushUrl) => Sibling(pushUrl, "bar/entries");

    private static string? Sibling(string? pushUrl, string name) =>
        pushUrl is not null && pushUrl.EndsWith("/snapshot", StringComparison.Ordinal) ? pushUrl[..^"snapshot".Length] + name : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_push.IsEnabled || !_analytics.Enabled || UrlFromPushUrl(_push.Url) is null)
        {
            return;
        }

        logger.LogInformation("Sending daily reports to {Host} every {Interval} min", new Uri(_push.Url!).Host, _analytics.IntervalMinutes);
        var delay = FirstDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(delay, time, stoppingToken);
            try
            {
                delay = await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delay = Fail(null, ex is Data.GameClassUnavailableException or System.Data.Common.DbException
                    ? "gameclass: " + ex.Message
                    : ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>One cycle; returns how long to wait before the next one.</summary>
    internal async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken)
    {
        var hour = Math.Clamp(dayStartHour(), 0, 23);
        var state = LoadState();
        if (state.DayStartHour != hour)
        {
            // Other day boundaries make every stored day wrong: send the whole history again.
            state = new SyncState { DayStartHour = hour };
        }

        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime.AddHours(-hour));
        var weekly = state.WeekCheck != today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recentFrom = today.AddDays(weekly ? -6 : -2);

        if (await SendRangeAsync(recentFrom, today, state, cancellationToken) is { } recentFailure)
        {
            return recentFailure;
        }

        if (weekly)
        {
            state.WeekCheck = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!state.BackfillDone)
        {
            var before = state.BackfillBefore is { } b ? DateOnly.ParseExact(b, "yyyy-MM-dd", CultureInfo.InvariantCulture) : recentFrom;
            var first = state.HistoryFrom is { } h ? DateOnly.FromDateTime(h.AddHours(-hour)) : (DateOnly?)null;
            if (first is null || before <= first)
            {
                state.BackfillDone = true;
            }
            else
            {
                var batchFrom = before.AddDays(-_batchDays);
                batchFrom = batchFrom < first.Value ? first.Value : batchFrom;
                if (await SendRangeAsync(batchFrom, before.AddDays(-1), state, cancellationToken) is { } backfillFailure)
                {
                    SaveState(state);
                    return backfillFailure;
                }

                state.BackfillBefore = batchFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                state.BackfillDone = batchFrom <= first.Value;
            }
        }

        foreach (var old in state.Sent.Keys.Where(k => string.CompareOrdinal(k, today.AddDays(-40).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0).ToList())
        {
            state.Sent.Remove(old);
        }

        if (await PushBarAsync(hour, state, cancellationToken) is { } barFailure)
        {
            SaveState(state);
            return barFailure;
        }

        SaveState(state);
        status.Progress(state.BackfillDone, state.BackfillBefore);
        _backoff = TimeSpan.Zero;
        return state.BackfillDone ? TimeSpan.FromMinutes(_analytics.IntervalMinutes) : BackfillPause;
    }

    /// <summary>Null on success (or nothing to send); otherwise the delay before the next try.</summary>
    private async Task<TimeSpan?> SendRangeAsync(DateOnly from, DateOnly to, SyncState state, CancellationToken cancellationToken)
    {
        var hour = state.DayStartHour;
        var start = from.ToDateTime(new TimeOnly(hour, 0));
        var end = to.AddDays(1).ToDateTime(new TimeOnly(hour, 0));
        var data = await source().LoadAsync(start, end, cancellationToken);
        state.HistoryFrom = data.HistoryFrom;

        var changed = new List<DayRollup>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var rollup = AnalyticsBuilder.Rollup(day, hour, data);
            if (!state.Sent.TryGetValue(rollup.Date, out var sent) || sent != rollup.Fingerprint)
            {
                changed.Add(rollup);
            }
        }

        foreach (var chunk in changed.Chunk(_batchDays))
        {
            var payload = new AnalyticsPayload(PayloadVersion, "gameclass", BridgeInfo.Version, data.Now,
                (int)TimeZoneInfo.Local.GetUtcOffset(data.Now).TotalMinutes, hour, data.Currency, data.HistoryFrom, chunk);
            var outcome = await SendAsync(payload, cancellationToken);
            if (!outcome.Success)
            {
                if (outcome.HttpStatus is 413 or 422 && _batchDays > 1)
                {
                    _batchDays = Math.Max(1, _batchDays / 2);
                    status.Record(time.GetUtcNow(), false, outcome.HttpStatus, outcome.Problem);
                    return BackfillPause;
                }

                return Fail(outcome.HttpStatus, outcome.Problem);
            }

            foreach (var day in chunk)
            {
                state.Sent[day.Date] = day.Fingerprint;
            }

            status.Record(time.GetUtcNow(), true, outcome.HttpStatus, null, chunk.Length);
            if (_lastProblem is not null)
            {
                logger.LogInformation("Daily reports reach Manajoy again");
                _lastProblem = null;
            }
        }

        return null;
    }

    private TimeSpan Fail(int? httpStatus, string? problem)
    {
        status.Record(time.GetUtcNow(), false, httpStatus, problem);
        if (problem != _lastProblem)
        {
            logger.LogWarning("Daily reports not sent: {Problem}", problem);
            _lastProblem = problem;
        }

        if (httpStatus == 409)
        {
            return DisabledPause;
        }

        _backoff = httpStatus is 401 or 403
            ? TimeSpan.FromMinutes(5)
            : _backoff == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, _backoff.Ticks * 2));
        return _backoff;
    }

    /// <summary>
    /// The bar stock book (Bar/bar.json, written by the setup window) with GameClass's sales subtracted: sent when a
    /// number changed, together with book lines Manajoy has not confirmed yet (its backup after a PC change).
    /// </summary>
    private async Task<TimeSpan?> PushBarAsync(int dayStartHour, SyncState state, CancellationToken cancellationToken)
    {
        if (barLedger?.Invoke() is not { Entries.Count: > 0 } ledger || source() is not Bar.IBarSource bar)
        {
            return null;
        }

        // "Sold today / 7 days" needs the last week even when every anchor is newer.
        var weekAgo = time.GetLocalNow().DateTime.Date.AddHours(dayStartHour).AddDays(-7);
        var data = await bar.LoadBarAsync(ledger.SalesFrom() is { } from && from < weekAgo ? from : weekAgo, cancellationToken);
        var overview = Bar.BarStockCalculator.Compute(data, ledger, dayStartHour);
        var fingerprint = Bar.BarStockCalculator.Fingerprint(overview);
        var unsent = ledger.Entries.Where(e => !state.BarSent.Contains(e.Uid)).Take(5000).ToList();
        if (fingerprint == state.BarFingerprint && unsent.Count == 0)
        {
            return null;
        }

        var payload = new BarPayload(1, data.Now, (int)TimeZoneInfo.Local.GetUtcOffset(data.Now).TotalMinutes, data.Currency,
            overview.Items.Select(i => new BarPushItem(i.ItemId, i.Name, i.Price, i.Tracked, i.Remaining, i.LowThreshold, i.State,
                i.SoldToday, i.Sold7Days, i.LastReceiveAt)).ToList(),
            unsent, ledger.Items);
        var outcome = await SendAsync(BarUrlFromPushUrl(_push.Url), JsonSerializer.Serialize(payload, BridgeJson.SnakeCase), cancellationToken);
        if (!outcome.Success)
        {
            return Fail(outcome.HttpStatus, "bar: " + outcome.Problem);
        }

        state.BarFingerprint = fingerprint;
        state.BarSent.UnionWith(unsent.Select(e => e.Uid));
        return null;
    }

    private Task<SendOutcome> SendAsync(AnalyticsPayload payload, CancellationToken cancellationToken) =>
        SendAsync(UrlFromPushUrl(_push.Url), JsonSerializer.Serialize(payload, BridgeJson.SnakeCase), cancellationToken);

    private async Task<SendOutcome> SendAsync(string? url, string json, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClients.CreateClient(PushWorker.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(PushWorker.KeyHeader, _push.Key);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ManajoyGameClassBridge", BridgeInfo.Version));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _push.TimeoutSeconds)));
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return new SendOutcome(true, (int)response.StatusCode, null);
            }

            return new SendOutcome(false, (int)response.StatusCode, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "rejected: bridge key is wrong",
                HttpStatusCode.Conflict => "rejected: reports are turned off for this club in Manajoy",
                HttpStatusCode.UnprocessableEntity => "rejected: report format not accepted",
                HttpStatusCode.RequestEntityTooLarge => "rejected: request too large",
                HttpStatusCode.NotFound => "Manajoy does not accept reports yet",
                _ => $"HTTP {(int)response.StatusCode}",
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendOutcome(false, null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new SendOutcome(false, null, $"network: {ex.HttpRequestError}");
        }
    }

    private SyncState LoadState()
    {
        try
        {
            var file = new FileInfo(_statePath);
            if (file is { Exists: true, Length: < 1_000_000 })
            {
                return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(_statePath), BridgeJson.SnakeCase) ?? new SyncState();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Report sync state unreadable ({Message}); starting over", ex.Message);
        }

        return new SyncState();
    }

    private void SaveState(SyncState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var temp = _statePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, BridgeJson.SnakeCase));
            File.Move(temp, _statePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing it only means some days are sent once more.
            logger.LogDebug("Report sync state not saved: {Message}", ex.Message);
        }
    }

    internal sealed class SyncState
    {
        public int DayStartHour { get; set; } = -1;

        public string? BackfillBefore { get; set; }

        public bool BackfillDone { get; set; }

        public DateTime? HistoryFrom { get; set; }

        public string? WeekCheck { get; set; }

        public Dictionary<string, string> Sent { get; set; } = [];

        public string? BarFingerprint { get; set; }

        /// <summary>Stock book lines Manajoy confirmed (its backup); each line is sent once.</summary>
        public HashSet<string> BarSent { get; set; } = [];
    }

    private sealed record SendOutcome(bool Success, int? HttpStatus, string? Problem);
}

/// <summary>
/// POST {push base}/analytics. Times are the club PC's local clock (as GameClass stores them) with
/// <c>utc_offset_minutes</c> next to them.
/// </summary>
internal sealed record AnalyticsPayload(
    [property: JsonPropertyName("v")] int Version,
    string Provider,
    string BridgeVersion,
    DateTime GeneratedAt,
    int UtcOffsetMinutes,
    int DayStartHour,
    string? Currency,
    DateTime? HistoryFrom,
    IReadOnlyList<DayRollup> Days);

/// <summary>POST {push base}/bar: stock per item (GameClass sales already subtracted) and new stock book lines.</summary>
internal sealed record BarPayload(
    [property: JsonPropertyName("v")] int Version,
    DateTime GeneratedAt,
    int UtcOffsetMinutes,
    string? Currency,
    IReadOnlyList<BarPushItem> Items,
    IReadOnlyList<Bar.BarEntry> Entries,
    IReadOnlyList<Bar.BarItemSettings> Settings);

internal sealed record BarPushItem(int ItemId, string Name, decimal Price, bool Tracked, int? Remaining, int LowThreshold, string State,
    int SoldToday, [property: JsonPropertyName("sold_7d")] int Sold7Days, DateTime? LastReceiveAt);

/// <summary>Last report push, shown by /health and the setup window.</summary>
internal sealed class AnalyticsPushStatus(IOptions<BridgeOptions> options)
{
    private readonly object _sync = new();
    private AnalyticsPushReport _report = new(
        options.Value.Push.IsEnabled && options.Value.Analytics.Enabled, null, null, null, null, 0, false, null, 0);

    public AnalyticsPushReport Current
    {
        get
        {
            lock (_sync)
            {
                return _report;
            }
        }
    }

    public void Record(DateTimeOffset at, bool success, int? httpStatus, string? problem, int days = 0)
    {
        lock (_sync)
        {
            _report = success
                ? _report with { LastSuccessAt = at, LastAttemptAt = at, LastHttpStatus = httpStatus, LastError = null, ConsecutiveFailures = 0, DaysSent = _report.DaysSent + days }
                : _report with { LastAttemptAt = at, LastHttpStatus = httpStatus, LastError = problem, ConsecutiveFailures = _report.ConsecutiveFailures + 1 };
        }
    }

    public void Progress(bool backfillDone, string? backfillBefore)
    {
        lock (_sync)
        {
            _report = _report with { BackfillDone = backfillDone, BackfillBefore = backfillBefore };
        }
    }
}

public sealed record AnalyticsPushReport(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt,
    [property: JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: JsonPropertyName("last_http_status")] int? LastHttpStatus,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("consecutive_failures")] int ConsecutiveFailures,
    [property: JsonPropertyName("backfill_done")] bool BackfillDone,
    [property: JsonPropertyName("backfill_before")] string? BackfillBefore,
    [property: JsonPropertyName("days_sent")] int DaysSent);
