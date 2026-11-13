using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Hosting;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Tests;

public class StoreAndCompatTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0);
    private static readonly TimeSpan Tz = TimeSpan.FromHours(5);

    [Fact]
    public void Unknown_state_leaves_icafe_fields_empty_so_backend_reports_unknown()
    {
        var pc = Snapshot(busy: true, heartbeat: false).Pcs.Single(p => p.Number == 1);

        Assert.Equal(PcStates.Unknown, pc.State);
        Assert.Null(pc.ICafeCompat.PcEnabled);
        Assert.Null(pc.ICafeCompat.PcInUsing);
        Assert.Null(pc.ICafeCompat.StatusConnectTimeLeft);
        Assert.Null(pc.TimeLeftMinutes);
    }

    [Fact]
    public void Store_flags_a_gap_for_a_cursor_from_a_previous_run()
    {
        var store = new SnapshotStore(Options.Create(new BridgeOptions()), TimeProvider.System);
        store.Publish(Snapshot(busy: false, heartbeat: true), "Odbc");
        store.Publish(Snapshot(busy: true, heartbeat: true), "Odbc");

        var fresh = store.EventsSince(0, 100);
        var change = Assert.Single(fresh.Events);
        Assert.False(fresh.Gap);
        // Sequences are clock-seeded, so they are far above any small cursor kept from an older run.
        Assert.True(change.Sequence > 1_000_000);

        var stale = store.EventsSince(42, 100);
        Assert.True(stale.Gap);

        var upToDate = store.EventsSince(change.Sequence, 100);
        Assert.Empty(upToDate.Events);
        Assert.False(upToDate.Gap);
    }

    [Fact]
    public void Store_reports_stale_after_missed_polls()
    {
        var store = new SnapshotStore(Options.Create(new BridgeOptions()), TimeProvider.System);
        var snapshot = Snapshot(busy: false, heartbeat: true);
        store.Publish(snapshot, "Odbc");

        Assert.Equal("ok", store.Health(snapshot.FetchedAt.AddSeconds(10), 5).Status);
        Assert.Equal("stale", store.Health(snapshot.FetchedAt.AddSeconds(31), 5).Status);
    }

    [Fact]
    public void Store_reports_degraded_when_gcserver_is_not_live()
    {
        var store = new SnapshotStore(Options.Create(new BridgeOptions()), TimeProvider.System);
        var snapshot = Snapshot(busy: false, heartbeat: false);
        store.Publish(snapshot, "Odbc");

        Assert.Equal("degraded", store.Health(snapshot.FetchedAt.AddSeconds(1), 5).Status);
    }

    private static ClubSnapshot Snapshot(bool busy, bool heartbeat)
    {
        var data = new GameClassData(
            Now,
            Tz,
            new ClubSettings("Club", "UZS"),
            heartbeat ? Now : Now.AddHours(-1),
            [new ComputerRow(1, 1, "10.0.0.1", 1, false, 0, false)],
            [new ComputerGroupRow(1, "General")],
            [new TariffRow(1, "Internet", true, 1, 0.5m, 1, 1)],
            [],
            busy ? [new SessionSegmentRow(9, 90, 1, 0, Now.AddMinutes(-5), Now.AddMinutes(-5), Now.AddMinutes(30), 1, false, false, 5, 1, 10m, 0m, 0m, 0m, 0, 0d, 0m, 0m)] : [],
            heartbeat ? [new UncontrolRow(26, Now, Now, true)] : []);
        return SnapshotBuilder.Build(data, new DateTimeOffset(Now, Tz), "test");
    }
}
