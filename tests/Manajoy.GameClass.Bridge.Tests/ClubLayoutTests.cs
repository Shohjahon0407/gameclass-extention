using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

public class LayoutRulesTests
{
    private static LayoutSaveRequest Request(Dictionary<string, LayoutPoint>? pcs = null, params LayoutObject[] objects) =>
        new(pcs ?? new Dictionary<string, LayoutPoint> { ["1"] = new(100, 100) }, objects.ToList());

    [Fact]
    public void Valid_map_passes_and_text_is_cleaned()
    {
        var (pcs, objects) = LayoutRules.Validate(Request(null, new LayoutObject("desk1", "desk", 0, 0, 150, 75, " Kassa\u0007 ")));

        Assert.Equal(new LayoutPoint(100, 100), pcs["1"]);
        Assert.Equal("Kassa", objects[0].Text);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1001)]
    public void Points_outside_the_grid_are_rejected(int x, int y)
    {
        var error = Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(new() { ["1"] = new(x, y) })));
        Assert.Equal("layout_invalid", error.Code);
    }

    [Theory]
    [InlineData("<b>")]
    [InlineData("")]
    [InlineData("123456789012345678901234567890123")]
    public void Bad_pc_keys_are_rejected(string key)
    {
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(new() { [key] = new(1, 1) })));
    }

    [Fact]
    public void Unknown_kinds_duplicate_ids_and_long_texts_are_rejected()
    {
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null, new LayoutObject("a", "script", 0, 0, 10, 10, null))));
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null,
            new LayoutObject("a", "wall", 0, 0, 10, 10, null), new LayoutObject("a", "wall", 0, 0, 10, 10, null))));
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null, new LayoutObject("a b", "wall", 0, 0, 10, 10, null))));
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null, new LayoutObject("a", "label", 0, 0, 10, 10, new string('x', 41)))));
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null, new LayoutObject("a", "area", 0, 0, 10, 10, "Bar", "#ff0000"))));
        Assert.Equal("orange", LayoutRules.Validate(Request(null, new LayoutObject("a", "area", 0, 0, 10, 10, "Bar", "orange"))).Objects[0].Color);
    }

    [Fact]
    public void Too_many_objects_are_rejected()
    {
        var objects = Enumerable.Range(0, LayoutRules.MaxObjects + 1).Select(i => new LayoutObject($"w{i}", "wall", 0, 0, 10, 10, null)).ToArray();
        Assert.Throws<SetupException>(() => LayoutRules.Validate(Request(null, objects)));
    }

    [Fact]
    public void File_store_round_trips_and_ignores_a_broken_file()
    {
        var dir = Directory.CreateTempSubdirectory("mj-layout-");
        try
        {
            var path = Path.Combine(dir.FullName, FileLayoutStore.FileName);
            var store = new FileLayoutStore(path);
            Assert.Null(store.Load());

            var doc = ClubLayoutDoc.Empty with
            {
                Pcs = new Dictionary<string, LayoutPoint> { ["7"] = new(250, 300) },
                Objects = [new LayoutObject("door1", "entrance", 50, 600, 125, 25, "Kirish")],
                ServerVersion = "v1",
            };
            store.Save(doc);
            var loaded = store.Load()!;
            Assert.Equal(new LayoutPoint(250, 300), loaded.Pcs["7"]);
            Assert.Equal("Kirish", loaded.Objects[0].Text);
            Assert.Equal("v1", loaded.ServerVersion);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, """{"v":1,"pcs":{"1":{"x":99999,"y":0}},"objects":[]}""");
            Assert.Null(store.Load());
            File.WriteAllText(path, "not json");
            Assert.Null(store.Load());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

public class LayoutCoordinatorTests
{
    private sealed class FakeManajoy : ILayoutRemote
    {
        public Dictionary<string, LayoutPoint> Points { get; } = new();

        public HashSet<string> Mapped { get; } = ["1", "2", "3"];

        public bool Offline { get; set; }

        public int Puts { get; private set; }

        public string Version => string.Join(';', Points.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.X}:{p.Value.Y}"));

        public Task<RemoteLayout> GetAsync(CancellationToken cancellationToken)
        {
            ThrowIfOffline();
            return Task.FromResult(Current());
        }

        public Task<RemoteSaveResult> PutAsync(IReadOnlyDictionary<string, LayoutPoint> pcs, string? baseVersion, bool force, CancellationToken cancellationToken)
        {
            ThrowIfOffline();
            if (!force && baseVersion != Version)
            {
                throw new LayoutConflictException(Current());
            }

            Puts++;
            Points.Clear();
            foreach (var (name, point) in pcs.Where(p => Mapped.Contains(p.Key)))
            {
                Points[name] = point;
            }

            return Task.FromResult(new RemoteSaveResult(Points.Count, pcs.Keys.Where(k => !Mapped.Contains(k)).ToList(), Version));
        }

        private RemoteLayout Current() => new(Version,
            Mapped.Order().Select(n => Points.TryGetValue(n, out var p) ? new RemotePc(n, p.X, p.Y) : new RemotePc(n, null, null)).ToList(), 0);

        private void ThrowIfOffline()
        {
            if (Offline)
            {
                throw new LayoutRemoteException("offline", "no route to host");
            }
        }
    }

    private static LayoutSaveRequest Map(params (string Name, int X, int Y)[] points) =>
        new(points.ToDictionary(p => p.Name, p => new LayoutPoint(p.X, p.Y)), [new LayoutObject("desk1", "desk", 0, 0, 100, 50, null)]);

    [Fact]
    public async Task Without_pairing_the_map_stays_local()
    {
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => null);

        var view = await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None);

        Assert.Equal("local", view.Sync.State);
        Assert.False(store.Load()!.Pending);
        Assert.Equal("local", (await layout.LoadAsync(CancellationToken.None)).Sync.State);
    }

    [Fact]
    public async Task Opening_pulls_manajoy_points_and_keeps_local_only_pcs()
    {
        var manajoy = new FakeManajoy();
        manajoy.Points["1"] = new LayoutPoint(500, 500);
        var store = new MemoryLayoutStore(ClubLayoutDoc.Empty with
        {
            Pcs = new Dictionary<string, LayoutPoint> { ["1"] = new(100, 100), ["2"] = new(200, 100), ["9"] = new(900, 900) },
            ServerVersion = "old",
        });
        var layout = new LayoutCoordinator(store, () => manajoy);

        var view = await layout.LoadAsync(CancellationToken.None);

        Assert.Equal("synced", view.Sync.State);
        Assert.Equal(new LayoutPoint(500, 500), view.Layout.Pcs["1"]);
        Assert.False(view.Layout.Pcs.ContainsKey("2"));          // removed in Manajoy
        Assert.Equal(new LayoutPoint(900, 900), view.Layout.Pcs["9"]); // not a station: stays here
        Assert.Equal(["9"], view.Sync.Skipped);
    }

    [Fact]
    public async Task Saving_pushes_with_the_last_seen_version()
    {
        var manajoy = new FakeManajoy();
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => manajoy);
        await layout.LoadAsync(CancellationToken.None);

        var view = await layout.SaveAsync(Map(("1", 100, 100), ("2", 200, 100), ("7", 300, 100)), CancellationToken.None);

        Assert.Equal("synced", view.Sync.State);
        Assert.Equal(["7"], view.Sync.Skipped);
        Assert.Equal(2, manajoy.Points.Count);
        Assert.Equal(manajoy.Version, store.Load()!.ServerVersion);
        Assert.Single(store.Load()!.Objects);
    }

    [Fact]
    public async Task Offline_save_stays_pending_and_is_sent_on_next_open()
    {
        var manajoy = new FakeManajoy();
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => manajoy);
        await layout.LoadAsync(CancellationToken.None);

        manajoy.Offline = true;
        var view = await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None);
        Assert.Equal("pending", view.Sync.State);
        Assert.True(store.Load()!.Pending);
        Assert.Empty(manajoy.Points);

        manajoy.Offline = false;
        view = await layout.LoadAsync(CancellationToken.None);
        Assert.Equal("synced", view.Sync.State);
        Assert.False(store.Load()!.Pending);
        Assert.Equal(new LayoutPoint(100, 100), manajoy.Points["1"]);
    }

    [Fact]
    public async Task A_map_changed_elsewhere_is_never_overwritten_silently()
    {
        var manajoy = new FakeManajoy();
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => manajoy);
        await layout.LoadAsync(CancellationToken.None);

        manajoy.Points["3"] = new LayoutPoint(700, 700); // drawn in the Biznes app meanwhile
        var view = await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None);

        Assert.Equal("conflict", view.Sync.State);
        Assert.Equal("1", view.Sync.Detail);
        Assert.Equal(0, manajoy.Puts);
        Assert.True(store.Load()!.Pending);

        view = await layout.ResolveAsync(useServer: true, CancellationToken.None);
        Assert.Equal("synced", view.Sync.State);
        Assert.Equal(new LayoutPoint(700, 700), view.Layout.Pcs["3"]);
        Assert.False(view.Layout.Pcs.ContainsKey("1"));
        Assert.Single(view.Layout.Objects);
    }

    [Fact]
    public async Task Owner_can_choose_to_overwrite_after_a_conflict()
    {
        var manajoy = new FakeManajoy();
        manajoy.Points["3"] = new LayoutPoint(700, 700);
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => null);
        await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None); // drawn before pairing

        layout = new LayoutCoordinator(store, () => manajoy);
        await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None);
        Assert.Equal("conflict", (await layout.SaveAsync(Map(("1", 100, 100)), CancellationToken.None)).Sync.State);

        var view = await layout.ResolveAsync(useServer: false, CancellationToken.None);

        Assert.Equal("synced", view.Sync.State);
        Assert.Equal(new LayoutPoint(100, 100), manajoy.Points["1"]);
        Assert.False(manajoy.Points.ContainsKey("3"));
    }

    [Fact]
    public async Task Rejected_key_is_reported_not_retried_as_offline()
    {
        var store = new MemoryLayoutStore();
        var layout = new LayoutCoordinator(store, () => new RejectingManajoy());

        var view = await layout.LoadAsync(CancellationToken.None);

        Assert.Equal("error", view.Sync.State);
        Assert.Equal("key_rejected", view.Sync.Detail);
    }

    private sealed class RejectingManajoy : ILayoutRemote
    {
        public Task<RemoteLayout> GetAsync(CancellationToken cancellationToken) => throw new LayoutRemoteException("key_rejected", "HTTP 401");

        public Task<RemoteSaveResult> PutAsync(IReadOnlyDictionary<string, LayoutPoint> pcs, string? baseVersion, bool force, CancellationToken cancellationToken) =>
            throw new LayoutRemoteException("key_rejected", "HTTP 401");
    }
}
