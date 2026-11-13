using Manajoy.GameClass.Bridge.Data;
using Manajoy.GameClass.Bridge.GameClass;

namespace Manajoy.GameClass.Bridge.Analytics;

/// <summary>Reads one window of GameClass history over a single read-only connection.</summary>
internal sealed class AnalyticsReader(ReadOnlyGameClassDb db) : IAnalyticsSource, Bar.IBarSource
{
    public Task<Bar.BarData> LoadBarAsync(DateTime? salesFrom, CancellationToken cancellationToken) =>
        db.WithConnectionAsync(async scope =>
        {
            var now = (await scope.QueryAsync(GameClassQueries.ServerNow,
                r => r.GetDateTimeOrNull("ServerNow") ?? DateTime.Now, cancellationToken))[0];
            var settings = await scope.QueryAsync(GameClassQueries.ClubSettings,
                r => (Key: r.GetStringOrEmpty("SettingKey"), Value: r.GetStringOrNull("SettingValue")), cancellationToken);
            var catalog = await scope.QueryAsync(AnalyticsQueries.BarCatalog, r => new Bar.BarCatalogItem(
                r.GetInt32("Id"),
                Text(r.GetStringOrNull("Name")),
                r.GetDecimalOrNull("Price") ?? 0m,
                (r.GetInt32OrNull("IsDeleted") ?? 0) != 0), cancellationToken);
            IReadOnlyList<Bar.BarSale> sales = salesFrom is { } from
                ? await scope.QueryAsync(AnalyticsQueries.BarSalesSince(from), r => new Bar.BarSale(
                    r.GetInt32OrNull("ItemId") ?? 0,
                    r.GetDateTimeOrNull("Moment") ?? DateTime.MinValue,
                    r.GetInt32OrNull("Quantity") ?? 0), cancellationToken)
                : [];
            var currency = settings.FirstOrDefault(s => s.Key.Equals("Currency", StringComparison.OrdinalIgnoreCase)).Value;
            return new Bar.BarData(now, currency, catalog, sales);
        }, cancellationToken);

    public Task<AnalyticsData> LoadAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
        db.WithConnectionAsync(async scope =>
        {
            var now = (await scope.QueryAsync(GameClassQueries.ServerNow,
                r => r.GetDateTimeOrNull("ServerNow") ?? DateTime.Now, cancellationToken))[0];

            var settings = await scope.QueryAsync(GameClassQueries.ClubSettings,
                r => (Key: r.GetStringOrEmpty("SettingKey"), Value: r.GetStringOrNull("SettingValue")), cancellationToken);
            var currency = settings.FirstOrDefault(s => s.Key.Equals("Currency", StringComparison.OrdinalIgnoreCase)).Value;

            var computers = (await scope.QueryAsync(AnalyticsQueries.AllComputers, r => (
                    Computer: new AnalyticsComputer(r.GetInt32("Id"), r.GetInt32("Number"), Text(r.GetStringOrNull("Zone"))),
                    Deleted: (r.GetInt32OrNull("IsDeleted") ?? 0) != 0), cancellationToken))
                .ToList();

            var tariffs = (await scope.QueryAsync(AnalyticsQueries.AllTariffs,
                    r => (Id: r.GetInt32("Id"), Name: Text(r.GetStringOrNull("Name"))), cancellationToken))
                .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Name);

            var staff = (await scope.QueryAsync(AnalyticsQueries.Staff,
                    r => (Id: r.GetInt32("Id"), Name: Text(r.GetStringOrNull("Name"))), cancellationToken))
                .GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);

            var segments = await scope.QueryAsync(AnalyticsQueries.Segments(from, to), r => new SegmentRow(
                r.GetInt32("SessionId"),
                r.GetInt32("SegmentId"),
                r.GetInt32OrNull("ComputerId") ?? 0,
                r.GetInt32OrNull("TariffId") ?? 0,
                r.GetDateTimeOrNull("SegStart") ?? DateTime.MinValue,
                r.GetDateTimeOrNull("SegStop") ?? DateTime.MinValue,
                r.GetDecimalOrNull("TimeCost") ?? 0m,
                r.GetDecimalOrNull("TrafficCost") ?? 0m,
                r.GetInt32OrNull("Status") ?? 0,
                (r.GetInt32OrNull("Postpay") ?? 0) != 0,
                (r.GetInt32OrNull("ToResolve") ?? 0) != 0,
                r.GetDecimalOrNull("CommonPay") ?? 0m,
                r.GetDecimalOrNull("ServiceCost") ?? 0m,
                r.GetDecimalOrNull("PrintCost") ?? 0m,
                r.GetInt32OrNull("OperatorId") ?? 0,
                (r.GetInt32OrNull("IsAccount") ?? 0) != 0,
                r.GetDateTimeOrNull("FirstMoney")), cancellationToken);

            var money = await scope.QueryAsync(AnalyticsQueries.MoneyEvents(from, to), r => new MoneyEventRow(
                r.GetInt32("SessionId"),
                r.GetDateTimeOrNull("Moment") ?? DateTime.MinValue,
                r.GetDecimalOrNull("Summa") ?? 0m,
                r.GetInt32OrNull("ActionType") ?? 0,
                (r.GetInt32OrNull("Postpay") ?? 0) != 0,
                (r.GetInt32OrNull("IsAccount") ?? 0) != 0), cancellationToken);

            var bar = await scope.QueryAsync(AnalyticsQueries.BarSales(from, to), r => new BarSaleRow(
                r.GetInt32OrNull("ItemId") ?? 0,
                Text(r.GetStringOrNull("ItemName")),
                r.GetInt32OrNull("Quantity") ?? 0,
                r.GetDecimalOrNull("UnitPrice") ?? 0m,
                r.GetInt32OrNull("TypeCost") ?? 0,
                r.GetDateTimeOrNull("Moment") ?? DateTime.MinValue,
                r.GetInt32OrNull("ComputerId") ?? 0,
                (r.GetInt32OrNull("IsAccount") ?? 0) != 0,
                r.GetInt32OrNull("OperatorId") ?? 0), cancellationToken);

            var accounts = await scope.QueryAsync(AnalyticsQueries.AccountMoney(from, to), r => new AccountMoneyRow(
                r.GetInt32OrNull("What") ?? -1,
                r.GetDecimalOrNull("Summa") ?? 0m,
                r.GetDateTimeOrNull("Moment") ?? DateTime.MinValue,
                r.GetInt32OrNull("OperatorId") ?? 0), cancellationToken);

            var shifts = await scope.QueryAsync(AnalyticsQueries.Shifts(from, to), r => new ShiftRow(
                r.GetDateTimeOrNull("Moment") ?? DateTime.MinValue,
                r.GetDecimalOrNull("Summa") ?? 0m,
                r.GetDecimalOrNull("Removed") ?? 0m,
                r.GetInt32OrNull("OperatorId") ?? 0), cancellationToken);

            var historyFrom = (await scope.QueryAsync(AnalyticsQueries.HistoryFrom,
                r => r.GetDateTimeOrNull("FirstAt"), cancellationToken)).FirstOrDefault();

            // Only computers that still exist count for occupancy; deleted ones keep their history rows.
            var live = computers.Where(c => !c.Deleted).Select(c => c.Computer).ToList();
            var deleted = computers.Where(c => c.Deleted).Select(c => c.Computer with { Number = -c.Computer.Number });
            return new AnalyticsData(now, currency, historyFrom, live.Concat(deleted).ToList(), tariffs, staff,
                segments, money, bar, accounts, shifts);
        }, cancellationToken);

    private static string Text(string? value) => TextRepair.FixCyrillicMojibake(value ?? string.Empty).Trim();
}
