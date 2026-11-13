namespace Manajoy.GameClass.Bridge.Pricing;

// A faithful port of GameClass's own pricing (TTarif in Server/uTariffication.pas, GameClass 3.85), so the
// cashier sees exactly what GameClass will charge. Read-only: nothing here talks to GameClass.
//
// Kept deliberately close to the Delphi original (same loops, same double arithmetic, same rounding), because
// the quirks matter: e.g. a 10-minute order is charged the start minimum and therefore runs longer than asked.
// Anything the original would not handle (no variant for a moment, an unknown condition) raises a
// QuoteException instead of producing a number that might be wrong.

public sealed record PricingVariant(
    int Id,
    string Name,
    TimeSpan Start,
    TimeSpan Stop,
    double Cost,
    bool IsPacket,
    string DaysOfWeek,
    string? Condition = null,
    bool FreePacket = false)
{
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);

    /// <summary>"&gt;=/60" → (">=", 60); a missing number defaults to 60 like GameClass (StrToIntDef(..., 60)).</summary>
    public (string Type, int Minutes) ParsedCondition
    {
        get
        {
            var parts = (Condition ?? string.Empty).Split('/');
            var minutes = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var m) ? m : 60;
            return (parts[0].Trim(), minutes);
        }
    }
}

public sealed record PricingTariff(
    int Id,
    string Name,
    int GroupId,
    int RoundTimeSeconds,
    double RoundMoney,
    bool UseSeparateSum,
    double StartMoneyMin,
    double StartMoneyMax,
    double MaximumTrust,
    IReadOnlyList<PricingVariant> Variants);

/// <summary>Registry values (GameClass defaults: StartMoneyMinimum 5, StartMoneyMaximum 200, MaximumTrustPostPay 200).</summary>
public sealed record PricingSettings(double StartMoneyMinimum = 5, double StartMoneyMaximum = 200, double MaximumTrustPostPay = 200);

public sealed class QuoteException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
}

public sealed class TariffCalculator(PricingTariff tariff, PricingSettings settings)
{
    private const string ConditionEqualMore = ">=";
    private const int GlobalTimerSeconds = 5;

    public PricingTariff Tariff { get; } = tariff;

    public double MinimumMoney => Tariff.UseSeparateSum ? Tariff.StartMoneyMin : settings.StartMoneyMinimum;

    public double MaximumMoney => Tariff.UseSeparateSum ? Tariff.StartMoneyMax : settings.StartMoneyMaximum;

    public double PostpayLimit => Tariff.UseSeparateSum ? Tariff.MaximumTrust : settings.MaximumTrustPostPay;

    private int RoundTime => Tariff.RoundTimeSeconds > 0
        ? Tariff.RoundTimeSeconds
        : throw new QuoteException("unsupported_tariff", "roundtime is 0");

    // ------------------------------------------------------------------ public API (CalculateCost / CalculateTimeLength)

    /// <summary>TTarif.CalculateCost with discount 0 (a guest).</summary>
    public double CalculateCost(DateTime start, DateTime stop, bool clearSeconds)
    {
        if (clearSeconds)
        {
            start = ClearSeconds(start);
            stop = ClearSeconds(stop);
        }

        var summa = CalcCostByTime(start, stop);
        summa = RoundMoney(summa, Tariff.RoundMoney, 1);
        if (summa < MinimumMoney)
        {
            summa = MinimumMoney;
        }

        return summa;
    }

    /// <summary>TTarif.CalculateTimeLength with discount 0: how long `money` lasts from `start` (zero below the minimum).</summary>
    public TimeSpan CalculateTimeLength(DateTime start, double money)
    {
        if (money < MinimumMoney)
        {
            return TimeSpan.Zero;
        }

        money = RoundMoney(money, Tariff.RoundMoney, -1);
        var newStop = CalcTimeByCost(start, money);
        return RoundTimeLength(newStop - start, RoundTime, -1);
    }

    /// <summary>The non-packet variant GameClass applies at `moment` for an order of `length` (cheapest that qualifies).</summary>
    public PricingVariant CurrentVariant(DateTime moment, TimeSpan length) => VariantByTime(moment, length);

    // ------------------------------------------------------------------ packets

    /// <summary>TTarifVariants.IsAvailable: only packets, and only inside their window (free packets: on allowed days).</summary>
    public static bool IsAvailable(PricingVariant variant, DateTime now)
    {
        if (!variant.IsPacket)
        {
            return false;
        }

        if (variant.FreePacket)
        {
            var today = DayOfTheWeek(now);
            var tomorrow = today == 7 ? 1 : today + 1;
            var days = variant.DaysOfWeek;
            return (days.Contains(Digit(today)) && days.Contains(Digit(tomorrow)))
                || (days.Contains(Digit(today)) && new TimeSpan(0, 23, 59, 59, 99) - variant.Start > now.TimeOfDay);
        }

        return TypeIntersect(variant, now, now) != Intersect.Not;
    }

    /// <summary>Start, stop and price of buying `variant` now (frmCompStart: PacketTarif / FreePacketTarif).</summary>
    public (DateTime Start, DateTime Stop, double Cost) PacketTimes(PricingVariant variant, DateTime now)
    {
        if (variant.FreePacket)
        {
            return (now, now + variant.Start, variant.Cost);
        }

        var start = now;
        if (WholePrevDay(variant, now))
        {
            start = start.AddDays(-1);
        }

        start = start.Date + new TimeSpan(variant.Start.Hours, variant.Start.Minutes, 0);
        return (start, start + WholeLength(variant), variant.Cost);
    }

    private static TimeSpan WholeLength(PricingVariant variant) =>
        variant.Stop > variant.Start ? variant.Stop - variant.Start : TimeSpan.FromDays(1) - (variant.Start - variant.Stop);

    /// <summary>TTarif.GetWholePrevDay: an overnight packet bought after midnight started on the previous day.</summary>
    private static bool WholePrevDay(PricingVariant variant, DateTime now)
    {
        if (variant.FreePacket)
        {
            return false;
        }

        var curDay = DayOfTheWeek(now);
        var prevDay = curDay == 1 ? 7 : curDay - 1;
        var result = false;
        var inToday = false;
        foreach (var ch in variant.DaysOfWeek)
        {
            if (!char.IsDigit(ch))
            {
                continue;
            }

            var day = ch - '0';
            if (day == curDay)
            {
                var left = now.Date + new TimeSpan(variant.Start.Hours, variant.Start.Minutes, 0);
                var right = left + new TimeSpan(variant.Stop.Hours, variant.Stop.Minutes, 0);
                if (left <= now && now < right)
                {
                    inToday = true;
                }
            }

            if (day == prevDay)
            {
                result = true;
            }
        }

        return !inToday && result;
    }

    private enum Intersect
    {
        Not,
        All,
        Middle,
        Left,
        Right,
        LeftRight,
    }

    /// <summary>TTarifVariants.TypeIntersect, used with startR == stopR to ask "is now inside the packet window".</summary>
    private static Intersect TypeIntersect(PricingVariant variant, DateTime startR, DateTime stopR)
    {
        var dayR = Digit(DayOfTheWeek(startR));
        var startDay = variant.DaysOfWeek.Contains(dayR);
        var stopDay = DaysOfWeekStop(variant).Contains(dayR);

        var timeStartR = startR.TimeOfDay;
        var timeStopR = stopR.TimeOfDay;
        var timeStartV = variant.Start;
        var timeStopV = variant.Stop == TimeSpan.Zero ? TimeSpan.FromDays(1) - TimeSpan.FromTicks(1) : variant.Stop;

        if (!(startDay || stopDay))
        {
            return Intersect.Not;
        }

        if (!startDay && stopDay)
        {
            timeStartV = TimeSpan.Zero;
        }

        if (startDay && !stopDay)
        {
            timeStopV = TimeSpan.Zero;
        }

        if (timeStopV > timeStartV)
        {
            if (timeStartR >= timeStartV && timeStopR <= timeStopV)
            {
                return Intersect.All;
            }

            if (timeStartR < timeStartV && timeStopR > timeStopV)
            {
                return Intersect.Middle;
            }

            if (timeStopR > timeStopV && timeStartR < timeStopV)
            {
                return Intersect.Left;
            }

            if (timeStartR < timeStartV && timeStopR > timeStartV)
            {
                return Intersect.Right;
            }
        }

        if (timeStopV < timeStartV)
        {
            if ((timeStartR < timeStopV && timeStopR <= timeStopV)
                || (timeStartR >= timeStartV && timeStopR > timeStartV)
                || (timeStartR >= timeStopV && timeStopR <= timeStopV))
            {
                return Intersect.All;
            }

            if (timeStopR > timeStartV && timeStartR < timeStopV)
            {
                return Intersect.LeftRight;
            }

            if (timeStartR < timeStopV)
            {
                return Intersect.Left;
            }

            if (timeStopR > timeStartV)
            {
                return Intersect.Right;
            }
        }

        return Intersect.Not;
    }

    /// <summary>TTarifVariants.GetDaysofweekStop: overnight (or all-day 00:00–00:00) windows end on the next day.</summary>
    private static string DaysOfWeekStop(PricingVariant variant)
    {
        var overnight = variant.Start > variant.Stop || (variant.Start == TimeSpan.Zero && variant.Stop == TimeSpan.Zero);
        return new string(variant.DaysOfWeek.Where(char.IsDigit)
            .Select(ch => overnight ? (ch == '7' ? '1' : (char)(ch + 1)) : ch).ToArray());
    }

    // ------------------------------------------------------------------ the "mega functions"

    /// <summary>MegaFunctionCalcCostByTime: per-minute then per-roundtime steps, each at the variant valid at its start.</summary>
    private double CalcCostByTime(DateTime start, DateTime stop)
    {
        var summa = 0d;
        if (stop - start > TimeSpan.FromDays(1))
        {
            stop = start.AddDays(1);
        }

        var length = stop - start;
        var moment = start;
        var roundTime = RoundTime;
        if (roundTime < 60)
        {
            var lastMinutes = (roundTime + 59) / 60;
            var stopWithoutLastMinutes = stop.AddMinutes(-lastMinutes);
            while (moment < stopWithoutLastMinutes)
            {
                summa += VariantByTime(moment, length).Cost / 60;
                moment = moment.AddMinutes(1);
            }
        }

        var step = roundTime / 60d / 60d;
        while (moment < stop)
        {
            summa += VariantByTime(moment, length).Cost * step;
            moment = moment.AddSeconds(roundTime);
        }

        return summa;
    }

    /// <summary>MegaFunctionCalcTimeByCost: spend the money minute by minute, then in roundtime steps; step back on overdraw.</summary>
    private DateTime CalcTimeByCost(DateTime start, double cost)
    {
        var summa = cost;
        var moment = start;
        var minutes = 0;
        var roundTime = RoundTime;
        if (roundTime < 60)
        {
            while (RoundTo3(summa) >= 0 && minutes <= 1441)
            {
                summa -= VariantByMoney(moment, cost).Cost / 60;
                moment = moment.AddMinutes(1);
                minutes++;
            }

            moment = moment.AddMinutes(-1);
            summa += VariantByMoney(moment, cost).Cost / 60;
        }

        var step = roundTime / 60d / 60d;
        while (RoundTo3(summa) >= 0 && moment - start < TimeSpan.FromDays(1))
        {
            summa -= VariantByMoney(moment, cost).Cost * step;
            moment = moment.AddSeconds(roundTime);
        }

        moment = moment.AddSeconds(-roundTime);
        summa += VariantByMoney(moment, cost).Cost * step;
        if (summa < 0)
        {
            moment = moment.AddSeconds(-roundTime);
        }

        return moment;
    }

    // ------------------------------------------------------------------ variant choice

    private PricingVariant VariantByTime(DateTime moment, TimeSpan length)
    {
        // Two passes, as in GameClass: the money of the first guess decides money-based conditions.
        var first = Inexpensive(moment, byTime: true, length, 0);
        var money = first.Cost * MinutesBetween(length) / 60;
        return Inexpensive(moment, byTime: true, length, money);
    }

    private PricingVariant VariantByMoney(DateTime moment, double money) => Inexpensive(moment, byTime: false, TimeSpan.Zero, money);

    /// <summary>GetInexpensiveTarifVariant: the cheapest non-packet variant covering the moment (time and weekday) whose condition holds.</summary>
    private PricingVariant Inexpensive(DateTime moment, bool byTime, TimeSpan length, double money)
    {
        PricingVariant? best = null;
        var minimum = double.MaxValue;
        var day = Digit(DayOfTheWeek(moment));
        foreach (var variant in Tariff.Variants)
        {
            if (variant.IsPacket || !(variant.Cost < minimum) || !IsBetween(moment.TimeOfDay, variant.Start, variant.Stop)
                || !variant.DaysOfWeek.Contains(day))
            {
                continue;
            }

            if (variant.HasCondition)
            {
                var (type, conditionMinutes) = variant.ParsedCondition;
                if (type != ConditionEqualMore)
                {
                    // GameClass never applies other condition types; nothing to price with.
                    continue;
                }

                if (variant.Cost <= 0)
                {
                    throw new QuoteException("unsupported_tariff", $"condition on a zero-cost variant {variant.Id}");
                }

                var byMoney = money / variant.Cost * 60 >= conditionMinutes;
                var ok = byTime ? MinutesBetween(length) >= conditionMinutes || byMoney : byMoney;
                if (!ok)
                {
                    continue;
                }
            }

            best = variant;
            minimum = variant.Cost;
        }

        return best ?? throw new QuoteException("no_variant", $"no tariff variant covers {moment:ddd HH:mm}");
    }

    // ------------------------------------------------------------------ Delphi helpers

    /// <summary>uVirtualTime.IsBetween: inclusive; start &gt; end wraps midnight; start == end is all day.</summary>
    internal static bool IsBetween(TimeSpan moment, TimeSpan start, TimeSpan end)
    {
        if (start == end)
        {
            return true;
        }

        if (start < end)
        {
            return start <= moment && end >= moment;
        }

        return (moment >= start && moment >= end) || (moment <= start && moment <= end);
    }

    /// <summary>fnRoundMoney: floor (direction &lt; 0) or ceil with 1e-5 tolerance to a multiple of `step`.</summary>
    internal static double RoundMoney(double money, double step, int direction)
    {
        if (step <= 0)
        {
            return money;
        }

        var parts = Math.Truncate(money / step);
        var rounded = step * parts;
        if (direction > 0 && Math.Abs(money - rounded) > 0.00001)
        {
            rounded += step;
        }

        return rounded;
    }

    /// <summary>fnRoundTime + TimeFromSeconds (capped at 23:59:00).</summary>
    internal static TimeSpan RoundTimeLength(TimeSpan length, int stepSeconds, int direction)
    {
        var seconds = (long)Math.Round(length.TotalSeconds, MidpointRounding.ToEven);
        if (seconds < 2)
        {
            seconds += GlobalTimerSeconds;
        }

        if (direction > 0)
        {
            seconds -= 1;
        }

        var old = seconds;
        var i1 = (long)Math.Round(seconds / (double)stepSeconds, MidpointRounding.ToEven);
        seconds = i1 * stepSeconds <= seconds ? (i1 + 1) * stepSeconds : i1 * stepSeconds;
        if (direction < 0 && seconds > old)
        {
            seconds -= stepSeconds;
        }

        var hours = seconds / 3600;
        if (hours > 23)
        {
            return new TimeSpan(23, 59, 0);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Delphi RoundTo(x, -3) (banker's rounding); only its sign is used.</summary>
    private static double RoundTo3(double value) => Math.Round(value * 1000, MidpointRounding.ToEven) / 1000;

    private static double MinutesBetween(TimeSpan length) => Math.Floor(length.TotalMinutes + 1e-9);

    private static DateTime ClearSeconds(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    /// <summary>DateUtils.DayOfTheWeek: Monday = 1 … Sunday = 7.</summary>
    internal static int DayOfTheWeek(DateTime value) => value.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)value.DayOfWeek;

    private static string Digit(int day) => day.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
