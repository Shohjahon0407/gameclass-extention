using System.Net;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Hosting;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Tests;

public class PushWorkerTests
{
    private const string Url = "https://api.example.test/integrations/gameclass/26/snapshot";
    private static readonly DateTime DbNow = new(2026, 10, 5, 12, 0, 0);

    [Fact]
    public async Task First_snapshot_is_pushed_with_the_bridge_key()
    {
        var (worker, handler, store, _, status) = Create();
        store.Publish(Snapshot(busy: false, at: 0), "Odbc");

        await worker.PushIfDueAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(Url, request.Url);
        Assert.Equal("club-key", request.Key);
        Assert.Contains("\"provider\":\"gameclass\"", request.Body);
        Assert.NotNull(status.Current.LastSuccessAt);
    }

    [Fact]
    public async Task Only_pc_states_leave_the_club()
    {
        var (worker, handler, store, _, _) = Create();
        store.Publish(Snapshot(busy: true, at: 0), "Odbc");

        await worker.PushIfDueAsync(CancellationToken.None);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"icafe_compat\"", body);
        Assert.Contains("\"state\":\"busy\"", body);
        foreach (var local in new[] { "\"session\"", "\"prepaid_amount\"", "\"tariffs\"", "\"ip\"", "\"club\"", "\"upcoming_reservations\"" })
        {
            Assert.DoesNotContain(local, body);
        }
    }

    [Fact]
    public async Task Same_snapshot_is_not_pushed_twice_and_quiet_periods_wait_for_the_interval()
    {
        var (worker, handler, store, clock, _) = Create();
        store.Publish(Snapshot(busy: false, at: 0), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(5));
        store.Publish(Snapshot(busy: false, at: 5), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Single(handler.Requests); // nothing changed, interval (15 s) not reached

        clock.Advance(TimeSpan.FromSeconds(11));
        store.Publish(Snapshot(busy: false, at: 16), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_status_change_is_pushed_immediately()
    {
        var (worker, handler, store, clock, _) = Create();
        store.Publish(Snapshot(busy: false, at: 0), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(5));
        store.Publish(Snapshot(busy: true, at: 5), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("\"pc_in_using\":1", handler.Requests[1].Body);
    }

    [Fact]
    public async Task Rejected_key_backs_off_for_minutes()
    {
        var (worker, handler, store, clock, status) = Create(HttpStatusCode.Unauthorized);
        store.Publish(Snapshot(busy: false, at: 0), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Equal(401, status.Current.LastHttpStatus);
        Assert.Equal("rejected: bridge key is wrong", status.Current.LastError);

        clock.Advance(TimeSpan.FromSeconds(60));
        store.Publish(Snapshot(busy: true, at: 60), "Odbc");
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromMinutes(5));
        await worker.PushIfDueAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("http://api.manajoy.uz/x", false)]
    [InlineData("https://api.manajoy.uz/x", true)]
    [InlineData("http://192.168.122.1:8000/x", true)]
    [InlineData("http://127.0.0.1:8000/x", true)]
    [InlineData("http://8.8.8.8/x", false)]
    public void Push_url_must_be_https_outside_the_lan(string url, bool valid)
    {
        var options = new BridgeOptions { Push = new PushOptions { Url = url, Key = "k" } };
        Assert.Equal(valid, new BridgeOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Push_needs_a_key()
    {
        var options = new BridgeOptions { Push = new PushOptions { Url = Url } };
        Assert.True(new BridgeOptionsValidator().Validate(null, options).Failed);
    }

    private static (PushWorker, FakeHandler, SnapshotStore, ManualClock, PushStatus) Create(HttpStatusCode reply = HttpStatusCode.OK)
    {
        var options = Options.Create(new BridgeOptions { Push = new PushOptions { Url = Url, Key = "club-key", IntervalSeconds = 15 } });
        var clock = new ManualClock();
        var handler = new FakeHandler(reply);
        var store = new SnapshotStore(options, clock);
        var status = new PushStatus(options);
        var worker = new PushWorker(store, status, new FakeFactory(handler), options, clock, NullLogger<PushWorker>.Instance);
        return (worker, handler, store, clock, status);
    }

    private static ClubSnapshot Snapshot(bool busy, int at)
    {
        var now = DbNow.AddSeconds(at);
        var data = new GameClassData(
            now,
            TimeSpan.FromHours(5),
            new ClubSettings("Club", "UZS"),
            now,
            [new ComputerRow(1, 1, "10.0.0.1", 1, false, 0, false)],
            [new ComputerGroupRow(1, "General")],
            [new TariffRow(1, "Internet", true, 1, 0.5m, 1, 1)],
            [],
            busy ? [new SessionSegmentRow(9, 90, 1, 0, now.AddMinutes(-1), now.AddMinutes(-1), now.AddMinutes(30), 1, false, false, 5, 1, 10m, 0m, 0m, 0m, 0, 0d, 0m, 0m)] : [],
            []);
        return SnapshotBuilder.Build(data, new DateTimeOffset(now, TimeSpan.FromHours(5)), "test");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 7, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
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
