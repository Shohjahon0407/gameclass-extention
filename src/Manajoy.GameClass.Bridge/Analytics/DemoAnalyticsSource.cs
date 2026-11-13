namespace Manajoy.GameClass.Bridge.Analytics;

/// <summary>
/// --setup-demo: four months of believable club history (24 PCs, evening and night peaks, busier weekends, bar,
/// accounts, shifts, a few bookings and debts). Same seed every run, so screenshots stay comparable.
/// </summary>
internal sealed class DemoAnalyticsSource(IReadOnlyList<string> zones, Func<DateTime>? clock = null) : IAnalyticsSource, Bar.IBarSource
{
    public async Task<Bar.BarData> LoadBarAsync(DateTime? salesFrom, CancellationToken cancellationToken)
    {
        var now = _clock();
        var from = salesFrom ?? now;
        var data = await LoadAsync(from, now.AddMinutes(1), cancellationToken);
        var catalog = Items.Select(i => new Bar.BarCatalogItem(i.Id, i.Name, i.Price, false))
            .Append(new Bar.BarCatalogItem(0, "Связь с оператором", 3, false))
            .ToList();
        return new Bar.BarData(data.Now, data.Currency, catalog,
            data.BarSales.Where(b => b.Moment >= from).Select(b => new Bar.BarSale(b.ItemId, b.Moment, b.Quantity)).ToList());
    }

    private const int HistoryDays = 120;

    // How likely a free PC is taken in each hour (index = hour of day).
    private static readonly double[] Demand =
    [
        0.55, 0.45, 0.35, 0.22, 0.12, 0.06, 0.04, 0.05, 0.07, 0.10, 0.16, 0.22,
        0.28, 0.32, 0.36, 0.40, 0.48, 0.58, 0.68, 0.76, 0.80, 0.78, 0.72, 0.64,
    ];

    private static readonly (int Id, string Name, int Price)[] Items =
    [
        (1, "Coca-Cola 0.5", 8000), (2, "Flash", 12000), (3, "Qahva", 5000), (4, "Choy", 3000),
        (5, "Lays", 10000), (6, "Snickers", 9000), (7, "Suv 0.5", 4000), (8, "Burger", 25000),
    ];

    private static readonly IReadOnlyDictionary<int, string> Operators = new Dictionary<int, string>
    {
        [1] = "Admin", [2] = "Kassir 1", [3] = "Kassir 2",
    };

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private AnalyticsData? _all;

    public Task<AnalyticsData> LoadAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var all = _all ??= Generate(_clock());
        return Task.FromResult(all with
        {
            Segments = all.Segments.Where(s => s.Start < to && (s.Stop > from || s.Status == 0)).ToList(),
            MoneyEvents = all.MoneyEvents.Where(m => m.Moment >= from && m.Moment < to).ToList(),
            BarSales = all.BarSales.Where(b => b.Moment >= from && b.Moment < to).ToList(),
            AccountMoney = all.AccountMoney.Where(a => a.Moment >= from && a.Moment < to).ToList(),
            Shifts = all.Shifts.Where(s => s.Moment >= from && s.Moment < to).ToList(),
        });
    }

    private AnalyticsData Generate(DateTime now)
    {
        var random = new Random(26);
        now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        var computers = zones.Select((zone, i) => new AnalyticsComputer(100 + i, i + 1, zone)).ToList();
        var tariffs = new Dictionary<int, string>
        {
            [1] = "Standart soat", [2] = "VIP soat", [3] = "Bootcamp soat", [4] = "PlayStation soat", [5] = "Tungi paket",
        };

        var segments = new List<SegmentRow>();
        var money = new List<MoneyEventRow>();
        var bar = new List<BarSaleRow>();
        var accounts = new List<AccountMoneyRow>();
        var shifts = new List<ShiftRow>();
        var id = 1;
        var historyFrom = now.Date.AddDays(-HistoryDays).AddHours(6);

        foreach (var pc in computers)
        {
            var (tariff, price) = pc.Zone switch
            {
                "VIP" => (2, 15000),
                "Bootcamp" => (3, 12000),
                "PlayStation" => (4, 20000),
                _ => (1, 10000),
            };

            var cursor = historyFrom.AddMinutes(random.Next(0, 240));
            while (cursor < now.AddHours(30))
            {
                var weekend = cursor.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
                var chance = Math.Min(0.95, Demand[cursor.Hour] * (weekend ? 1.25 : 1.0) * (pc.Zone == "VIP" ? 0.85 : 1.0));
                if (random.NextDouble() > chance)
                {
                    cursor = cursor.AddMinutes(random.Next(10, 40));
                    continue;
                }

                var night = cursor.Hour is >= 22 or < 2 && random.NextDouble() < 0.25;
                var minutes = night ? 8 * 60 : random.Next(2, 13) * 15 + random.Next(0, 10);
                var start = cursor;
                var stop = start.AddMinutes(minutes);
                var cost = night ? 50000m : Math.Round(price * minutes / 60m / 500m) * 500m;
                var sessionId = id++;
                var account = random.NextDouble() < 0.22;
                var postpay = !account && random.NextDouble() < 0.08;
                var booked = random.NextDouble() < 0.07;
                var future = start > now;
                if (future && !booked)
                {
                    break;
                }

                var status = future ? 0 : stop > now ? 1 : 2;
                var operatorId = start.Hour is >= 9 and < 21 ? 2 : 3;
                var paidAt = booked ? start.AddMinutes(-random.Next(20, 180)) : start;
                var debt = postpay && random.NextDouble() < 0.15;
                decimal service = 0;

                if (status != 0)
                {
                    if (!account && !postpay)
                    {
                        money.Add(new MoneyEventRow(sessionId, paidAt, cost, 0, false, false));
                    }

                    for (var n = random.NextDouble() < 0.55 ? 0 : random.Next(1, 3); n > 0; n--)
                    {
                        var item = Items[random.Next(Items.Length)];
                        var at = start.AddMinutes(random.Next(5, Math.Max(6, minutes - 5)));
                        if (at > now)
                        {
                            continue;
                        }

                        var count = random.NextDouble() < 0.2 ? 2 : 1;
                        var type = account ? 2 : random.NextDouble() < 0.7 ? 1 : postpay ? 3 : 2;
                        bar.Add(new BarSaleRow(item.Id, item.Name, count, item.Price, type, at, pc.Id, account && type != 1, operatorId));
                        if (type != 1)
                        {
                            service += count * item.Price;
                            if (!account && !postpay)
                            {
                                money.Add(new MoneyEventRow(sessionId, at, count * item.Price, 2, false, false));
                            }
                        }
                    }
                }

                segments.Add(new SegmentRow(sessionId, id++, pc.Id, night ? 5 : tariff, start, stop, cost, 0m, status, postpay, debt,
                    cost + service, service, random.NextDouble() < 0.03 ? 2000m : 0m, operatorId, account, status == 0 ? null : paidAt));
                if (segments[^1].PrintCost > 0 && !account && !postpay && status != 0)
                {
                    money.Add(new MoneyEventRow(sessionId, start, segments[^1].PrintCost, 2, false, false));
                }

                cursor = stop.AddMinutes(random.Next(2, 45));
            }
        }

        // Account top-ups at the counter and the twice-a-day shift close.
        for (var day = historyFrom.Date; day <= now.Date; day = day.AddDays(1))
        {
            for (var n = random.Next(2, 7); n > 0; n--)
            {
                var at = day.AddHours(random.Next(10, 23)).AddMinutes(random.Next(60));
                if (at < now)
                {
                    accounts.Add(new AccountMoneyRow(0, random.Next(2, 11) * 10000m, at, at.Hour < 21 ? 2 : 3));
                }
            }

            if (random.NextDouble() < 0.1)
            {
                var at = day.AddHours(random.Next(12, 22));
                if (at < now)
                {
                    accounts.Add(new AccountMoneyRow(1, random.Next(1, 4) * 10000m, at, 2));
                }
            }
        }

        var shiftStart = historyFrom.Date.AddHours(9);
        for (var close = shiftStart.AddHours(12); close < now; close = close.AddHours(12))
        {
            var from = close.AddHours(-12);
            var total = money.Where(m => m.Moment >= from && m.Moment < close).Sum(m => m.Amount)
                + bar.Where(b => b.TypeCost == 1 && b.Moment >= from && b.Moment < close).Sum(b => b.Quantity * b.UnitPrice)
                + accounts.Where(a => a.Moment >= from && a.Moment < close).Sum(a => a.What == 0 ? a.Amount : -a.Amount);
            shifts.Add(new ShiftRow(close.AddMinutes(random.Next(0, 15)), total, Math.Floor(total * 0.9m / 10000m) * 10000m, close.Hour == 21 ? 2 : 3));
        }

        return new AnalyticsData(now, "UZS", historyFrom, computers, tariffs, Operators, segments, money, bar, accounts, shifts);
    }
}
