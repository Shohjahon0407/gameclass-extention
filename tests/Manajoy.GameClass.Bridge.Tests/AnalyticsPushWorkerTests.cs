using System.Net;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Analytics;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Tests;

public sealed class AnalyticsPushWorkerTests : IDisposable
{
    private const string PushUrl = "https://api.example.test/integrations/gameclass/26/snapshot";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mj-reports-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Url_is_a_sibling_of_the_snapshot_url()
    {
        Assert.Equal("https://api.example.test/integrations/gameclass/26/analytics", AnalyticsPushWorker.UrlFromPushUrl(PushUrl));
        Assert.Null(AnalyticsPushWorker.UrlFromPushUrl("https://api.example.test/other"));
    }

    [Fact]
    public async Task Recent_week_goes_first_then_history_newest_first_until_the_first_record()
    {
        var (worker, handler, status) = Create(historyDays: 30);

        var delay = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Range(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 26)), Dates(handler.Requests[0]));
        Assert.Equal(Range(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 19)), Dates(handler.Requests[1]));

        handler.Requests.Clear();
        delay = await worker.RunOnceAsync(CancellationToken.None);
        // The last 3 days did not change; the rest of the history (from business day 09-26) ends the backfill.
        Assert.Equal(TimeSpan.FromMinutes(10), delay);
        Assert.True(status.Current.BackfillDone);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(Range(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 5)), Dates(request));
    }

    [Fact]
    public async Task Days_that_did_not_change_are_not_sent_again()
    {
        var (worker, handler, _) = Create(historyDays: 3);
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        handler.Requests.Clear();

        await worker.RunOnceAsync(CancellationToken.None);

        // The demo club's "now" is fixed, so nothing changed: no request at all.
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Turned_off_in_manajoy_waits_six_hours_and_keeps_nothing_as_sent()
    {
        var (worker, handler, status) = Create(historyDays: 3, reply: HttpStatusCode.Conflict);

        var delay = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(6), delay);
        Assert.Single(handler.Requests);
        Assert.Equal(409, status.Current.LastHttpStatus);
        Assert.False(File.Exists(Path.Combine(_dir, "sync.json")) && File.ReadAllText(Path.Combine(_dir, "sync.json")).Contains("2026-10-26"));
    }

    [Fact]
    public async Task Payload_carries_the_key_the_day_start_and_no_customer_data()
    {
        var (worker, handler, _) = Create(historyDays: 30);
        await worker.RunOnceAsync(CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal("https://api.example.test/integrations/gameclass/26/analytics", request.Url);
        Assert.Equal("club-key", request.Key);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal(1, json.RootElement.GetProperty("v").GetInt32());
        Assert.Equal(6, json.RootElement.GetProperty("day_start_hour").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("utc_offset_minutes", out _));
        var day = json.RootElement.GetProperty("days")[0];
        Assert.True(day.GetProperty("money").GetProperty("kassa").GetDecimal() > 0);
        Assert.Equal(24, day.GetProperty("hourly_busy_minutes").GetArrayLength());
        Assert.DoesNotContain("phone", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_new_day_start_hour_sends_everything_again()
    {
        var hour = 6;
        var (worker, handler, _) = Create(historyDays: 3, dayStart: () => hour);
        await worker.RunOnceAsync(CancellationToken.None);
        await worker.RunOnceAsync(CancellationToken.None);
        handler.Requests.Clear();

        hour = 5;
        await worker.RunOnceAsync(CancellationToken.None);

        Assert.NotEmpty(handler.Requests);
        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(5, json.RootElement.GetProperty("day_start_hour").GetInt32());
        Assert.Equal(7, json.RootElement.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task Bar_stock_is_sent_when_it_changes_and_each_book_line_once()
    {
        var options = Options.Create(new BridgeOptions { Push = new PushOptions { Url = PushUrl, Key = "club-key" } });
        var now = new DateTime(2026, 10, 26, 22, 0, 0);
        var source = new DemoAnalyticsSource(["Standard", "VIP"], () => now);
        var store = new Bar.BarStore(null);
        store.Add(1, "Coca-Cola 0.5", Bar.BarEntryKind.Count, 100, null, now.AddDays(-3));
        var handler = new FakeHandler(HttpStatusCode.OK);
        var worker = new AnalyticsPushWorker(() => source, () => 6, new AnalyticsPushStatus(options), new FakeFactory(handler), options,
            new FixedClock(new DateTimeOffset(now, TimeSpan.Zero)), NullLogger<AnalyticsPushWorker>.Instance,
            Path.Combine(_dir, "sync.json"), () => store.Load());

        await worker.RunOnceAsync(CancellationToken.None);
        var bar = Assert.Single(handler.Requests, r => r.Url.EndsWith("/bar", StringComparison.Ordinal));
        Assert.Equal("https://api.example.test/integrations/gameclass/26/bar", bar.Url);
        using (var json = JsonDocument.Parse(bar.Body))
        {
            var coke = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("item_id").GetInt32() == 1);
            Assert.True(coke.GetProperty("tracked").GetBoolean());
            Assert.True(coke.GetProperty("remaining").GetInt32() < 100);
            Assert.Equal(1, json.RootElement.GetProperty("entries").GetArrayLength());
        }

        handler.Requests.Clear();
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(handler.Requests, r => r.Url.EndsWith("/bar", StringComparison.Ordinal));

        store.Add(1, "Coca-Cola 0.5", Bar.BarEntryKind.Receive, 24, null, now);
        await worker.RunOnceAsync(CancellationToken.None);
        var next = Assert.Single(handler.Requests, r => r.Url.EndsWith("/bar", StringComparison.Ordinal));
        using var again = JsonDocument.Parse(next.Body);
        Assert.Equal("receive", Assert.Single(again.RootElement.GetProperty("entries").EnumerateArray()).GetProperty("kind").GetString());
    }

    private (AnalyticsPushWorker, FakeHandler, AnalyticsPushStatus) Create(int historyDays, HttpStatusCode reply = HttpStatusCode.OK,
        Func<int>? dayStart = null)
    {
        var options = Options.Create(new BridgeOptions { Push = new PushOptions { Url = PushUrl, Key = "club-key" } });
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero));
        var source = new LimitedSource(new DemoAnalyticsSource(["Standard", "Standard", "VIP"], () => new DateTime(2026, 10, 26, 22, 0, 0)),
            new DateTime(2026, 10, 26, 22, 0, 0).AddDays(-historyDays));
        var handler = new FakeHandler(reply);
        var status = new AnalyticsPushStatus(options);
        var worker = new AnalyticsPushWorker(() => source, dayStart ?? (() => 6), status, new FakeFactory(handler), options, clock,
            NullLogger<AnalyticsPushWorker>.Instance, Path.Combine(_dir, "sync.json"));
        return (worker, handler, status);
    }

    private static List<DateOnly> Dates(Captured request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("days").EnumerateArray()
            .Select(d => DateOnly.Parse(d.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    private static List<DateOnly> Range(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    /// <summary>The demo club, as if GameClass statistics started <c>historyFrom</c>.</summary>
    private sealed class LimitedSource(IAnalyticsSource inner, DateTime historyFrom) : IAnalyticsSource
    {
        public async Task<AnalyticsData> LoadAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            var data = await inner.LoadAsync(from, to, cancellationToken);
            return data with
            {
                HistoryFrom = historyFrom,
                Segments = data.Segments.Where(s => s.Start >= historyFrom).ToList(),
                MoneyEvents = data.MoneyEvents.Where(m => m.Moment >= historyFrom).ToList(),
                BarSales = data.BarSales.Where(b => b.Moment >= historyFrom).ToList(),
            };
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed record Captured(string Url, string? Key, string Body);

    private sealed class FakeHandler(HttpStatusCode reply) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Captured(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues(PushWorker.KeyHeader, out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(reply);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
