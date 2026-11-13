using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Snapshot;

namespace Manajoy.GameClass.Bridge.Tests;

public class SnapshotBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0);
    private static readonly TimeSpan Tashkent = TimeSpan.FromHours(5);

    [Fact]
    public void Computer_without_segments_is_available()
    {
        var snapshot = Build([]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Available, pc.State);
        Assert.Equal(GameClassPcStates.Free, pc.GameClassState);
        Assert.Null(pc.Session);
        Assert.Null(pc.TimeLeftMinutes);
    }

    [Fact]
    public void Active_prepaid_segment_makes_computer_busy_with_time_left()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(52).AddSeconds(30), status: 1)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Busy, pc.State);
        Assert.Equal(GameClassPcStates.InSession, pc.GameClassState);
        Assert.Equal(52, pc.TimeLeftMinutes);
        Assert.NotNull(pc.Session);
        Assert.Equal("Internet", pc.Session!.TariffName);
        Assert.Equal(10m, pc.Session.PrepaidAmount);
        Assert.Null(pc.Session.PostpayLimit);
        Assert.Equal(TimeSpan.FromHours(5), pc.Session.StartedAt.Offset);
        Assert.Equal(Now.AddMinutes(52).AddSeconds(30), pc.Session.EndsAt!.Value.DateTime);
        Assert.Equal(1, pc.ICafeCompat.PcInUsing);
        Assert.Equal(1, pc.ICafeCompat.PcEnabled);
        Assert.Equal("00:52:30", pc.ICafeCompat.StatusConnectTimeLeft);
        Assert.Equal("1", pc.ICafeCompat.PcName);
    }

    [Fact]
    public void Free_computer_maps_to_icafe_available_fields()
    {
        var compat = Build([]).Pcs.Single(p => p.Number == 2).ICafeCompat;
        Assert.Equal(0, compat.PcInUsing);
        Assert.Equal(1, compat.PcEnabled);
        Assert.Null(compat.StatusConnectTimeLeft);
        Assert.Equal("General", compat.PcAreaName);
    }

    [Theory]
    [InlineData(-30, "00:00:00")]
    [InlineData(59, "00:00:59")]
    [InlineData(3723, "01:02:03")]
    [InlineData(90000, "25:00:00")]
    public void Time_left_uses_icafe_format(int seconds, string expected)
    {
        Assert.Equal(expected, SnapshotBuilder.FormatTimeLeft(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Postpay_session_has_no_end_and_no_time_left()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-5), stop: Now.AddYears(1), status: 1, postpay: true)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Busy, pc.State);
        Assert.Null(pc.TimeLeftMinutes);
        Assert.Null(pc.Session!.EndsAt);
        Assert.True(pc.Session.Postpay);
        Assert.Null(pc.Session.PrepaidAmount);
        Assert.Equal(10m, pc.Session.PostpayLimit);
    }

    [Fact]
    public void Future_reservation_keeps_computer_available_and_is_listed()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddHours(1), stop: Now.AddHours(2), status: 0)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Available, pc.State);
        var reservation = Assert.Single(pc.UpcomingReservations);
        Assert.Equal(Now.AddHours(1), reservation.StartsAt.DateTime);
    }

    [Fact]
    public void Started_but_not_activated_reservation_holds_the_computer()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-1), stop: Now.AddHours(1), status: 0)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Busy, pc.State);
        Assert.Equal(GameClassPcStates.Reserved, pc.GameClassState);
    }

    [Fact]
    public void Expired_segment_is_ignored()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddHours(-2), stop: Now.AddMinutes(-1), status: 1)]);

        Assert.Equal(PcStates.Available, snapshot.Pcs.Single(p => p.Number == 1).State);
    }

    [Fact]
    public void Lost_link_without_session_is_offline()
    {
        var snapshot = Build([], uncontrolled: [Heartbeat(Now.AddSeconds(-20)), new UncontrolRow(2, Now.AddMinutes(-3), Now.AddSeconds(-20), false)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 2);
        Assert.Equal(PcStates.Offline, pc.State);
        Assert.Equal(GameClassPcStates.Offline, pc.GameClassState);
        Assert.True(pc.LinkLost);
        Assert.True(snapshot.GameClass.ServerAlive);
        Assert.Equal(1, snapshot.Counts[PcStates.Offline]);
    }

    [Fact]
    public void Lost_link_during_session_stays_busy_but_is_flagged()
    {
        var snapshot = Build(
            [Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(20), status: 1)],
            [Heartbeat(Now.AddSeconds(-5)), new UncontrolRow(1, Now.AddMinutes(-1), Now.AddSeconds(-5), false)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 1);
        Assert.Equal(PcStates.Busy, pc.State);
        Assert.True(pc.LinkLost);
    }

    [Fact]
    public void Link_row_not_extended_at_latest_pulse_is_closed()
    {
        // The client came back: GCServer stopped extending the row, the heartbeat moved on.
        var snapshot = Build([], uncontrolled: [Heartbeat(Now), new UncontrolRow(2, Now.AddMinutes(-3), Now.AddSeconds(-61), false)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 2);
        Assert.Equal(PcStates.Available, pc.State);
        Assert.False(pc.LinkLost);
    }

    [Fact]
    public void Missing_heartbeat_row_reports_unknown_but_keeps_db_facts()
    {
        // GameClass "Clear statistics" deletes the heartbeat row until the next operator logon.
        var snapshot = Build(
            [Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(20), status: 1)],
            [new UncontrolRow(2, Now.AddMinutes(-3), Now, false)]);

        Assert.Null(snapshot.GameClass.ServerAlive);
        Assert.True(snapshot.GameClass.HeartbeatMissing);
        Assert.All(snapshot.Pcs, pc => Assert.Equal(PcStates.Unknown, pc.State));
        Assert.Equal(GameClassPcStates.InSession, snapshot.Pcs.Single(p => p.Number == 1).GameClassState);
        Assert.False(snapshot.Pcs.Single(p => p.Number == 2).LinkLost);
    }

    [Fact]
    public void Finished_session_is_never_active_even_if_its_stop_is_ahead()
    {
        // GCServer's clock can run ahead of SQL; a stopped session may carry a stop time slightly in the future.
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddSeconds(30), status: 2)]);

        Assert.Equal(PcStates.Available, snapshot.Pcs.Single(p => p.Number == 1).State);
    }

    [Fact]
    public void Session_time_cost_covers_all_segments()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(20), status: 1)]);

        Assert.Equal(2.5m, snapshot.Pcs.Single(p => p.Number == 1).Session!.TimeCost);
    }

    [Fact]
    public void Timestamps_carry_the_sql_server_offset()
    {
        var snapshot = Build([]);

        Assert.Equal(Tashkent, snapshot.ServerTime.Offset);
        Assert.Equal(300, snapshot.GameClass.UtcOffsetMinutes);
    }

    [Fact]
    public void State_only_change_to_unknown_is_an_event()
    {
        long seq = 0;
        var alive = Build([]);
        var dead = Build([], [Heartbeat(Now.AddMinutes(-10))]);

        var changes = SnapshotDiff.Changes(alive, dead, () => ++seq);
        Assert.Equal(3, changes.Count);
        Assert.All(changes, c => Assert.Equal(PcStates.Unknown, c.ToState));
    }

    [Fact]
    public void Club_with_heartbeat_switched_off_still_reports_session_states()
    {
        var data = new GameClassData(
            Now,
            Tashkent,
            new ClubSettings("My Club", "$", HeartbeatEnabled: false),
            null,
            [new ComputerRow(1, 1, "10.0.0.1", 1, false, 0, false)],
            [new ComputerGroupRow(1, "General")],
            [new TariffRow(1, "Internet", true, 1, 0.5m, 1, 1)],
            [],
            [Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(20), status: 1)],
            []);
        var snapshot = SnapshotBuilder.Build(data, new DateTimeOffset(Now, Tashkent), "test");

        Assert.True(snapshot.GameClass.HeartbeatDisabled);
        Assert.False(snapshot.GameClass.HeartbeatMissing);
        Assert.Null(snapshot.GameClass.ServerAlive);
        Assert.Equal(PcStates.Busy, snapshot.Pcs.Single().State);
    }

    [Fact]
    public void Stale_heartbeat_marks_states_unknown()
    {
        var snapshot = Build([], [Heartbeat(Now.AddMinutes(-6))]);

        Assert.False(snapshot.GameClass.ServerAlive);
        Assert.Equal(360, snapshot.GameClass.HeartbeatAgeSeconds);
        Assert.All(snapshot.Pcs, pc => Assert.Equal(PcStates.Unknown, pc.State));
    }

    [Fact]
    public void Paused_heartbeat_within_five_minutes_is_still_alive()
    {
        var snapshot = Build([], [Heartbeat(Now.AddMinutes(-2))]);

        Assert.True(snapshot.GameClass.ServerAlive);
        Assert.Equal(PcStates.Available, snapshot.Pcs.Single(p => p.Number == 1).State);
    }

    [Fact]
    public void Maintenance_session_disables_the_computer()
    {
        var snapshot = Build([Segment(computerId: 3, start: Now.AddMinutes(-1), stop: Now.AddHours(6), status: 1, tariffId: 0)]);

        var pc = snapshot.Pcs.Single(p => p.Number == 3);
        Assert.Equal(PcStates.Disabled, pc.State);
        Assert.Equal(GameClassPcStates.Maintenance, pc.GameClassState);
        Assert.Equal(1, snapshot.Counts[PcStates.Disabled]);
    }

    [Fact]
    public void Counts_and_zones_summarise_states()
    {
        var snapshot = Build([Segment(computerId: 1, start: Now.AddMinutes(-10), stop: Now.AddMinutes(20), status: 1)]);

        Assert.Equal(1, snapshot.Counts[PcStates.Busy]);
        Assert.Equal(2, snapshot.Counts[PcStates.Available]);
        Assert.Equal(3, snapshot.Counts["total"]);
        var general = snapshot.Zones["1"];
        Assert.Equal("General", general.Name);
        Assert.Equal(1, general.Busy);
        Assert.Equal(1, general.Available);
        Assert.Equal(20, general.NextFreeInMinutes);
        Assert.Equal(1, snapshot.Zones["2"].Available);
    }

    [Fact]
    public void Tariff_variants_are_attached_with_time_of_day()
    {
        var snapshot = Build([]);

        var tariff = Assert.Single(snapshot.Tariffs);
        var variant = Assert.Single(tariff.Variants);
        Assert.Equal("23:00", variant.From);
        Assert.Equal("07:00", variant.To);
    }

    [Fact]
    public void Diff_reports_transitions_but_not_the_baseline()
    {
        long seq = 0;
        var free = Build([]);
        var busy = Build([Segment(computerId: 1, start: Now.AddMinutes(-1), stop: Now.AddMinutes(30), status: 1)]);

        Assert.Empty(SnapshotDiff.Changes(null, free, () => ++seq));

        var started = SnapshotDiff.Changes(free, busy, () => ++seq);
        var change = Assert.Single(started);
        Assert.Equal(1, change.PcNumber);
        Assert.Equal(GameClassPcStates.Free, change.FromGameClassState);
        Assert.Equal(GameClassPcStates.InSession, change.ToGameClassState);
        Assert.Equal(7, change.SessionId);

        var stopped = Assert.Single(SnapshotDiff.Changes(busy, free, () => ++seq));
        Assert.Equal(PcStates.Available, stopped.ToState);
        Assert.Equal(7, stopped.SessionId);
        Assert.Empty(SnapshotDiff.Changes(free, free, () => ++seq));
    }

    private static ClubSnapshot Build(IReadOnlyList<SessionSegmentRow> segments, IReadOnlyList<UncontrolRow>? uncontrolled = null)
    {
        var rows = uncontrolled ?? [Heartbeat(Now.AddSeconds(-3))];
        var data = new GameClassData(
            Now,
            Tashkent,
            new ClubSettings("My Club", "$"),
            rows.Where(u => u.IsClubHeartbeat).Select(u => (DateTime?)u.Stop).Max(),
            [
                new ComputerRow(1, 1, "192.168.122.158", 1, false, 0, false),
                new ComputerRow(2, 2, "192.168.0.2", 1, false, 0, false),
                new ComputerRow(3, 3, "192.168.0.3", 2, true, 0, false),
            ],
            [new ComputerGroupRow(1, "General"), new ComputerGroupRow(2, "VIP")],
            [new TariffRow(1, "Internet", true, 1, 0.5m, 1, 1)],
            [new TariffVariantRow(2, 1, "Ночной", new DateTime(1900, 1, 1, 23, 0, 0), new DateTime(1900, 1, 1, 7, 0, 0), 8m, false, "1234567")],
            segments,
            rows);
        return SnapshotBuilder.Build(data, new DateTimeOffset(Now, Tashkent), "test");
    }

    private static UncontrolRow Heartbeat(DateTime at) => new(26, at, at, true);

    private static SessionSegmentRow Segment(int computerId, DateTime start, DateTime stop, int status, bool postpay = false, int tariffId = 1) =>
        new(7, 70, computerId, 0, start, start, stop, tariffId, false, postpay, 5, status, 10m, 0m, 0m, 0m, 0, 0d, 0.5m, 2.5m);
}
