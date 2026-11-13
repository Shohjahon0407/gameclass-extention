using System.Globalization;

namespace Manajoy.GameClass.Bridge.Bar;

// GameClass's bar («Услуги») has a price list and a sales log but no stock: ServicesBase is id/name/price/isdelete
// and Services records each sale. The owner keeps the stock here instead — counts, deliveries and write-offs —
// and every GameClass sale of a tracked item is subtracted automatically. Nothing is written to GameClass.

/// <summary>ServicesBase row. Id 0 is GameClass's own built-in service, never a product.</summary>
public sealed record BarCatalogItem(int Id, string Name, decimal Price, bool Deleted);

/// <summary>One Services row: how many of an item were sold and when (GameClass server clock).</summary>
public sealed record BarSale(int ItemId, DateTime Moment, int Quantity);

/// <summary>What GameClass says right now: its clock, the price list and the sales since the oldest stock anchor.</summary>
public sealed record BarData(DateTime Now, string? Currency, IReadOnlyList<BarCatalogItem> Catalog, IReadOnlyList<BarSale> Sales);

public interface IBarSource
{
    /// <param name="salesFrom">Sales at or after this moment; null = no sales needed (nothing is tracked yet).</param>
    Task<BarData> LoadBarAsync(DateTime? salesFrom, CancellationToken cancellationToken);
}

public static class BarEntryKind
{
    /// <summary>Counted on the shelf: the stock is exactly <c>Qty</c> at <c>At</c> (the anchor).</summary>
    public const string Count = "count";

    /// <summary>A delivery: +Qty.</summary>
    public const string Receive = "receive";

    /// <summary>Broken, expired, given away: −Qty.</summary>
    public const string WriteOff = "writeoff";

    public static bool IsValid(string? kind) => kind is Count or Receive or WriteOff;
}

/// <summary>A stock book line. Never edited or deleted; a mistake is fixed with a new count or write-off.</summary>
public sealed record BarEntry(string Uid, int ItemId, string ItemName, string Kind, int Qty, DateTime At, string? Note);

/// <param name="LowThreshold">0 = no "running low" warning.</param>
public sealed record BarItemSettings(int ItemId, int LowThreshold, bool Tracked);

public sealed record BarLedger(IReadOnlyList<BarEntry> Entries, IReadOnlyList<BarItemSettings> Items)
{
    public static readonly BarLedger Empty = new([], []);

    /// <summary>Where each tracked item's stock starts: its last count, else its first entry (from zero).</summary>
    public DateTime? Anchor(int itemId)
    {
        var mine = Entries.Where(e => e.ItemId == itemId).ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        return mine.Where(e => e.Kind == BarEntryKind.Count).Select(e => (DateTime?)e.At).Max() ?? mine.Min(e => e.At);
    }

    /// <summary>The oldest moment any tracked item needs sales from.</summary>
    public DateTime? SalesFrom() =>
        Entries.Select(e => e.ItemId).Distinct().Where(IsTracked).Select(Anchor).Where(a => a.HasValue).Min();

    public bool IsTracked(int itemId) =>
        Items.FirstOrDefault(i => i.ItemId == itemId)?.Tracked ?? Entries.Any(e => e.ItemId == itemId);

    public int Threshold(int itemId) => Items.FirstOrDefault(i => i.ItemId == itemId)?.LowThreshold ?? 0;
}

/// <summary>ok / low (≤ threshold) / out (0) / negative (more sold than the book says: count again).</summary>
public sealed record BarStockItem(
    int ItemId,
    string Name,
    decimal Price,
    bool Tracked,
    int? Remaining,
    int LowThreshold,
    string State,
    int SoldToday,
    int Sold7Days,
    int SoldSinceAnchor,
    DateTime? AnchorAt,
    DateTime? LastReceiveAt,
    int? LastReceiveQty);

public sealed record BarOverview(DateTime Now, string? Currency, int Tracked, int Low, int Out, IReadOnlyList<BarStockItem> Items);

public static class BarStockCalculator
{
    public const string StateOk = "ok";
    public const string StateLow = "low";
    public const string StateOut = "out";
    public const string StateNegative = "negative";
    public const string StateUntracked = "untracked";

    public static BarOverview Compute(BarData data, BarLedger ledger, int dayStartHour)
    {
        var today = data.Now.Date.AddHours(dayStartHour);
        if (data.Now < today)
        {
            today = today.AddDays(-1);
        }

        var week = today.AddDays(-6);
        var salesByItem = data.Sales.GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var known = data.Catalog.Where(c => c.Id != 0).ToDictionary(c => c.Id);

        // Items that were deleted in GameClass but still have stock entries stay visible until counted to zero.
        var ids = known.Values.Where(c => !c.Deleted).Select(c => c.Id)
            .Concat(ledger.Entries.Select(e => e.ItemId).Where(id => id != 0))
            .Distinct();

        var items = ids.Select(id =>
            {
                known.TryGetValue(id, out var catalog);
                var name = catalog?.Name is { Length: > 0 } n ? n : ledger.Entries.LastOrDefault(e => e.ItemId == id)?.ItemName ?? $"#{id}";
                var sales = salesByItem.TryGetValue(id, out var list) ? list : [];
                var soldToday = sales.Where(s => s.Moment >= today).Sum(s => s.Quantity);
                var sold7 = sales.Where(s => s.Moment >= week).Sum(s => s.Quantity);
                var threshold = ledger.Threshold(id);
                var tracked = ledger.IsTracked(id) && ledger.Anchor(id) is not null;
                if (!tracked)
                {
                    return new BarStockItem(id, name, catalog?.Price ?? 0, false, null, threshold, StateUntracked, soldToday, sold7, 0,
                        null, null, null);
                }

                var anchorAt = ledger.Anchor(id)!.Value;
                var entries = ledger.Entries.Where(e => e.ItemId == id).ToList();
                var anchor = entries.Where(e => e.Kind == BarEntryKind.Count && e.At == anchorAt).LastOrDefault();
                var after = entries.Where(e => e.At >= anchorAt && !ReferenceEquals(e, anchor)).ToList();
                var soldSince = sales.Where(s => s.Moment > anchorAt).Sum(s => s.Quantity);
                var remaining = (anchor?.Qty ?? 0)
                    + after.Where(e => e.Kind == BarEntryKind.Receive).Sum(e => e.Qty)
                    - after.Where(e => e.Kind == BarEntryKind.WriteOff).Sum(e => e.Qty)
                    - soldSince;
                var lastReceive = entries.Where(e => e.Kind == BarEntryKind.Receive).MaxBy(e => e.At);
                var state = remaining < 0 ? StateNegative
                    : remaining == 0 ? StateOut
                    : threshold > 0 && remaining <= threshold ? StateLow
                    : StateOk;
                return new BarStockItem(id, name, catalog?.Price ?? 0, true, remaining, threshold, state, soldToday, sold7, soldSince,
                    anchorAt, lastReceive?.At, lastReceive?.Qty);
            })
            .Where(i => i.Tracked || (known.TryGetValue(i.ItemId, out var c) && !c.Deleted))
            .OrderBy(i => i.Tracked ? 0 : 1)
            .ThenBy(i => i.State switch { StateNegative => 0, StateOut => 1, StateLow => 2, _ => 3 })
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new BarOverview(data.Now, data.Currency, items.Count(i => i.Tracked), items.Count(i => i.State == StateLow),
            items.Count(i => i.State is StateOut or StateNegative), items);
    }

    /// <summary>Changes only when a number the owner sees changes (the push sends the stock again only then).</summary>
    public static string Fingerprint(BarOverview overview) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|",
            overview.Items.Select(i => string.Create(CultureInfo.InvariantCulture,
                $"{i.ItemId}:{i.Name}:{i.Price}:{i.Tracked}:{i.Remaining}:{i.LowThreshold}:{i.SoldToday}:{i.Sold7Days}:{i.LastReceiveAt:O}")))))
        )[..32].ToLowerInvariant();
}
