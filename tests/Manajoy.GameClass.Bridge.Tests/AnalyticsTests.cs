using Manajoy.GameClass.Bridge.Analytics;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

public class AnalyticsBuilderTests
{
    // 2026-10-26 was a Monday.
    private static readonly DateOnly Day = new(2026, 10, 26);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    private static readonly AnalyticsComputer[] TwoPcs = [new(1, 1, "Standard"), new(2, 2, "VIP")];

    private static SegmentRow Segment(int id, int pc, DateTime start, DateTime stop, decimal cost = 0, int status = 2,
        bool postpay = false, bool account = false, DateTime? paidAt = null, int tariff = 1, bool debt = false) =>
        new(id, id * 10, pc, tariff, start, stop, cost, 0m, status, postpay, debt, cost, 0m, 0m, 1, account, paidAt ?? start);

    private static AnalyticsData Data(DateTime now, IEnumerable<SegmentRow>? segments = null, IEnumerable<MoneyEventRow>? money = null,
        IEnumerable<BarSaleRow>? bar = null, IEnumerable<AccountMoneyRow>? accounts = null, IEnumerable<AnalyticsComputer>? pcs = null,
        DateTime? historyFrom = null) =>
        new(now, "so'm", historyFrom, (pcs ?? TwoPcs).ToList(), new Dictionary<int, string> { [1] = "Standart" },
            new Dictionary<int, string> { [1] = "Admin" }, (segments ?? []).ToList(), (money ?? []).ToList(), (bar ?? []).ToList(),
            (accounts ?? []).ToList(), []);

    private static AnalyticsQuery OneDay(int dayStart = 6) => new(Day, Day, "auto", dayStart);

    [Fact]
    public void Business_day_runs_from_six_to_six_and_night_sessions_belong_to_the_evening_before()
    {
        var (from, to) = AnalyticsBuilder.Window(OneDay());
        Assert.Equal(At(26, 6), from);
        Assert.Equal(At(27, 6), to);

        // 02:00–04:00 on the 27th is still Monday's business day; 05:00–07:00 on the 26th is half Sunday's.
        var data = Data(At(28, 12), [Segment(1, 1, At(27, 2), At(27, 4)), Segment(2, 2, At(26, 5), At(26, 7))]);
        var report = AnalyticsBuilder.Build(OneDay(), data);

        Assert.Equal(3.0, report.Usage.BusyHours);
        Assert.Equal(1, report.Sessions.Count);
        Assert.Equal(Math.Round(3.0 / 48, 4), report.Usage.Occupancy);
    }

    [Fact]
    public void A_running_session_counts_only_until_now()
    {
        var data = Data(At(26, 12), [Segment(1, 1, At(26, 10), At(26, 15), status: 1)]);
        var report = AnalyticsBuilder.Build(OneDay(), data);

        Assert.Equal(2.0, report.Usage.BusyHours);
        // Occupancy is measured over the hours that have passed (06:00–12:00, two PCs).
        Assert.Equal(Math.Round(2.0 / 12, 4), report.Usage.Occupancy);
    }

    [Fact]
    public void Peak_counts_a_pc_handed_from_one_session_to_the_next_once()
    {
        var data = Data(At(28, 0), [
            Segment(1, 1, At(26, 18), At(26, 20)),
            Segment(2, 1, At(26, 20), At(26, 22)),
            Segment(3, 2, At(26, 19), At(26, 21)),
        ]);
        var report = AnalyticsBuilder.Build(OneDay(), data);

        Assert.Equal(2, report.Usage.PeakBusy);
        Assert.Equal(At(26, 19), report.Usage.PeakAt);
    }

    [Fact]
    public void Reservations_and_cancelled_sessions_are_not_busy_time()
    {
        var data = Data(At(26, 12), [
            Segment(1, 1, At(26, 18), At(26, 20), status: 0),
            Segment(2, 2, At(26, 9), At(26, 9), status: 2),
        ]);
        var report = AnalyticsBuilder.Build(OneDay(), data);

        Assert.Equal(0, report.Usage.BusyHours);
        Assert.Equal(1, report.Bookings.Upcoming);
        Assert.Equal(1, report.Bookings.Cancelled);
        Assert.Equal(0, report.Sessions.Count);
    }

    [Fact]
    public void Paid_well_before_the_start_is_a_past_booking()
    {
        var data = Data(At(28, 0), [
            Segment(1, 1, At(26, 18), At(26, 20), paidAt: At(26, 16)),
            Segment(2, 2, At(26, 18), At(26, 20), paidAt: At(26, 17, 58)),
        ]);
        Assert.Equal(1, AnalyticsBuilder.Build(OneDay(), data).Bookings.Past);
    }

    [Fact]
    public void Bar_sold_through_a_session_is_moved_out_of_pc_time_and_counted_once()
    {
        // A guest prepaid 20 000; 5 000 of it went on coffee through the session. 47 000 bar paid separately.
        var data = Data(At(28, 0),
            [Segment(1, 1, At(26, 10), At(26, 12), cost: 15000)],
            money: [new MoneyEventRow(1, At(26, 10), 15000, 0, false, false), new MoneyEventRow(1, At(26, 11), 5000, 2, false, false)],
            bar: [new BarSaleRow(3, "Qahva", 1, 5000, 2, At(26, 11), 1, false, 1), new BarSaleRow(2, "Flash", 4, 11750, 1, At(26, 13), 0, false, 1)]);
        var money = AnalyticsBuilder.Build(OneDay(), data).Money;

        Assert.Equal(15000, money.PcTime);
        Assert.Equal(52000, money.Bar);
        Assert.Equal(47000, money.BarCash);
        Assert.Equal(5000, money.BarInSessions);
        Assert.Equal(67000, money.Kassa);
        Assert.Equal(money.Kassa, money.PcTime + money.Bar);
    }

    /// <summary>
    /// GC-SERVER lab, 2026-10-26 (gameclass-installer/LAB-STATUS.md «Bar»): GameClass closed the shift at 11:40:28 with
    /// Computers 17 226 + Services 47 000 = 64 226. Rows as read from the lab database; 225 = the guest money of the
    /// earlier lab days (3–19 Oct) in one row. Session 41 bought coffee (TypeCost 2), session 42 a Flash (TypeCost 3).
    /// </summary>
    [Fact]
    public void Lab_shift_close_matches_gameclass()
    {
        var query = new AnalyticsQuery(new DateOnly(2026, 10, 3), Day, "auto", 6);
        var data = Data(new DateTime(2026, 10, 26, 11, 40, 28),
            [Segment(41, 1, At(26, 11, 38), At(26, 11, 38), cost: 0.5m), Segment(42, 3, At(26, 11, 39), At(26, 11, 39), cost: 0.5m)],
            money: [
                new MoneyEventRow(1, At(16, 12), 225, 0, false, false),
                new MoneyEventRow(41, At(26, 11, 38), 20, 0, false, false),
                new MoneyEventRow(41, At(26, 11, 38), 4980.5m, 2, false, false),
                new MoneyEventRow(42, At(26, 11, 39), 12000.5m, 0, false, false),
            ],
            bar: [
                new BarSaleRow(1, "Coca-Cola", 2, 8000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(5, "Lays", 1, 10000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(3, "Suv", 3, 4000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(6, "Snickers", 1, 9000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(7, "Qahva", 1, 5000, 2, At(26, 11, 38), 1, false, 1),
                new BarSaleRow(4, "Flash", 1, 12000, 3, At(26, 11, 39), 3, false, 1),
            ]);
        var money = AnalyticsBuilder.Build(query, data).Money;

        Assert.Equal(64226, money.Kassa);
        Assert.Equal(47000, money.BarCash);
        Assert.Equal(17000, money.BarInSessions);
        Assert.Equal(226, money.PcTime);
        Assert.Equal(64000, money.Bar);
    }

    [Fact]
    public void Account_money_counts_at_the_counter_and_account_sessions_do_not_count_twice()
    {
        var data = Data(At(28, 0),
            [Segment(1, 1, At(26, 10), At(26, 12), cost: 20000, account: true)],
            money: [new MoneyEventRow(1, At(26, 10), 20000, 0, false, true)],
            accounts: [new AccountMoneyRow(0, 50000, At(26, 9), 1), new AccountMoneyRow(1, 10000, At(26, 20), 1), new AccountMoneyRow(2, 20000, At(26, 10), 0)]);
        var money = AnalyticsBuilder.Build(OneDay(), data).Money;

        Assert.Equal(0, money.PcTime);
        Assert.Equal(50000, money.AccountsAdded);
        Assert.Equal(10000, money.AccountsReturned);
        Assert.Equal(40000, money.Kassa);
    }

    [Fact]
    public void Previous_period_is_the_same_length_right_before()
    {
        var query = new AnalyticsQuery(new DateOnly(2026, 10, 20), Day, "auto", 6);
        Assert.Equal((At(13, 6), At(27, 6)), AnalyticsBuilder.LoadRange(query));

        var data = Data(At(28, 0), money: [
            new MoneyEventRow(1, At(15, 12), 30000, 0, false, false),
            new MoneyEventRow(2, At(21, 12), 45000, 0, false, false),
        ]);
        var report = AnalyticsBuilder.Build(query, data);
        Assert.Equal(45000, report.Money.Kassa);
        Assert.Equal(30000, report.Previous.Kassa);
    }

    [Theory]
    [InlineData(1, "auto", "hour", 24)]
    [InlineData(7, "auto", "day", 7)]
    [InlineData(7, "hour", "day", 7)]
    [InlineData(7, "week", "week", 2)]
    [InlineData(300, "auto", "month", 11)]
    public void Series_buckets_follow_the_business_day(int days, string group, string expected, int buckets)
    {
        var query = new AnalyticsQuery(Day.AddDays(1 - days), Day, group, 6);
        var report = AnalyticsBuilder.Build(query, Data(At(28, 0)));

        Assert.Equal(expected, report.Group);
        Assert.Equal(buckets, report.Series.Count);
        Assert.Equal(AnalyticsBuilder.Window(query).From, report.Series[0].Start);
    }

    [Fact]
    public void Daily_series_puts_night_money_on_the_evening_before()
    {
        var query = new AnalyticsQuery(new DateOnly(2026, 10, 25), Day, "day", 6);
        var data = Data(At(28, 0), money: [new MoneyEventRow(1, At(26, 3), 10000, 0, false, false)]);
        var series = AnalyticsBuilder.Build(query, data).Series;

        Assert.Equal("2026-10-25", series[0].Key);
        Assert.Equal(10000, series[0].Kassa);
        Assert.Equal(0, series[1].Kassa);
    }

    [Fact]
    public void Hours_before_the_first_record_do_not_dilute_occupancy()
    {
        var data = Data(At(28, 0), [Segment(1, 1, At(26, 18), At(27, 0))], historyFrom: At(26, 18));
        var report = AnalyticsBuilder.Build(OneDay(), data);

        // 18:00–06:00 = 12 h of history, two PCs; PC 1 busy 6 h.
        Assert.Equal(0.25, report.Usage.Occupancy);
    }

    [Fact]
    public void Deleted_computers_keep_their_history_but_not_capacity()
    {
        var pcs = new[] { new AnalyticsComputer(1, 1, "Standard"), new AnalyticsComputer(9, -9, "Old") };
        var data = Data(At(28, 0), [Segment(1, 9, At(26, 10), At(26, 12))], pcs: pcs);
        var report = AnalyticsBuilder.Build(OneDay(), data);

        Assert.Equal(1, report.Usage.PcCount);
        Assert.Contains(report.Pcs, p => p.Number == 9 && p.BusyHours == 2);
        Assert.DoesNotContain(report.Zones, z => z.Zone == "Old");
    }

    [Fact]
    public async Task Daily_rollups_add_up_to_the_period_report()
    {
        var zones = Enumerable.Range(0, 12).Select(i => i < 3 ? "VIP" : "Standard").ToList();
        var source = new DemoAnalyticsSource(zones, () => At(26, 22));
        var query = new AnalyticsQuery(new DateOnly(2026, 10, 19), Day, "day", 6);
        var (from, to) = AnalyticsBuilder.Window(query);
        var data = await source.LoadAsync(from.AddDays(-7), to, CancellationToken.None);

        var report = AnalyticsBuilder.Build(query, data);
        var days = Enumerable.Range(0, 8).Select(i => AnalyticsBuilder.Rollup(query.From.AddDays(i), 6, data)).ToList();

        Assert.Equal(report.Money.Kassa, days.Sum(d => d.Money.Kassa));
        Assert.Equal(report.Money.Bar, days.Sum(d => d.Money.Bar));
        Assert.Equal(report.Sessions.Count, days.Sum(d => d.Sessions.Count));
        Assert.Equal(report.Usage.BusyHours, Math.Round(days.Sum(d => d.Usage.BusyMinutes) / 60, 2), 1);
        Assert.Equal(report.Usage.Occupancy, days.Sum(d => d.Usage.BusyMinutes) / days.Sum(d => d.Usage.CapacityMinutes), 3);
        Assert.Equal(report.Usage.PeakBusy, days.Max(d => d.Usage.PeakBusy));
        Assert.All(days, d => Assert.Equal(d.Usage.BusyMinutes, d.HourlyBusyMinutes.Sum(), 1));
        Assert.All(days, d => Assert.Equal(d.Usage.BusyMinutes, d.Pcs.Sum(p => p.BusyMinutes), 1));
        Assert.False(days[^1].Complete);
        Assert.True(days[0].Complete);
    }

    [Fact]
    public void Rollup_fingerprint_changes_only_with_the_numbers()
    {
        var segments = new[] { Segment(1, 1, At(26, 10), At(26, 12), cost: 15000) };
        var money = new[] { new MoneyEventRow(1, At(26, 10), 15000, 0, false, false) };
        var a = AnalyticsBuilder.Rollup(Day, 6, Data(At(28, 0), segments, money));
        var b = AnalyticsBuilder.Rollup(Day, 6, Data(At(28, 1), segments, money));
        var c = AnalyticsBuilder.Rollup(Day, 6, Data(At(28, 0), segments, [new MoneyEventRow(1, At(26, 10), 16000, 0, false, false)]));

        Assert.Equal(32, a.Fingerprint.Length);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.Fingerprint, c.Fingerprint);
        Assert.Equal(120, a.Usage.BusyMinutes);
        Assert.Equal(60, a.HourlyBusyMinutes[10]);
        Assert.Equal(15000, a.Sessions.PaidSum);
        Assert.Single(a.Timeline);
    }

    [Fact]
    public void Lab_day_rollup_matches_the_shift_close()
    {
        var data = Data(new DateTime(2026, 10, 26, 11, 40, 28), money: [
                new MoneyEventRow(41, At(26, 11, 38), 20, 0, false, false),
                new MoneyEventRow(41, At(26, 11, 38), 4980.5m, 2, false, false),
                new MoneyEventRow(42, At(26, 11, 39), 12000.5m, 0, false, false),
            ],
            bar: [
                new BarSaleRow(1, "Coca-Cola", 2, 8000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(5, "Lays", 1, 10000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(3, "Suv", 3, 4000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(6, "Snickers", 1, 9000, 1, At(26, 11, 37), 0, false, 1),
                new BarSaleRow(7, "Qahva", 1, 5000, 2, At(26, 11, 38), 1, false, 1),
                new BarSaleRow(4, "Flash", 1, 12000, 3, At(26, 11, 39), 3, false, 1),
            ]);
        var rollup = AnalyticsBuilder.Rollup(Day, 6, data);

        // 64 226 − 225 (the earlier lab days, not part of this business day).
        Assert.Equal(64001, rollup.Money.Kassa);
        Assert.Equal(47000, rollup.Money.BarCash);
        Assert.Equal(6, rollup.Bar.Count);
    }

    [Fact]
    public void Rollup_json_is_snake_case_and_has_no_customer_fields()
    {
        var rollup = AnalyticsBuilder.Rollup(Day, 6, Data(At(28, 0), [Segment(1, 1, At(26, 10), At(26, 12), cost: 15000, account: true)]));
        var json = System.Text.Json.JsonSerializer.Serialize(rollup, Hosting.BridgeJson.SnakeCase);

        Assert.Contains("\"busy_minutes\":", json);
        Assert.Contains("\"hourly_busy_minutes\":", json);
        Assert.Contains("\"pc_time\":", json);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("login", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Day_timeline_lists_every_pc_with_its_blocks()
    {
        var data = Data(At(26, 12), [
            Segment(1, 1, At(26, 10), At(26, 11, 30), cost: 15000),
            Segment(2, 1, At(26, 11, 45), At(26, 14), status: 1),
            Segment(3, 2, At(26, 20), At(26, 22), status: 0),
        ]);
        var day = AnalyticsBuilder.Day(Day, 6, data);

        Assert.Equal(2, day.Pcs.Count);
        var pc1 = day.Pcs[0];
        Assert.Equal(2, pc1.Blocks.Count);
        Assert.Equal("Standart", pc1.Blocks[0].Tariff);
        Assert.True(pc1.Blocks[1].Active);
        Assert.Equal(1.75, pc1.BusyHours);
        Assert.True(day.Pcs[1].Blocks[0].Reserved);
    }

    [Fact]
    public async Task Demo_source_gives_a_plausible_report()
    {
        var zones = Enumerable.Range(0, 24).Select(i => i < 4 ? "VIP" : "Standard").ToList();
        var source = new DemoAnalyticsSource(zones, () => At(26, 22));
        var service = new AnalyticsService(() => source);
        var report = await service.ReportAsync("2026-10-01", "2026-10-26", "auto", 6, CancellationToken.None);

        Assert.Equal(24, report.Usage.PcCount);
        Assert.InRange(report.Usage.Occupancy, 0.2, 0.95);
        Assert.True(report.Money.Kassa > 0 && report.Money.Bar > 0);
        Assert.Equal(7, report.Heatmap.Count);
        Assert.Same(report, await service.ReportAsync("2026-10-01", "2026-10-26", "auto", 6, CancellationToken.None));
    }

    [Theory]
    [InlineData("2026-10-26", "2026-10-01")]
    [InlineData("2025-01-01", "2026-10-26")]
    [InlineData("26.10.2026", "2026-10-26")]
    public async Task Bad_ranges_are_refused(string from, string to)
    {
        var service = new AnalyticsService(() => throw new InvalidOperationException("must not load"));
        var error = await Assert.ThrowsAsync<SetupException>(() => service.ReportAsync(from, to, "day", 6, CancellationToken.None));
        Assert.Equal("analytics_range", error.Code);
    }
}

public class AnalyticsAccessTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void First_pin_is_set_without_an_old_one_and_unlocks()
    {
        var access = new AnalyticsAccess(null);
        Assert.False(access.Status(null).PinSet);
        Assert.Equal("pin_not_set", Assert.Throws<SetupException>(() => access.Require(null)).Code);

        var token = access.SetPin(new AnalyticsPinChangeRequest(null, "2468")).Token;
        access.Require(token);
        Assert.True(access.Status(token).Unlocked);
        Assert.False(access.Status("forged").Unlocked);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a4")]
    [InlineData("")]
    public void Pin_must_be_four_to_eight_digits(string pin)
    {
        var access = new AnalyticsAccess(null);
        Assert.Equal("pin_format", Assert.Throws<SetupException>(() => access.SetPin(new AnalyticsPinChangeRequest(null, pin))).Code);
    }

    [Fact]
    public void Five_wrong_pins_lock_for_five_minutes()
    {
        var clock = new Clock();
        var access = new AnalyticsAccess(null, clock);
        access.SetPin(new AnalyticsPinChangeRequest(null, "2468"));

        for (var i = 1; i < AnalyticsAccess.MaxAttempts; i++)
        {
            var wrong = Assert.Throws<SetupException>(() => access.Unlock(new AnalyticsPinRequest("0000")));
            Assert.Equal("pin_wrong", wrong.Code);
            Assert.Equal((AnalyticsAccess.MaxAttempts - i).ToString(), wrong.Detail);
        }

        Assert.Equal("pin_locked", Assert.Throws<SetupException>(() => access.Unlock(new AnalyticsPinRequest("0000"))).Code);
        // Even the right PIN waits out the lock.
        Assert.Equal("pin_locked", Assert.Throws<SetupException>(() => access.Unlock(new AnalyticsPinRequest("2468"))).Code);

        clock.Now += TimeSpan.FromMinutes(5);
        Assert.NotEmpty(access.Unlock(new AnalyticsPinRequest("2468")).Token);
    }

    [Fact]
    public void Token_expires_after_fifteen_idle_minutes_but_slides_with_use()
    {
        var clock = new Clock();
        var access = new AnalyticsAccess(null, clock);
        var token = access.SetPin(new AnalyticsPinChangeRequest(null, "2468")).Token;

        clock.Now += TimeSpan.FromMinutes(10);
        access.Require(token);
        clock.Now += TimeSpan.FromMinutes(10);
        access.Require(token);
        clock.Now += TimeSpan.FromMinutes(16);
        Assert.Equal("pin_required", Assert.Throws<SetupException>(() => access.Require(token)).Code);
    }

    [Fact]
    public void Changing_the_pin_needs_the_old_one_and_ends_other_tokens()
    {
        var access = new AnalyticsAccess(null);
        var first = access.SetPin(new AnalyticsPinChangeRequest(null, "2468")).Token;

        Assert.Equal("pin_wrong", Assert.Throws<SetupException>(() => access.SetPin(new AnalyticsPinChangeRequest(null, "1357"))).Code);
        var second = access.SetPin(new AnalyticsPinChangeRequest("2468", "1357")).Token;

        Assert.Equal("pin_required", Assert.Throws<SetupException>(() => access.Require(first)).Code);
        access.Require(second);
        Assert.NotEmpty(access.Unlock(new AnalyticsPinRequest("1357")).Token);
    }

    [Fact]
    public void File_keeps_only_a_salted_hash_and_the_day_start()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mj-analytics-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "analytics.json");
        try
        {
            var access = new AnalyticsAccess(path);
            var token = access.SetPin(new AnalyticsPinChangeRequest(null, "2468")).Token;
            access.SetDayStart(new AnalyticsSettingsRequest(5), token);

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("2468", text);
            Assert.Contains("\"DayStartHour\":5", text);

            var reopened = new AnalyticsAccess(path);
            Assert.Equal(5, reopened.DayStartHour);
            Assert.True(reopened.Status(null).PinSet);
            Assert.False(reopened.Status(token).Unlocked);
            Assert.NotEmpty(reopened.Unlock(new AnalyticsPinRequest("2468")).Token);
            Assert.Equal("analytics_settings", Assert.Throws<SetupException>(() => reopened.SetDayStart(new AnalyticsSettingsRequest(24),
                reopened.Unlock(new AnalyticsPinRequest("2468")).Token)).Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
