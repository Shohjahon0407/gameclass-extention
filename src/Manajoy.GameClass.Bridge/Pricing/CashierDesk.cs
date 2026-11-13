using Manajoy.GameClass.Bridge.Snapshot;

namespace Manajoy.GameClass.Bridge.Pricing;

// The cashier assistant: for a PC picked on the map, what each tariff costs now (by time, by money, packets,
// postpay) and, for a running prepaid session, how far extra money moves its end. Prices only — the session
// itself is still started in GameClass by the operator; nothing is written anywhere.

public sealed record PricingSession(
    int TariffId,
    string? TariffName,
    DateTime SegmentStart,
    DateTime? EndsAt,
    bool Postpay,
    bool IsPacket,
    double Prepaid,
    double PreviousSegmentsCost,
    double ExtraCost,
    int AdditionalMinutes);

public sealed record PricingPc(int Number, int GroupId, string State, PricingSession? Session, IReadOnlyList<DateTime> ReservationStarts);

public sealed record PricingContext(string? Currency, PricingSettings Settings, IReadOnlyList<PricingTariff> Tariffs, IReadOnlyDictionary<int, PricingPc> Pcs)
{
    /// <summary>Null when the installed service is older than this window (no pricing fields in its snapshot).</summary>
    public static PricingContext? FromSnapshot(ClubSnapshot snapshot)
    {
        if (snapshot.Tariffs.Any(t => t.RoundTimeSeconds is null || t.Variants.Any(v => v.FromSeconds is null)))
        {
            return null;
        }

        var club = snapshot.Club;
        var settings = new PricingSettings(
            (double)(club.StartMoneyMinimum ?? 5m),
            (double)(club.StartMoneyMaximum ?? 200m),
            (double)(club.MaxTrustPostpay ?? 200m));
        var tariffs = snapshot.Tariffs.Select(t => new PricingTariff(
            t.Id,
            t.Name,
            t.GroupId,
            t.RoundTimeSeconds ?? 1,
            (double)t.RoundMoney,
            t.UseSeparateSum ?? false,
            (double)(t.StartMoneyMin ?? 0m),
            (double)(t.StartMoneyMax ?? 0m),
            (double)(t.MaximumTrust ?? 0m),
            t.Variants.Select(v => new PricingVariant(
                v.Id, v.Name, TimeSpan.FromSeconds(v.FromSeconds ?? 0), TimeSpan.FromSeconds(v.ToSeconds ?? 0),
                (double)v.Cost, v.IsPacket, v.DaysOfWeek, v.Condition, v.FreePacket ?? false)).ToList())).ToList();

        var pcs = snapshot.Pcs.ToDictionary(pc => pc.Number, pc => new PricingPc(
            pc.Number,
            pc.GroupId,
            pc.State,
            pc.Session is { } s
                ? new PricingSession(
                    s.TariffId,
                    s.TariffName,
                    s.SegmentStartedAt.DateTime,
                    s.EndsAt?.DateTime,
                    s.Postpay,
                    s.IsPacket,
                    (double)(s.PrepaidAmount ?? 0m),
                    (double)Math.Max(0m, s.TimeCost - (s.SegmentTimeCost ?? s.TimeCost)),
                    (double)(s.ExtraCost ?? 0m),
                    s.AdditionalMinutes ?? 0)
                : null,
            pc.UpcomingReservations.Select(r => r.StartsAt.DateTime).OrderBy(d => d).ToList()));
        return new PricingContext(club.Currency, settings, tariffs, pcs);
    }
}

// ------------------------------------------------------------------ wire model (camelCase via the setup host)

public sealed record PacketOffer(int VariantId, string Name, double Cost, DateTime Start, DateTime End);

public sealed record TariffOffer(
    int Id,
    string Name,
    string? CurrentVariant,
    double? HourCost,
    double MinMoney,
    double MaxMoney,
    IReadOnlyList<int> QuickMinutes,
    IReadOnlyList<double> QuickMoney,
    IReadOnlyList<PacketOffer> Packets,
    double PostpayLimit,
    DateTime? PostpayEnd,
    string? Error);

public sealed record RunningSession(string? TariffName, DateTime? EndsAt, bool Postpay, bool IsPacket, double Prepaid, bool CanAdd, string? AddUnavailable);

public sealed record PricingInfo(
    int Pc,
    string? Currency,
    DateTime Now,
    IReadOnlyList<TariffOffer> Tariffs,
    RunningSession? Session,
    DateTime? NextReservation);

public sealed record QuoteRequest(int Pc, int TariffId, string Mode, int? Minutes, double? Money, int? VariantId);

public sealed record QuoteResult(
    string Mode,
    double Cost,
    DateTime Start,
    DateTime End,
    int Minutes,
    int? RequestedMinutes,
    IReadOnlyList<string> Warnings,
    DateTime? Reservation);

public sealed class CashierDesk(PricingContext context, Func<DateTime> clock)
{
    private static readonly int[] QuickMinutesSet = [30, 60, 120, 180, 300];
    private static readonly TimeSpan EndTolerance = TimeSpan.FromSeconds(2);

    public PricingInfo Pricing(int pcNumber)
    {
        var pc = Pc(pcNumber);
        var now = clock();
        var offers = TariffsFor(pc).Select(t => Offer(t, now)).ToList();
        return new PricingInfo(pc.Number, context.Currency, now, offers, Running(pc), NextReservation(pc, now));
    }

    public QuoteResult Quote(QuoteRequest request)
    {
        var pc = Pc(request.Pc);
        var now = clock();
        if (request.Mode == "add")
        {
            return AddMoney(pc, request.Money ?? 0);
        }

        var tariff = TariffsFor(pc).FirstOrDefault(t => t.Id == request.TariffId)
            ?? throw new QuoteException("tariff_not_for_pc");
        var calc = new TariffCalculator(tariff, context.Settings);
        var warnings = new List<string>();
        DateTime end;
        double cost;
        int? requested = null;
        switch (request.Mode)
        {
            case "duration":
                var minutes = request.Minutes is > 0 and <= 24 * 60 ? request.Minutes.Value : throw new QuoteException("bad_minutes");
                requested = minutes;
                // frmCompStart (TimeLength): price of the ordered time, then the end GameClass really sets for that money.
                cost = calc.CalculateCost(now, now.AddMinutes(minutes), clearSeconds: true);
                end = now + calc.CalculateTimeLength(now, cost);
                if ((end - now).TotalMinutes >= minutes + 1)
                {
                    warnings.Add("extended");
                }

                if (cost > calc.MaximumMoney)
                {
                    warnings.Add("over_max");
                }

                break;
            case "money":
                cost = request.Money is > 0 ? request.Money.Value : throw new QuoteException("bad_money");
                if (cost < calc.MinimumMoney)
                {
                    throw new QuoteException("below_min", Format(calc.MinimumMoney));
                }

                if (cost > calc.MaximumMoney)
                {
                    warnings.Add("over_max");
                }

                end = now + calc.CalculateTimeLength(now, cost);
                break;
            case "packet":
                var variant = tariff.Variants.FirstOrDefault(v => v.Id == request.VariantId && TariffCalculator.IsAvailable(v, now))
                    ?? throw new QuoteException("packet_unavailable");
                var (start, stop, price) = calc.PacketTimes(variant, now);
                return new QuoteResult("packet", price, start, stop, (int)Math.Round((stop - start).TotalMinutes), null,
                    ReservationWarning(pc, now, stop, warnings), NextReservation(pc, now));
            case "postpay":
                cost = calc.PostpayLimit;
                end = now + calc.CalculateTimeLength(now, cost);
                break;
            default:
                throw new QuoteException("bad_mode");
        }

        if ((end - now) >= new TimeSpan(23, 59, 0))
        {
            warnings.Add("day_cap");
        }

        return new QuoteResult(request.Mode, cost, now, end, (int)Math.Floor((end - now).TotalMinutes), requested,
            ReservationWarning(pc, now, end, warnings), NextReservation(pc, now));
    }

    /// <summary>
    /// GameClass RecalcStopTime: end = segment start + time for (paid − earlier segments − extras), plus penalty minutes.
    /// Only offered when this reproduces the session's current end, so a wrong estimate is never shown.
    /// </summary>
    private QuoteResult AddMoney(PricingPc pc, double money)
    {
        var (calc, session, currentEnd) = AddContext(pc);
        if (money <= 0)
        {
            throw new QuoteException("bad_money");
        }

        var remaining = session.Prepaid - session.PreviousSegmentsCost - session.ExtraCost;
        var newEnd = session.SegmentStart + calc.CalculateTimeLength(session.SegmentStart, remaining + money)
            + TimeSpan.FromMinutes(session.AdditionalMinutes);
        var warnings = new List<string>();
        if (session.Prepaid + money > calc.MaximumMoney)
        {
            warnings.Add("over_max");
        }

        return new QuoteResult("add", money, currentEnd, newEnd, (int)Math.Floor((newEnd - currentEnd).TotalMinutes), null,
            ReservationWarning(pc, clock(), newEnd, warnings), NextReservation(pc, clock()));
    }

    private (TariffCalculator Calc, PricingSession Session, DateTime CurrentEnd) AddContext(PricingPc pc)
    {
        var session = pc.Session ?? throw new QuoteException("no_session");
        if (session.Postpay)
        {
            throw new QuoteException("add_postpay");
        }

        if (session.IsPacket)
        {
            throw new QuoteException("add_packet");
        }

        var tariff = context.Tariffs.FirstOrDefault(t => t.Id == session.TariffId) ?? throw new QuoteException("add_unverified", "tariff");
        var calc = new TariffCalculator(tariff, context.Settings);
        var currentEnd = session.EndsAt ?? throw new QuoteException("add_unverified", "no end");
        var remaining = session.Prepaid - session.PreviousSegmentsCost - session.ExtraCost;
        var predicted = session.SegmentStart + calc.CalculateTimeLength(session.SegmentStart, remaining)
            + TimeSpan.FromMinutes(session.AdditionalMinutes);
        if ((predicted - currentEnd).Duration() > EndTolerance)
        {
            throw new QuoteException("add_unverified", $"predicted {predicted:HH:mm:ss}, GameClass {currentEnd:HH:mm:ss}");
        }

        return (calc, session, currentEnd);
    }

    private RunningSession? Running(PricingPc pc)
    {
        if (pc.Session is not { } s)
        {
            return null;
        }

        string? unavailable = null;
        try
        {
            AddContext(pc);
        }
        catch (QuoteException ex)
        {
            unavailable = ex.Code;
        }

        return new RunningSession(s.TariffName, s.EndsAt, s.Postpay, s.IsPacket, s.Prepaid, unavailable is null, unavailable);
    }

    private TariffOffer Offer(PricingTariff tariff, DateTime now)
    {
        var calc = new TariffCalculator(tariff, context.Settings);
        try
        {
            var current = calc.CurrentVariant(now, TimeSpan.FromHours(1));
            var quickMoney = new[] { 1, 2, 3, 5 }
                .Select(h => TariffCalculator.RoundMoney(current.Cost * h, tariff.RoundMoney, 1))
                .Where(m => m > 0)
                .Distinct()
                .ToList();
            var packets = tariff.Variants
                .Where(v => TariffCalculator.IsAvailable(v, now))
                .Select(v =>
                {
                    var (start, stop, cost) = calc.PacketTimes(v, now);
                    return new PacketOffer(v.Id, v.Name, cost, start, stop);
                })
                .ToList();
            var postpayEnd = now + calc.CalculateTimeLength(now, calc.PostpayLimit);
            return new TariffOffer(tariff.Id, tariff.Name, current.Name, current.Cost, calc.MinimumMoney, calc.MaximumMoney,
                QuickMinutesSet, quickMoney, packets, calc.PostpayLimit, postpayEnd, null);
        }
        catch (QuoteException ex)
        {
            return new TariffOffer(tariff.Id, tariff.Name, null, null, calc.MinimumMoney, calc.MaximumMoney, [], [], [],
                calc.PostpayLimit, null, ex.Code);
        }
    }

    /// <summary>GameClass offers a tariff only to computers of the same group (strict equality).</summary>
    private IEnumerable<PricingTariff> TariffsFor(PricingPc pc) => context.Tariffs.Where(t => t.GroupId == pc.GroupId);

    private PricingPc Pc(int number) => context.Pcs.TryGetValue(number, out var pc) ? pc : throw new QuoteException("unknown_pc");

    private static DateTime? NextReservation(PricingPc pc, DateTime now) =>
        pc.ReservationStarts.Where(r => r > now).Select(r => (DateTime?)r).FirstOrDefault();

    /// <summary>GameClass refuses a session that runs into the next reservation; warn before the cashier tries.</summary>
    private static List<string> ReservationWarning(PricingPc pc, DateTime now, DateTime end, List<string> warnings)
    {
        if (NextReservation(pc, now) is { } next && end > next)
        {
            warnings.Add("reservation");
        }

        return warnings;
    }

    private static string Format(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
