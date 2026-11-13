using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Bar;

public sealed record BarEntryRequest(int ItemId, string? Kind, int Qty, string? Note);

public sealed record BarItemRequest(int ItemId, int? LowThreshold, bool? Tracked);

/// <summary>One item's book: its lines and how many were sold each day since it is tracked.</summary>
public sealed record BarHistory(BarStockItem? Item, IReadOnlyList<BarEntry> Entries, IReadOnlyList<BarDaySales> Sales);

public sealed record BarDaySales(string Date, int Quantity);

public sealed record BarRestoreResult(int Restored, bool HadLocal);

/// <summary>What the hall page may show without the PIN: only items running low (no money).</summary>
public sealed record BarAlert(int ItemId, string Name, int Remaining, string State);

public sealed record BarBackup(IReadOnlyList<BarEntry> Entries, IReadOnlyList<BarItemSettings> Items);

/// <summary>The setup window's «Bar» page: GameClass's price list and sales (read-only) against the owner's stock book.</summary>
internal sealed class BarService(Func<IBarSource> source, BarStore store)
{
    private static readonly TimeSpan AlertsCacheFor = TimeSpan.FromSeconds(60);
    private (DateTime At, IReadOnlyList<BarAlert> Alerts)? _alerts;

    /// <summary>Low / empty tracked items for the hall (polled; GameClass is read at most once a minute).</summary>
    public async Task<IReadOnlyList<BarAlert>> AlertsAsync(int dayStartHour, CancellationToken cancellationToken)
    {
        if (_alerts is { } cached && DateTime.UtcNow - cached.At < AlertsCacheFor)
        {
            return cached.Alerts;
        }

        IReadOnlyList<BarAlert> alerts = [];
        if (store.Load().Entries.Count > 0)
        {
            alerts = (await OverviewAsync(dayStartHour, cancellationToken)).Items
                .Where(i => i.Tracked && i.State is BarStockCalculator.StateLow or BarStockCalculator.StateOut or BarStockCalculator.StateNegative)
                .Select(i => new BarAlert(i.ItemId, i.Name, i.Remaining ?? 0, i.State))
                .ToList();
        }

        _alerts = (DateTime.UtcNow, alerts);
        return alerts;
    }

    public async Task<BarOverview> OverviewAsync(int dayStartHour, CancellationToken cancellationToken)
    {
        var ledger = store.Load();
        var data = await LoadAsync(ledger.SalesFrom(), dayStartHour, cancellationToken);
        return BarStockCalculator.Compute(data, ledger, dayStartHour);
    }

    public void Changed() => _alerts = null;

    public async Task<BarOverview> AddAsync(BarEntryRequest request, int dayStartHour, CancellationToken cancellationToken)
    {
        // The item and the moment come from GameClass itself: same clock as its sales, real product name.
        var data = await LoadAsync(null, dayStartHour, cancellationToken);
        var item = data.Catalog.FirstOrDefault(c => c.Id == request.ItemId && c.Id != 0)
            ?? throw new SetupException("bar_item", request.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        store.Add(item.Id, item.Name, request.Kind, request.Qty, request.Note, data.Now);
        Changed();
        return await OverviewAsync(dayStartHour, cancellationToken);
    }

    public async Task<BarOverview> SetItemAsync(BarItemRequest request, int dayStartHour, CancellationToken cancellationToken)
    {
        store.SetItem(request.ItemId, request.LowThreshold, request.Tracked);
        Changed();
        return await OverviewAsync(dayStartHour, cancellationToken);
    }

    public async Task<BarHistory> HistoryAsync(int itemId, int dayStartHour, CancellationToken cancellationToken)
    {
        var ledger = store.Load();
        var anchor = ledger.Anchor(itemId);
        var data = await LoadAsync(anchor, dayStartHour, cancellationToken);
        var overview = BarStockCalculator.Compute(data, ledger, dayStartHour);
        var sales = data.Sales.Where(s => s.ItemId == itemId && (anchor is null || s.Moment > anchor))
            .GroupBy(s => DateOnly.FromDateTime(s.Moment.AddHours(-dayStartHour)))
            .OrderByDescending(g => g.Key)
            .Take(120)
            .Select(g => new BarDaySales(g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), g.Sum(s => s.Quantity)))
            .ToList();
        return new BarHistory(overview.Items.FirstOrDefault(i => i.ItemId == itemId),
            ledger.Entries.Where(e => e.ItemId == itemId).OrderByDescending(e => e.At).Take(200).ToList(), sales);
    }

    private async Task<BarData> LoadAsync(DateTime? salesFrom, int dayStartHour, CancellationToken cancellationToken)
    {
        // "Sold today / 7 days" is shown for untracked items too: always read at least the last week.
        try
        {
            var data = await source().LoadBarAsync(null, cancellationToken);
            var weekAgo = data.Now.Date.AddHours(dayStartHour).AddDays(-7);
            var from = salesFrom is { } anchor && anchor < weekAgo ? anchor : weekAgo;
            return await source().LoadBarAsync(from, cancellationToken);
        }
        catch (Exception ex) when (ex is Data.GameClassUnavailableException or System.Data.Common.DbException)
        {
            throw new SetupException("analytics_db", ex.Message, ex);
        }
    }
}
