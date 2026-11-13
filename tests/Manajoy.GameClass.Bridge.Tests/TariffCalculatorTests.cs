using Manajoy.GameClass.Bridge.Pricing;

namespace Manajoy.GameClass.Bridge.Tests;

/// <summary>
/// Lab club tariffs (GC-SERVER, read 2026-10-15): Registry StartMoneyMinimum "0,5", StartMoneyMaximum 200,
/// MaximumTrustPostPay 200; roundtime 1 s. Expected values are what GameClass 3.85.6.1 itself did in the lab
/// (lab-logs/acceptance/2026-10-05-lab-as-club/*.json) or follow from its source (uTariffication.pas).
/// </summary>
public class TariffCalculatorTests
{
    private static readonly PricingSettings Lab = new(0.5, 200, 200);

    internal static PricingTariff Internet => new(1, "Internet", 1, 1, 0.5, false, 1, 100, 200,
    [
        new(1, "По умолчанию", TimeSpan.Zero, TimeSpan.Zero, 10, false, "1234567"),
        new(2, "Ночной", TimeSpan.FromHours(23), TimeSpan.FromHours(7), 8, false, "1234567"),
        new(3, "Ночь пакет", TimeSpan.Zero, TimeSpan.FromHours(7), 50, true, "1234567"),
    ]);

    internal static PricingTariff Games => new(2, "Games", 1, 1, 0.5, false, 1, 100, 200,
    [
        new(4, "По умолчанию", TimeSpan.Zero, TimeSpan.Zero, 8, false, "1234567"),
        new(5, "Пакет", TimeSpan.Zero, TimeSpan.FromHours(7), 30, true, "1234567"),
    ]);

    private static TariffCalculator Calc(PricingTariff tariff, PricingSettings? settings = null) => new(tariff, settings ?? Lab);

    // 2026-10-05 was a Monday.
    private static DateTime At(int hour, int minute, int second = 0, int day = 5) => new(2026, 10, day, hour, minute, second);

    [Fact]
    public void Lab_prepaid_10_lasts_exactly_one_hour()
    {
        // 2.json: PC1 Internet, started 17:16:34 with 10 → ends 18:16:34.
        var start = At(17, 16, 34);
        Assert.Equal(At(18, 16, 34), start + Calc(Internet).CalculateTimeLength(start, 10));
    }

    [Fact]
    public void Lab_adding_5_gives_half_an_hour_more()
    {
        // 3.json: total 15 → 18:46:34.
        var start = At(17, 16, 34);
        Assert.Equal(At(18, 46, 34), start + Calc(Internet).CalculateTimeLength(start, 15));
    }

    [Fact]
    public void Lab_tariff_change_prices_the_rest_from_the_new_segment()
    {
        // 4.json: switched to Games at 17:17:31 with 15 − 0.5 spent = 14.5 at 8/h → 19:06:16.
        var segment = At(17, 17, 31);
        Assert.Equal(At(19, 6, 16), segment + Calc(Games).CalculateTimeLength(segment, 14.5));
    }

    [Fact]
    public void Lab_one_coin_lasts_six_minutes_because_the_lab_minimum_is_half()
    {
        // 16a.json: 1 → 18:03:56..18:09:56.
        var start = At(18, 3, 56);
        Assert.Equal(At(18, 9, 56), start + Calc(Internet).CalculateTimeLength(start, 1));
    }

    [Fact]
    public void Below_the_minimum_buys_no_time()
    {
        Assert.Equal(TimeSpan.Zero, Calc(Internet, new PricingSettings(5, 200, 200)).CalculateTimeLength(At(12, 0), 4.5));
    }

    [Fact]
    public void Duration_price_rounds_up_and_charges_the_minimum()
    {
        var calc = Calc(Internet, new PricingSettings(5, 200, 200));
        var now = At(12, 0, 40);

        // 10 min at 10/h = 1.67 → rounded up to 2.0 → raised to the minimum 5 → which really buys 30 minutes.
        var cost = calc.CalculateCost(now, now.AddMinutes(10), clearSeconds: true);
        Assert.Equal(5, cost, 6);
        Assert.Equal(TimeSpan.FromMinutes(30), calc.CalculateTimeLength(now, cost));

        // 2 h by day = 20.
        Assert.Equal(20, calc.CalculateCost(now, now.AddHours(2), clearSeconds: true), 6);
    }

    [Fact]
    public void Day_to_night_boundary_uses_each_minutes_own_rate()
    {
        // 22:30 with 10: 30 min by day cost 5, the remaining 5 at the night rate 8/h last 37.5 min → 23:37:30 on paper.
        // GameClass's minute-by-minute subtraction leaves -1e-15 instead of 0 here (same sign in double and x87
        // extended precision), so it steps one second back: 23:37:29. The port keeps that.
        var start = At(22, 30);
        Assert.Equal(At(23, 37, 29), start + Calc(Internet).CalculateTimeLength(start, 10));

        // The other way: 2 h from 22:00 = 1 h day (10) + 1 h night (8) = 18.
        Assert.Equal(18, Calc(Internet).CalculateCost(At(22, 0), At(24 - 24, 0, 0, 6), clearSeconds: true), 6);
    }

    [Fact]
    public void Cheapest_variant_wins_where_they_overlap()
    {
        var calc = Calc(Internet);
        Assert.Equal("По умолчанию", calc.CurrentVariant(At(15, 0), TimeSpan.FromHours(1)).Name);
        Assert.Equal("Ночной", calc.CurrentVariant(At(2, 0), TimeSpan.FromHours(1)).Name);
        Assert.Equal("Ночной", calc.CurrentVariant(At(7, 0), TimeSpan.FromHours(1)).Name); // inclusive end
    }

    [Fact]
    public void Weekday_only_variant_is_checked_against_each_moments_own_day()
    {
        // A Friday-only night rate (day 5) is not used at 02:00 on Saturday.
        var tariff = new PricingTariff(9, "T", 1, 1, 0.5, false, 0, 100, 200,
        [
            new(1, "Day", TimeSpan.Zero, TimeSpan.Zero, 10, false, "1234567"),
            new(2, "Fri night", TimeSpan.FromHours(22), TimeSpan.FromHours(6), 5, false, "5"),
        ]);
        var calc = Calc(tariff);
        Assert.Equal("Fri night", calc.CurrentVariant(new DateTime(2026, 10, 9, 23, 0, 0), TimeSpan.FromHours(1)).Name);
        Assert.Equal("Day", calc.CurrentVariant(new DateTime(2026, 10, 10, 2, 0, 0), TimeSpan.FromHours(1)).Name);
    }

    [Fact]
    public void Condition_variant_applies_only_to_long_orders()
    {
        // ">=/180": 3 hours or more at 7/h instead of 10/h.
        var tariff = new PricingTariff(9, "T", 1, 1, 0.5, false, 0, 100, 200,
        [
            new(1, "Base", TimeSpan.Zero, TimeSpan.Zero, 10, false, "1234567"),
            new(2, "3h+", TimeSpan.Zero, TimeSpan.Zero, 7, false, "1234567", ">=/180"),
        ]);
        var calc = Calc(tariff);
        Assert.Equal(20, calc.CalculateCost(At(12, 0), At(14, 0), clearSeconds: true), 6);
        Assert.Equal(21, calc.CalculateCost(At(12, 0), At(15, 0), clearSeconds: true), 6);
    }

    [Fact]
    public void Night_packet_is_offered_only_inside_its_window_and_ends_with_it()
    {
        var calc = Calc(Internet);
        var packet = Internet.Variants.Single(v => v.IsPacket);
        Assert.False(TariffCalculator.IsAvailable(packet, At(12, 0)));
        Assert.True(TariffCalculator.IsAvailable(packet, At(3, 20)));

        var (start, stop, cost) = calc.PacketTimes(packet, At(3, 20));
        Assert.Equal(At(0, 0), start);
        Assert.Equal(At(7, 0), stop);
        Assert.Equal(50, cost);
    }

    [Fact]
    public void Roundtime_of_a_minute_or_more_bills_in_whole_steps()
    {
        // roundtime 600 s (10-minute steps): 25 min at 6/h is charged as 3 steps = 3.0.
        var tariff = Internet with { RoundTimeSeconds = 600, RoundMoney = 0.5, Variants = [new(1, "Base", TimeSpan.Zero, TimeSpan.Zero, 6, false, "1234567")] };
        Assert.Equal(3, Calc(tariff).CalculateCost(At(12, 0), At(12, 25), clearSeconds: true), 6);
    }

    [Fact]
    public void No_variant_for_a_moment_is_an_error_not_a_price()
    {
        var tariff = Internet with { Variants = [new(1, "Day only", TimeSpan.FromHours(9), TimeSpan.FromHours(18), 10, false, "1234567")] };
        var error = Assert.Throws<QuoteException>(() => Calc(tariff).CalculateCost(At(17, 0), At(19, 0), clearSeconds: true));
        Assert.Equal("no_variant", error.Code);
    }

    [Theory]
    [InlineData(1.67, 0.5, 1, 2.0)]
    [InlineData(2.0, 0.5, 1, 2.0)]
    [InlineData(2.00000001, 0.5, 1, 2.0)]
    [InlineData(9.9, 0.5, -1, 9.5)]
    [InlineData(1234, 100, 1, 1300)]
    public void Money_rounding_matches_gameclass(double money, double step, int direction, double expected)
    {
        Assert.Equal(expected, TariffCalculator.RoundMoney(money, step, direction), 6);
    }

    [Theory]
    [InlineData("10:00", "18:00", "09:59", false)]
    [InlineData("10:00", "18:00", "18:00", true)]
    [InlineData("23:00", "07:00", "02:00", true)]
    [InlineData("23:00", "07:00", "12:00", false)]
    [InlineData("00:00", "00:00", "12:00", true)]
    public void Time_windows_follow_isbetween(string from, string to, string moment, bool expected)
    {
        Assert.Equal(expected, TariffCalculator.IsBetween(TimeSpan.Parse(moment), TimeSpan.Parse(from), TimeSpan.Parse(to)));
    }
}

public class CashierDeskTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 17, 20, 0);

    private static CashierDesk Desk(PricingSession? session = null, IReadOnlyList<DateTime>? reservations = null) =>
        new(new PricingContext("$", new PricingSettings(0.5, 200, 200),
            [TariffCalculatorTests.Internet, TariffCalculatorTests.Games,
             new PricingTariff(3, "VIP Tariff", 2, 1, 1, false, 1, 100, 200, [new(6, "VIP", TimeSpan.Zero, TimeSpan.Zero, 1, false, "1234567")])],
            new Dictionary<int, PricingPc>
            {
                [1] = new(1, 1, "busy", session, reservations ?? []),
                [7] = new(7, 2, "available", null, []),
            }), () => Now);

    [Fact]
    public void Only_tariffs_of_the_pcs_group_are_offered()
    {
        Assert.Equal(["Internet", "Games"], Desk().Pricing(1).Tariffs.Select(t => t.Name));
        Assert.Equal(["VIP Tariff"], Desk().Pricing(7).Tariffs.Select(t => t.Name));
        Assert.Equal("tariff_not_for_pc", Assert.Throws<QuoteException>(() => Desk().Quote(new(7, 1, "duration", 60, null, null))).Code);
    }

    [Fact]
    public void Offer_shows_the_current_rate_and_quick_amounts()
    {
        var internet = Desk().Pricing(1).Tariffs[0];
        Assert.Equal("По умолчанию", internet.CurrentVariant);
        Assert.Equal(10, internet.HourCost);
        Assert.Equal([10d, 20d, 30d, 50d], internet.QuickMoney);
        Assert.Empty(internet.Packets);
        Assert.Equal(200, internet.PostpayLimit);
    }

    [Fact]
    public void Duration_and_money_quotes()
    {
        var byTime = Desk().Quote(new(1, 1, "duration", 120, null, null));
        Assert.Equal(20, byTime.Cost, 6);
        // 20 at 10/h: the float remainder after 120 minutes is -3.5e-15, so GameClass ends one second early.
        Assert.Equal(Now.AddHours(2).AddSeconds(-1), byTime.End);
        Assert.Empty(byTime.Warnings);

        var byMoney = Desk().Quote(new(1, 1, "money", null, 15, null));
        Assert.Equal(Now.AddMinutes(90), byMoney.End);

        Assert.Equal("below_min", Assert.Throws<QuoteException>(() => Desk().Quote(new(1, 1, "money", null, 0.2, null))).Code);
        Assert.Contains("over_max", Desk().Quote(new(1, 1, "money", null, 250, null)).Warnings);
    }

    [Fact]
    public void A_session_running_into_a_reservation_is_flagged()
    {
        var quote = Desk(reservations: [Now.AddMinutes(45)]).Quote(new(1, 1, "duration", 60, null, null));
        Assert.Contains("reservation", quote.Warnings);
        Assert.Equal(Now.AddMinutes(45), quote.Reservation);
    }

    [Fact]
    public void Adding_money_to_the_lab_session_matches_gameclass()
    {
        // 2.json → 3.json: session 17:16:34 with 10 (ends 18:16:34); +5 → 18:46:34.
        var session = new PricingSession(1, "Internet", new DateTime(2026, 10, 5, 17, 16, 34), new DateTime(2026, 10, 5, 18, 16, 34),
            false, false, 10, 0, 0, 0);
        var desk = Desk(session);

        Assert.True(desk.Pricing(1).Session!.CanAdd);
        var quote = desk.Quote(new(1, 0, "add", null, 5, null));
        Assert.Equal(new DateTime(2026, 10, 5, 18, 46, 34), quote.End);
        Assert.Equal(30, quote.Minutes);
    }

    [Fact]
    public void Adding_money_after_a_tariff_change_uses_the_new_segment()
    {
        // 4.json: Games from 17:17:31, 15 paid, 0.5 spent before → ends 19:06:16.
        var session = new PricingSession(2, "Games", new DateTime(2026, 10, 5, 17, 17, 31), new DateTime(2026, 10, 5, 19, 6, 16),
            false, false, 15, 0.5, 0, 0);
        Assert.True(Desk(session).Pricing(1).Session!.CanAdd);
    }

    [Fact]
    public void Adding_money_is_refused_when_the_model_does_not_reproduce_the_session()
    {
        // GameClass says 18:20:00, our model says 18:16:34: show nothing rather than a wrong time.
        var session = new PricingSession(1, "Internet", new DateTime(2026, 10, 5, 17, 16, 34), new DateTime(2026, 10, 5, 18, 20, 0),
            false, false, 10, 0, 0, 0);
        var desk = Desk(session);
        Assert.False(desk.Pricing(1).Session!.CanAdd);
        Assert.Equal("add_unverified", Assert.Throws<QuoteException>(() => desk.Quote(new(1, 0, "add", null, 5, null))).Code);
    }

    [Fact]
    public void Postpay_and_packet_sessions_cannot_be_topped_up()
    {
        var postpay = new PricingSession(1, "Internet", Now.AddMinutes(-5), null, true, false, 0, 0, 0, 0);
        Assert.Equal("add_postpay", Desk(postpay).Pricing(1).Session!.AddUnavailable);
    }
}
