using Manajoy.GameClass.Bridge.Bar;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

public sealed class BarStockTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 20, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mj-bar-" + Guid.NewGuid().ToString("N"));

    private static readonly BarCatalogItem[] Catalog =
    [
        new(0, "Связь с оператором", 3, false),
        new(3, "Coca-Cola 0.5", 8000, false),
        new(4, "Pepsi 0.5", 8000, false),
        new(9, "Old snack", 5000, true),
    ];

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static BarEntry Entry(int item, string kind, int qty, DateTime at) =>
        new(Guid.NewGuid().ToString("N"), item, $"#{item}", kind, qty, at, null);

    private static BarData Data(params BarSale[] sales) => new(Now, "so'm", Catalog, sales);

    private static BarStockItem Item(BarOverview overview, int id) => overview.Items.Single(i => i.ItemId == id);

    [Fact]
    public void Count_plus_deliveries_minus_write_offs_minus_gameclass_sales()
    {
        var ledger = new BarLedger([
            Entry(3, BarEntryKind.Count, 100, Now.AddDays(-3)),
            Entry(3, BarEntryKind.Receive, 24, Now.AddDays(-1)),
            Entry(3, BarEntryKind.WriteOff, 2, Now.AddHours(-5)),
        ], []);
        var data = Data(
            new BarSale(3, Now.AddDays(-4), 50),           // before the count: already on the shelf count
            new BarSale(3, Now.AddDays(-2), 3),
            new BarSale(3, Now.AddHours(-1), 2),
            new BarSale(4, Now.AddHours(-1), 7));          // another item

        var coke = Item(BarStockCalculator.Compute(data, ledger, 6), 3);

        Assert.True(coke.Tracked);
        Assert.Equal(100 + 24 - 2 - 5, coke.Remaining);
        Assert.Equal(5, coke.SoldSinceAnchor);
        Assert.Equal(2, coke.SoldToday);
        Assert.Equal(55, coke.Sold7Days);
        Assert.Equal(BarStockCalculator.StateOk, coke.State);
        Assert.Equal(24, coke.LastReceiveQty);
    }

    [Fact]
    public void A_new_count_restarts_the_book()
    {
        var ledger = new BarLedger([
            Entry(3, BarEntryKind.Count, 100, Now.AddDays(-5)),
            Entry(3, BarEntryKind.Receive, 50, Now.AddDays(-4)),
            Entry(3, BarEntryKind.Count, 30, Now.AddDays(-1)),
            Entry(3, BarEntryKind.Receive, 10, Now.AddHours(-2)),
        ], []);
        var data = Data(new BarSale(3, Now.AddDays(-2), 40), new BarSale(3, Now.AddHours(-1), 4));

        Assert.Equal(30 + 10 - 4, Item(BarStockCalculator.Compute(data, ledger, 6), 3).Remaining);
    }

    [Fact]
    public void A_sale_removed_in_gameclass_gives_the_item_back()
    {
        var ledger = new BarLedger([Entry(3, BarEntryKind.Count, 10, Now.AddDays(-1))], []);

        Assert.Equal(7, Item(BarStockCalculator.Compute(Data(new BarSale(3, Now.AddHours(-1), 3)), ledger, 6), 3).Remaining);
        Assert.Equal(10, Item(BarStockCalculator.Compute(Data(), ledger, 6), 3).Remaining);
    }

    [Theory]
    [InlineData(10, 3, 0, "ok")]
    [InlineData(5, 0, 5, "low")]
    [InlineData(5, 5, 5, "out")]
    [InlineData(5, 8, 5, "negative")]
    public void States_follow_the_threshold(int counted, int sold, int threshold, string state)
    {
        var ledger = new BarLedger([Entry(4, BarEntryKind.Count, counted, Now.AddDays(-1))], [new BarItemSettings(4, threshold, true)]);
        var data = sold > 0 ? Data(new BarSale(4, Now.AddHours(-1), sold)) : Data();

        var overview = BarStockCalculator.Compute(data, ledger, 6);

        Assert.Equal(state, Item(overview, 4).State);
        Assert.Equal(state == "low" ? 1 : 0, overview.Low);
        Assert.Equal(state is "out" or "negative" ? 1 : 0, overview.Out);
    }

    [Fact]
    public void Untracked_deleted_and_system_items()
    {
        var overview = BarStockCalculator.Compute(Data(new BarSale(4, Now.AddHours(-1), 2)), BarLedger.Empty, 6);

        Assert.DoesNotContain(overview.Items, i => i.ItemId is 0 or 9);
        var pepsi = Item(overview, 4);
        Assert.False(pepsi.Tracked);
        Assert.Null(pepsi.Remaining);
        Assert.Equal("untracked", pepsi.State);
        Assert.Equal(2, pepsi.SoldToday);
        Assert.Equal(0, overview.Tracked);

        // A deleted product that still has stock stays on the list until counted out.
        var withOld = new BarLedger([Entry(9, BarEntryKind.Count, 4, Now.AddDays(-1))], []);
        Assert.Contains(BarStockCalculator.Compute(Data(), withOld, 6).Items, i => i.ItemId == 9 && i.Remaining == 4);
    }

    [Fact]
    public void Stopped_tracking_hides_the_stock_but_keeps_the_book()
    {
        var ledger = new BarLedger([Entry(3, BarEntryKind.Count, 10, Now.AddDays(-1))], [new BarItemSettings(3, 0, false)]);
        Assert.False(Item(BarStockCalculator.Compute(Data(), ledger, 6), 3).Tracked);
        Assert.Null(ledger.SalesFrom());
    }

    [Fact]
    public void Store_appends_validates_and_reads_back()
    {
        var path = Path.Combine(_dir, BarStore.FileName);
        var store = new BarStore(path);
        Assert.False(store.Exists);

        store.Add(3, "Coca-Cola 0.5", BarEntryKind.Count, 100, " kassa ostida ", Now.AddDays(-1));
        store.Add(3, "Coca-Cola 0.5", BarEntryKind.Receive, 24, null, Now);
        store.SetItem(3, 10, null);

        Assert.Equal("bar_qty", Assert.Throws<SetupException>(() => store.Add(3, "x", BarEntryKind.Receive, 0, null, Now)).Code);
        Assert.Equal("bar_qty", Assert.Throws<SetupException>(() => store.Add(3, "x", BarEntryKind.Receive, 100_001, null, Now)).Code);
        Assert.Equal("bar_kind", Assert.Throws<SetupException>(() => store.Add(3, "x", "sold", 1, null, Now)).Code);
        store.Add(3, "Coca-Cola 0.5", BarEntryKind.Count, 0, null, Now); // an empty shelf is a valid count

        var ledger = new BarStore(path).Load();
        Assert.Equal(3, ledger.Entries.Count);
        Assert.Equal("kassa ostida", ledger.Entries[0].Note);
        Assert.Equal(10, ledger.Threshold(3));
        Assert.Contains("\"item_name\": \"Coca-Cola 0.5\"", File.ReadAllText(path));
        Assert.NotNull(BarStore.TryRead(path));
    }

    [Fact]
    public void Restore_takes_each_backup_line_once()
    {
        var store = new BarStore(null);
        var mine = store.Add(3, "Coca-Cola 0.5", BarEntryKind.Count, 10, null, Now.AddDays(-2));
        var other = Entry(3, BarEntryKind.Receive, 6, Now.AddDays(-1));

        Assert.Equal(1, store.Restore([mine, other], [new BarItemSettings(3, 4, true)]));
        Assert.Equal(0, store.Restore([mine, other], []));
        Assert.Equal(16, Item(BarStockCalculator.Compute(Data(), store.Load(), 6), 3).Remaining);
    }

    [Fact]
    public void Manajoy_backup_json_restores_the_book()
    {
        // Exactly what GET /integrations/gameclass/{id}/bar/entries returns (backend bar.bar_backup).
        const string json = """
            {"entries":[{"uid":"aaaaaaaaaaaaaaaa","item_id":3,"item_name":"Coca-Cola 0.5","kind":"count","qty":10,"at":"2026-10-26T11:00:00","note":"lab"},
                        {"uid":"bbbbbbbbbbbbbbbb","item_id":3,"item_name":"Coca-Cola 0.5","kind":"receive","qty":5,"at":"2026-10-27T09:30:00","note":null}],
             "items":[{"item_id":3,"low_threshold":4,"tracked":true}]}
            """;
        var backup = System.Text.Json.JsonSerializer.Deserialize<BarBackup>(json, Hosting.BridgeJson.SnakeCase)!;
        var store = new BarStore(null);

        Assert.Equal(2, store.Restore(backup.Entries, backup.Items));
        var coke = Item(BarStockCalculator.Compute(Data(new BarSale(3, new DateTime(2026, 10, 26, 11, 37, 0), 2)), store.Load(), 6), 3);
        Assert.Equal(13, coke.Remaining);
        Assert.Equal(4, coke.LowThreshold);
        Assert.Equal(new DateTime(2026, 10, 26, 11, 0, 0), coke.AnchorAt);
    }

    [Fact]
    public void Fingerprint_changes_with_a_number()
    {
        var ledger = new BarLedger([Entry(3, BarEntryKind.Count, 10, Now.AddDays(-1))], []);
        var a = BarStockCalculator.Fingerprint(BarStockCalculator.Compute(Data(), ledger, 6));
        var b = BarStockCalculator.Fingerprint(BarStockCalculator.Compute(Data(), ledger, 6));
        var c = BarStockCalculator.Fingerprint(BarStockCalculator.Compute(Data(new BarSale(3, Now.AddHours(-1), 1)), ledger, 6));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public async Task Service_takes_the_name_and_clock_from_gameclass_and_refuses_unknown_items()
    {
        var source = new FakeSource(Data(new BarSale(3, Now.AddMinutes(-5), 1)));
        var service = new BarService(() => source, new BarStore(null));

        var overview = await service.AddAsync(new BarEntryRequest(3, BarEntryKind.Count, 12, null), 6, CancellationToken.None);
        Assert.Equal(12, Item(overview, 3).Remaining); // the sale 5 min ago is older than the count (GameClass "now")

        await Assert.ThrowsAsync<SetupException>(() => service.AddAsync(new BarEntryRequest(0, BarEntryKind.Count, 1, null), 6, CancellationToken.None));
        await Assert.ThrowsAsync<SetupException>(() => service.AddAsync(new BarEntryRequest(77, BarEntryKind.Count, 1, null), 6, CancellationToken.None));

        var history = await service.HistoryAsync(3, 6, CancellationToken.None);
        Assert.Single(history.Entries);
        Assert.Equal("Coca-Cola 0.5", history.Entries[0].ItemName);
    }

    private sealed class FakeSource(BarData data) : IBarSource
    {
        public Task<BarData> LoadBarAsync(DateTime? salesFrom, CancellationToken cancellationToken) =>
            Task.FromResult(data with { Sales = salesFrom is { } f ? data.Sales.Where(s => s.Moment >= f).ToList() : [] });
    }
}
