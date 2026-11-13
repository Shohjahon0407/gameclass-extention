using System.Globalization;

namespace Manajoy.GameClass.Bridge.Analytics;

/// <summary>
/// Turns raw GameClass rows into the owner's report. Pure and deterministic (tests feed synthetic rows).
///
/// Days are business days: a day starts at <see cref="AnalyticsQuery.DayStartHour"/> (06:00 by default), so a
/// night session at 02:00 belongs to the evening before. Money is counted when it was taken (cash basis, like
/// GameClass's own shift report); busy time is counted where it happened, cut at the window edges and at "now".
/// Computers with a negative number are deleted ones (history only): they never count as capacity.
/// </summary>
public static class AnalyticsBuilder
{
    public const int MaxDays = 366;

    /// <summary>A session paid this long before it started was booked in advance.</summary>
    private static readonly TimeSpan BookedLead = TimeSpan.FromMinutes(5);

    public static (DateTime From, DateTime To) Window(AnalyticsQuery query) =>
        (query.From.ToDateTime(new TimeOnly(query.DayStartHour, 0)),
         query.To.AddDays(1).ToDateTime(new TimeOnly(query.DayStartHour, 0)));

    /// <summary>The rows to load: the window and the same length before it (for the ▲/▼ comparison).</summary>
    public static (DateTime From, DateTime To) LoadRange(AnalyticsQuery query)
    {
        var (from, to) = Window(query);
        return (from - (to - from), to);
    }

    public static string NormalizeGroup(AnalyticsQuery query)
    {
        var days = query.To.DayNumber - query.From.DayNumber + 1;
        return query.Group switch
        {
            "hour" when days <= 3 => "hour",
            "week" => "week",
            "month" => "month",
            "day" when days <= 92 => "day",
            _ => days <= 3 ? "hour" : days <= 62 ? "day" : days <= 200 ? "week" : "month",
        };
    }

    public static AnalyticsReport Build(AnalyticsQuery query, AnalyticsData data)
    {
        var group = NormalizeGroup(query);
        var (from, to) = Window(query);
        var (prevFrom, prevTo) = (from - (to - from), from);
        var now = data.Now;
        var capacity = data.Computers.Count(c => c.Number > 0);
        var computers = data.Computers.ToDictionary(c => c.Id);

        var buckets = Buckets(group, from, to, query.DayStartHour);
        var total = new MoneyAcc();
        var previous = new MoneyAcc();
        var perBucket = buckets.Select(_ => new MoneyAcc()).ToArray();

        MoneyAcc? Target(DateTime at, out int bucket)
        {
            bucket = -1;
            if (at >= from && at < to)
            {
                bucket = BucketIndex(buckets, at);
                return total;
            }

            return at >= prevFrom && at < prevTo ? previous : null;
        }

        void Feed(DateTime at, Action<MoneyAcc> add)
        {
            if (Target(at, out var bucket) is { } acc)
            {
                add(acc);
                if (bucket >= 0)
                {
                    add(perBucket[bucket]);
                }
            }
        }

        foreach (var e in data.MoneyEvents)
        {
            if (!e.IsAccount && !e.Postpay)
            {
                Feed(e.Moment, a => a.GuestMoney += e.Amount);
            }
        }

        // Internet and print are charged per session part; counted at the part's start (as GameClass does).
        foreach (var s in data.Segments.Where(s => !s.IsAccount && !s.Postpay && s.Status != 0))
        {
            Feed(s.Start, a => a.Internet += s.TrafficCost);
        }

        foreach (var first in FirstSegments(data.Segments).Where(s => !s.IsAccount && !s.Postpay && s.Status != 0))
        {
            Feed(first.Start, a => a.Print += first.PrintCost);
        }

        foreach (var b in data.BarSales)
        {
            var sum = b.Quantity * b.UnitPrice;
            Feed(b.Moment, a =>
            {
                if (b.TypeCost == 1)
                {
                    a.BarCash += sum;
                }
                else if (b.IsAccount)
                {
                    a.BarFromAccounts += sum;
                }
                else
                {
                    a.BarInSessions += sum;
                }
            });
        }

        foreach (var m in data.AccountMoney.Where(m => m.OperatorId != 0))
        {
            Feed(m.Moment, a =>
            {
                if (m.What == 0)
                {
                    a.AccountsAdded += m.Amount;
                }
                else if (m.What == 1)
                {
                    a.AccountsReturned += m.Amount;
                }
            });
        }

        // ---- busy time ----
        var busy = Busy(data.Segments, now).ToList();
        var usageEnd = Min(to, now);
        var prevEnd = Min(prevTo, now);
        // Before GameClass's first record (a new club, or "Clear statistics") nobody can have been counted busy:
        // those hours must not dilute occupancy.
        var usageStart = Clamp(data.HistoryFrom, from, usageEnd);
        var prevStart = Clamp(data.HistoryFrom, prevFrom, prevEnd);
        double BusyMinutes(DateTime a, DateTime b) => busy.Sum(i => Overlap(i.Start, i.End, a, b));

        var busyNow = BusyMinutes(from, usageEnd);
        var busyPrev = BusyMinutes(prevFrom, prevEnd);
        var (peak, peakAt) = Peak(busy, from, usageEnd);

        var bucketBusy = new double[buckets.Count];
        foreach (var i in busy)
        {
            for (var k = Math.Max(0, BucketIndex(buckets, Max(i.Start, from))); k < buckets.Count && buckets[k].Start < Min(i.End, usageEnd); k++)
            {
                bucketBusy[k] += Overlap(i.Start, i.End, buckets[k].Start, Min(buckets[k].End, usageEnd));
            }
        }

        // ---- sessions ----
        var sessions = Sessions(data.Segments, now);
        var inWindow = sessions.Where(s => s.Start >= from && s.Start < to && s.Status != 0).ToList();
        var inPrev = sessions.Count(s => s.Start >= prevFrom && s.Start < prevTo && s.Status != 0);
        var bucketSessions = new int[buckets.Count];
        foreach (var s in inWindow)
        {
            bucketSessions[BucketIndex(buckets, s.Start)]++;
        }

        var paid = inWindow.Where(s => s.CommonPay > 0).ToList();
        var sessionSummary = new SessionSummary(
            inWindow.Count,
            Round(inWindow.Count == 0 ? 0 : inWindow.Average(s => (s.End - s.Start).TotalMinutes)),
            paid.Count == 0 ? 0 : Math.Round(paid.Average(s => s.CommonPay), 0),
            inWindow.Count(s => s.IsAccount),
            inWindow.Count(s => s.ToResolve));

        var bookings = new BookingSummary(
            sessions.Count(s => s.Status == 0 && s.Start >= now),
            inWindow.Count(s => s.Booked),
            data.Segments.Count(s => s.Status == 2 && s.Stop <= s.Start && s.Start >= from && s.Start < to));

        // ---- per computer, zone, tariff, operator ----
        var windowMinutes = (usageEnd - usageStart).TotalMinutes;
        var started = data.Segments.Where(s => s.Status != 0 && s.Stop > s.Start && s.Start >= from && s.Start < to).ToList();
        var pcs = data.Computers
            .Select(c =>
            {
                var mine = busy.Where(i => i.ComputerId == c.Id).ToList();
                var minutes = mine.Sum(i => Overlap(i.Start, i.End, from, usageEnd));
                var parts = started.Where(s => s.ComputerId == c.Id).ToList();
                DateTime? last = mine.Where(i => i.Start < usageEnd && i.End > from).Select(i => Min(i.End, usageEnd)).DefaultIfEmpty().Max();
                return (Computer: c, Stat: new PcStat(Math.Abs(c.Number), c.Zone, Hours(minutes),
                    Ratio(minutes, windowMinutes), parts.Select(s => s.SessionId).Distinct().Count(),
                    parts.Sum(s => s.TimeCost), last == default(DateTime) ? null : last));
            })
            .Where(p => p.Computer.Number > 0 || p.Stat.BusyHours > 0)
            .Select(p => p.Stat)
            .OrderBy(p => p.Number)
            .ToList();

        var zones = data.Computers.Where(c => c.Number > 0).GroupBy(c => c.Zone)
            .Select(g =>
            {
                var ids = g.Select(c => c.Id).ToHashSet();
                var minutes = busy.Where(i => ids.Contains(i.ComputerId)).Sum(i => Overlap(i.Start, i.End, from, usageEnd));
                return new ZoneStat(g.Key, g.Count(), Hours(minutes), Ratio(minutes, windowMinutes * g.Count()),
                    started.Where(s => ids.Contains(s.ComputerId)).Sum(s => s.TimeCost));
            })
            .OrderByDescending(z => z.BusyHours)
            .ToList();

        var tariffs = started.GroupBy(s => s.TariffId)
            .Select(g => new TariffStat(
                data.Tariffs.TryGetValue(g.Key, out var name) && name.Length > 0 ? name : g.Key == 0 ? "" : $"#{g.Key}",
                g.Select(s => s.SessionId).Distinct().Count(),
                Hours(busy.Where(i => i.TariffId == g.Key).Sum(i => Overlap(i.Start, i.End, from, usageEnd))),
                g.Sum(s => s.TimeCost)))
            .OrderByDescending(t => t.TimeValue)
            .ToList();

        var barInWindow = data.BarSales.Where(b => b.Moment >= from && b.Moment < to).ToList();
        var bar = barInWindow.GroupBy(b => b.ItemName.Length > 0 ? b.ItemName : $"#{b.ItemId}")
            .Select(g => new BarItemStat(g.Key, g.Sum(b => b.Quantity), g.Sum(b => b.Quantity * b.UnitPrice),
                g.Where(b => b.TypeCost == 1).Sum(b => b.Quantity * b.UnitPrice),
                g.Where(b => b.TypeCost != 1 && !b.IsAccount).Sum(b => b.Quantity * b.UnitPrice),
                g.Where(b => b.TypeCost != 1 && b.IsAccount).Sum(b => b.Quantity * b.UnitPrice)))
            .OrderByDescending(b => b.Total)
            .ToList();

        string Staff(int id) => id == 0 ? "—" : data.Operators.TryGetValue(id, out var n) && n.Length > 0 ? n : $"#{id}";
        var operatorIds = inWindow.Select(s => s.OperatorId).Concat(barInWindow.Select(b => b.OperatorId)).Distinct();
        var operators = operatorIds
            .Select(id => new OperatorStat(Staff(id),
                inWindow.Count(s => s.OperatorId == id),
                started.Where(s => s.OperatorId == id).Sum(s => s.TimeCost),
                barInWindow.Where(b => b.OperatorId == id).Sum(b => b.Quantity * b.UnitPrice)))
            .OrderByDescending(o => o.TimeValue + o.Bar)
            .ToList();

        var shifts = data.Shifts.Where(s => s.Moment >= from && s.Moment < to)
            .OrderByDescending(s => s.Moment)
            .Select(s => new ShiftStat(s.Moment, Staff(s.OperatorId), s.Total, s.Removed))
            .ToList();

        var debts = inWindow.Where(s => s.ToResolve)
            .Select(s => new DebtStat(computers.TryGetValue(s.ComputerId, out var c) ? Math.Abs(c.Number) : 0, s.End, s.CommonPay))
            .OrderByDescending(d => d.At)
            .ToList();

        var series = buckets.Select((b, k) =>
        {
            var m = perBucket[k].Result();
            return new PeriodPoint(b.Key, b.Start, m.Kassa, m.PcTime, m.Bar, Hours(bucketBusy[k]), bucketSessions[k]);
        }).ToList();

        var money = total.Result();
        var prevMoney = previous.Result();
        return new AnalyticsReport(
            query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            query.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            group,
            query.DayStartHour,
            from,
            to,
            now,
            data.Currency,
            data.HistoryFrom,
            money,
            new UsageSummary(Hours(busyNow), Ratio(busyNow, windowMinutes * capacity), capacity, peak, peakAt),
            sessionSummary,
            bookings,
            new KpiCompare(prevMoney.Kassa, prevMoney.PcTime, prevMoney.Bar, Hours(busyPrev),
                Ratio(busyPrev, (prevEnd - prevStart).TotalMinutes * capacity), inPrev),
            series,
            pcs,
            zones,
            tariffs,
            bar,
            operators,
            shifts,
            debts,
            Heatmap(busy, usageStart, usageEnd));
    }

    /// <summary>One business day as a timeline per computer: who sat where, from when to when.</summary>
    public static AnalyticsDay Day(DateOnly date, int dayStartHour, AnalyticsData data)
    {
        var from = date.ToDateTime(new TimeOnly(dayStartHour, 0));
        var to = from.AddDays(1);
        var now = data.Now;
        var segments = data.Segments.Where(s => s.Start < to && (s.Stop > from || s.Status == 0) && (s.Stop > s.Start || s.Status == 0)).ToList();
        var pcs = data.Computers
            .Select(c =>
            {
                var blocks = segments.Where(s => s.ComputerId == c.Id)
                    .Select(s => new TimelineBlock(
                        Max(s.Start, from),
                        Min(s.Stop > s.Start ? s.Stop : s.Start.AddMinutes(30), to),
                        "",
                        s.TimeCost,
                        IsBooked(s),
                        s.Status == 1 && s.Start <= now && s.Stop > now,
                        s.IsAccount,
                        s.Status == 0) with { Tariff = data.Tariffs.TryGetValue(s.TariffId, out var t) ? t : "" })
                    .OrderBy(b => b.Start)
                    .ToList();
                var minutes = segments.Where(s => s.ComputerId == c.Id && s.Status != 0)
                    .Sum(s => Overlap(s.Start, s.Status == 1 ? Min(s.Stop, now) : s.Stop, from, Min(to, now)));
                return new TimelinePc(Math.Abs(c.Number), c.Zone, Hours(minutes), blocks);
            })
            .Where(p => p.Blocks.Count > 0 || data.Computers.Any(c => Math.Abs(c.Number) == p.Number && c.Number > 0))
            .OrderBy(p => p.Number)
            .ToList();
        return new AnalyticsDay(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from, to, now, pcs);
    }

    /// <summary>
    /// One business day in additive form for Manajoy (see <see cref="DayRollup"/>). Money, bar, staff, shifts and
    /// debts come from <see cref="Build"/> itself, so the phone shows exactly what this window shows.
    /// </summary>
    public static DayRollup Rollup(DateOnly day, int dayStartHour, AnalyticsData data)
    {
        var query = new AnalyticsQuery(day, day, "hour", dayStartHour);
        var report = Build(query, data);
        var (from, to) = Window(query);
        var now = data.Now;
        var usageEnd = Min(to, now);
        var usageStart = Clamp(data.HistoryFrom, from, usageEnd);
        var live = data.Computers.Where(c => c.Number > 0).ToList();
        var busy = Busy(data.Segments, now).Where(i => i.Start < usageEnd && i.End > from).ToList();
        double Minutes(IEnumerable<Interval> intervals) => Math.Round(intervals.Sum(i => Overlap(i.Start, i.End, from, usageEnd)), 2);

        var hourly = new double[24];
        for (var slot = from; slot < usageEnd; slot = slot.AddHours(1))
        {
            hourly[slot.Hour] += busy.Sum(i => Overlap(i.Start, i.End, slot, Min(slot.AddHours(1), usageEnd)));
        }

        var hourlyMoney = new RollupHour[24];
        foreach (var point in report.Series)
        {
            hourlyMoney[point.Start.Hour] = new RollupHour(point.Kassa, point.PcTime, point.Bar, point.Sessions);
        }

        var sessions = Sessions(data.Segments, now).Where(s => s.Start >= from && s.Start < to && s.Status != 0).ToList();
        var paid = sessions.Where(s => s.CommonPay > 0).ToList();
        var started = data.Segments.Where(s => s.Status != 0 && s.Stop > s.Start && s.Start >= from && s.Start < to).ToList();

        var pcs = data.Computers
            .Select(c =>
            {
                var mine = busy.Where(i => i.ComputerId == c.Id).ToList();
                var parts = started.Where(s => s.ComputerId == c.Id).ToList();
                return new RollupPc(Math.Abs(c.Number), c.Zone, Minutes(mine), parts.Select(s => s.SessionId).Distinct().Count(),
                    parts.Sum(s => s.TimeCost), mine.Count == 0 ? null : mine.Max(i => Min(i.End, usageEnd)));
            })
            .Where(p => data.Computers.Any(c => c.Number == p.Number) || p.BusyMinutes > 0)
            .OrderBy(p => p.Number)
            .ToList();

        var zones = live.GroupBy(c => c.Zone)
            .Select(g =>
            {
                var ids = g.Select(c => c.Id).ToHashSet();
                return new RollupZone(g.Key, g.Count(), Minutes(busy.Where(i => ids.Contains(i.ComputerId))),
                    started.Where(s => ids.Contains(s.ComputerId)).Sum(s => s.TimeCost));
            })
            .OrderBy(z => z.Zone, StringComparer.Ordinal)
            .ToList();

        var tariffs = started.GroupBy(s => s.TariffId)
            .Select(g => new RollupTariff(
                data.Tariffs.TryGetValue(g.Key, out var name) && name.Length > 0 ? name : g.Key == 0 ? "" : $"#{g.Key}",
                g.Select(s => s.SessionId).Distinct().Count(),
                Minutes(busy.Where(i => i.TariffId == g.Key)),
                g.Sum(s => s.TimeCost)))
            .OrderByDescending(t => t.TimeValue)
            .ToList();

        var timeline = Day(day, dayStartHour, data).Pcs
            .SelectMany(p => p.Blocks.Select(b => new RollupBlock(p.Number, b.Start, b.End, b.Tariff, b.TimeValue, b.Booked, b.Account, b.Reserved, b.Active)))
            .ToList();

        var rollup = new DayRollup(
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to <= now,
            report.Money,
            new RollupUsage(Minutes(busy), Math.Round(Math.Max(0, (usageEnd - usageStart).TotalMinutes) * live.Count, 2), live.Count,
                report.Usage.PeakBusy, report.Usage.PeakAt),
            hourly.Select(m => Math.Round(m, 2)).ToList(),
            hourlyMoney.Select(h => h ?? new RollupHour(0, 0, 0, 0)).ToList(),
            new RollupSessions(sessions.Count, Math.Round(sessions.Sum(s => (s.End - s.Start).TotalMinutes), 2), paid.Count,
                paid.Sum(s => s.CommonPay), sessions.Count(s => s.IsAccount), sessions.Count(s => s.ToResolve)),
            new RollupBookings(report.Bookings.Past, report.Bookings.Cancelled),
            pcs,
            zones,
            tariffs,
            report.Bar,
            report.Operators,
            report.Shifts,
            report.Debts,
            timeline);
        return rollup with { Fingerprint = Fingerprint(rollup) };
    }

    /// <summary>Same day, same numbers → same fingerprint; the bridge sends a day again only when it changed.</summary>
    private static string Fingerprint(DayRollup rollup) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rollup with { Fingerprint = "" }, Hosting.BridgeJson.SnakeCase)))[..32].ToLowerInvariant();

    // ------------------------------------------------------------------ helpers

    private sealed class MoneyAcc
    {
        public decimal GuestMoney;
        public decimal Internet;
        public decimal Print;
        public decimal BarCash;
        public decimal BarInSessions;
        public decimal BarFromAccounts;
        public decimal AccountsAdded;
        public decimal AccountsReturned;

        public MoneySummary Result()
        {
            // GameClass "Time" = guest session money − internet; bar sold inside sessions is part of it.
            var time = GuestMoney - Internet;
            var pcTime = Math.Max(0m, time - BarInSessions);
            var kassa = time + BarCash + Print + Internet + AccountsAdded - AccountsReturned;
            return new MoneySummary(kassa, pcTime, BarCash + BarInSessions + BarFromAccounts, BarCash, BarInSessions,
                BarFromAccounts, Print, Internet, AccountsAdded, AccountsReturned);
        }
    }

    private sealed record Bucket(string Key, DateTime Start, DateTime End);

    private sealed record Interval(int ComputerId, int TariffId, DateTime Start, DateTime End);

    private sealed record Session(int SessionId, int ComputerId, DateTime Start, DateTime End, int Status, decimal CommonPay,
        bool IsAccount, bool ToResolve, int OperatorId, bool Booked);

    private static bool IsBooked(SegmentRow s) => s.Status == 0 || (s.FirstMoney is { } paidAt && paidAt <= s.Start - BookedLead);

    /// <summary>Time a computer was really in use: started, not a cancelled reservation, cut at "now".</summary>
    private static IEnumerable<Interval> Busy(IEnumerable<SegmentRow> segments, DateTime now) =>
        segments.Where(s => s.Status != 0 && s.Stop > s.Start && s.Start < now)
            .Select(s => new Interval(s.ComputerId, s.TariffId, s.Start, s.Status == 1 ? Min(s.Stop, now) : s.Stop))
            .Where(i => i.End > i.Start);

    private static IEnumerable<SegmentRow> FirstSegments(IEnumerable<SegmentRow> segments) =>
        segments.GroupBy(s => s.SessionId).Select(g => g.OrderBy(s => s.Start).First());

    private static List<Session> Sessions(IEnumerable<SegmentRow> segments, DateTime now) =>
        segments.Where(s => s.Stop > s.Start || s.Status == 0)
            .GroupBy(s => s.SessionId)
            .Select(g =>
            {
                var first = g.OrderBy(s => s.Start).First();
                var end = g.Max(s => s.Status == 1 ? Min(s.Stop, now) : s.Stop);
                return new Session(g.Key, first.ComputerId, first.Start, Max(end, first.Start), first.Status, first.CommonPay,
                    first.IsAccount, first.ToResolve, first.OperatorId, IsBooked(first));
            })
            .ToList();

    private static List<Bucket> Buckets(string group, DateTime from, DateTime to, int dayStartHour)
    {
        var list = new List<Bucket>();
        var cursor = from;
        while (cursor < to)
        {
            var businessDate = DateOnly.FromDateTime(cursor.AddHours(-dayStartHour));
            DateTime next;
            string key;
            switch (group)
            {
                case "hour":
                    next = cursor.AddHours(1);
                    key = cursor.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);
                    break;
                case "week":
                    var monday = businessDate.AddDays(-(((int)businessDate.DayOfWeek + 6) % 7));
                    next = monday.AddDays(7).ToDateTime(new TimeOnly(dayStartHour, 0));
                    key = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
                case "month":
                    var first = new DateOnly(businessDate.Year, businessDate.Month, 1);
                    next = first.AddMonths(1).ToDateTime(new TimeOnly(dayStartHour, 0));
                    key = first.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    break;
                default:
                    next = businessDate.AddDays(1).ToDateTime(new TimeOnly(dayStartHour, 0));
                    key = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
            }

            next = Min(next, to);
            list.Add(new Bucket(key, cursor, next));
            cursor = next;
        }

        return list;
    }

    private static int BucketIndex(List<Bucket> buckets, DateTime at)
    {
        int lo = 0, hi = buckets.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (at < buckets[mid].Start)
            {
                hi = mid - 1;
            }
            else if (at >= buckets[mid].End)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return Math.Clamp(lo, 0, buckets.Count - 1);
    }

    private static (int Peak, DateTime? At) Peak(IEnumerable<Interval> busy, DateTime from, DateTime to)
    {
        var edges = new List<(DateTime At, int Delta)>();
        foreach (var i in busy)
        {
            var s = Max(i.Start, from);
            var e = Min(i.End, to);
            if (e > s)
            {
                edges.Add((s, 1));
                edges.Add((e, -1));
            }
        }

        // Ends before starts at the same moment: a PC handed from one session to the next is one PC, not two.
        var current = 0;
        var peak = 0;
        DateTime? at = null;
        foreach (var edge in edges.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            current += edge.Delta;
            if (current > peak)
            {
                peak = current;
                at = edge.At;
            }
        }

        return (peak, at);
    }

    /// <summary>Average number of busy computers per weekday (Mon = 0) and hour.</summary>
    private static IReadOnlyList<double[]> Heatmap(IEnumerable<Interval> busy, DateTime from, DateTime to)
    {
        var minutes = new double[7, 24];
        var slots = new int[7, 24];
        for (var h = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0); h < to; h = h.AddHours(1))
        {
            slots[Weekday(h), h.Hour]++;
        }

        foreach (var i in busy)
        {
            var s = Max(i.Start, from);
            var e = Min(i.End, to);
            for (var h = new DateTime(s.Year, s.Month, s.Day, s.Hour, 0, 0); h < e; h = h.AddHours(1))
            {
                minutes[Weekday(h), h.Hour] += Overlap(s, e, h, h.AddHours(1));
            }
        }

        return Enumerable.Range(0, 7)
            .Select(d => Enumerable.Range(0, 24).Select(h => slots[d, h] == 0 ? 0 : Math.Round(minutes[d, h] / (slots[d, h] * 60.0), 2)).ToArray())
            .ToList();
    }

    private static int Weekday(DateTime at) => ((int)at.DayOfWeek + 6) % 7;

    private static double Overlap(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var s = Max(aStart, bStart);
        var e = Min(aEnd, bEnd);
        return e > s ? (e - s).TotalMinutes : 0;
    }

    private static double Hours(double minutes) => Math.Round(minutes / 60.0, 2);

    private static double Ratio(double part, double whole) => whole <= 0 ? 0 : Math.Round(part / whole, 4);

    private static double Round(double value) => Math.Round(value, 1);

    private static DateTime Clamp(DateTime? value, DateTime low, DateTime high) =>
        value is { } v && v > low ? Min(v, high) : low;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
