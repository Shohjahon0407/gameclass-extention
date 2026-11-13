using System.Text.Json;
using System.Text.RegularExpressions;

namespace Manajoy.GameClass.Bridge.Setup;

// The club floor map the owner draws in the setup window.
//
// GameClass keeps no coordinates (Computers has no position columns; GCServer lists PCs in a table), so the
// map is ours: PCs are keyed by their GameClass number (the same `external_name` the backend maps to Manajoy
// stations), zones are GameClass computer groups (frames are computed in the UI), and decorations (walls,
// desk, entrance, labels, rooms) are local only. PC points are synced with Manajoy's club map
// (services.ResourceLayout, 0..1000) through the bridge-key endpoint /integrations/gameclass/{id}/layout,
// so customers and the Biznes app see the same map. Nothing here touches GameClass.

public sealed record LayoutPoint(int X, int Y);

/// <summary>A decoration: area (hall, bar, WC… with a palette colour), wall, room, desk, entrance or text label.</summary>
public sealed record LayoutObject(string Id, string Kind, int X, int Y, int W, int H, string? Text, string? Color = null);

/// <summary>layout.json in the install folder (Administrators + SYSTEM only).</summary>
public sealed record ClubLayoutDoc(
    int V,
    Dictionary<string, LayoutPoint> Pcs,
    List<LayoutObject> Objects,
    DateTimeOffset? UpdatedAt,
    string? ServerVersion,
    bool Pending,
    List<string>? Skipped = null)
{
    public static ClubLayoutDoc Empty => new(1, new Dictionary<string, LayoutPoint>(), [], null, null, false);
}

public sealed record LayoutSaveRequest(Dictionary<string, LayoutPoint>? Pcs, List<LayoutObject>? Objects);

public sealed record LayoutResolveRequest(bool UseServer);

/// <summary>
/// Sync with Manajoy: local (not paired), synced, pending (saved here, not yet sent — offline),
/// conflict (changed in the Biznes app since; the owner chooses), error (key rejected / server error).
/// </summary>
public sealed record LayoutSyncInfo(string State, string? Detail, int OtherPlaced, IReadOnlyList<string> Skipped);

public sealed record LayoutView(ClubLayoutDoc Layout, LayoutSyncInfo Sync, int Grid);

internal static partial class LayoutRules
{
    public const int Grid = 1000;
    public const int MaxPcs = 1000;
    public const int MaxObjects = 300;
    public const int MaxText = 40;
    public const long MaxFileBytes = 256 * 1024;

    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "area", "wall", "desk", "entrance", "label", "room",
    };

    /// <summary>Area colours are names from the UI palette, never raw CSS (nothing from the file reaches a style).</summary>
    public static readonly IReadOnlySet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
    {
        "blue", "purple", "orange", "green", "teal", "pink", "red", "gray",
    };

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,32}$")]
    private static partial Regex PcKeyPattern();

    /// <summary>Checks what the window sends; the stored file is re-checked on load the same way.</summary>
    public static (Dictionary<string, LayoutPoint> Pcs, List<LayoutObject> Objects) Validate(LayoutSaveRequest request)
    {
        var pcs = request.Pcs ?? new Dictionary<string, LayoutPoint>();
        var objects = request.Objects ?? [];
        if (pcs.Count > MaxPcs)
        {
            throw Invalid($"too many PCs ({pcs.Count} > {MaxPcs})");
        }

        if (objects.Count > MaxObjects)
        {
            throw Invalid($"too many objects ({objects.Count} > {MaxObjects})");
        }

        var cleanPcs = new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
        foreach (var (key, point) in pcs)
        {
            if (!PcKeyPattern().IsMatch(key) || point is null || !InGrid(point.X) || !InGrid(point.Y))
            {
                throw Invalid($"bad PC point '{Shorten(key)}'");
            }

            cleanPcs[key] = point;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cleanObjects = new List<LayoutObject>(objects.Count);
        foreach (var item in objects)
        {
            if (item is null || !IdPattern().IsMatch(item.Id ?? string.Empty) || !ids.Add(item.Id!)
                || !Kinds.Contains(item.Kind ?? string.Empty)
                || (item.Color is not null && !Colors.Contains(item.Color))
                || !InGrid(item.X) || !InGrid(item.Y) || !InGrid(item.W) || !InGrid(item.H))
            {
                throw Invalid($"bad object '{Shorten(item?.Id)}'");
            }

            var text = CleanText(item.Text);
            if (text is { Length: > MaxText })
            {
                throw Invalid($"text longer than {MaxText}");
            }

            cleanObjects.Add(item with { Text = text });
        }

        return (cleanPcs, cleanObjects);
    }

    private static bool InGrid(int value) => value is >= 0 and <= Grid;

    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
    }

    private static string Shorten(string? value) => value is null ? "null" : value.Length > 20 ? value[..20] : value;

    private static SetupException Invalid(string detail) => new("layout_invalid", detail);
}

internal interface ILayoutStore
{
    ClubLayoutDoc? Load();

    void Save(ClubLayoutDoc doc);
}

internal sealed class MemoryLayoutStore(ClubLayoutDoc? initial = null) : ILayoutStore
{
    private ClubLayoutDoc? _doc = initial;

    public ClubLayoutDoc? Load() => _doc;

    public void Save(ClubLayoutDoc doc) => _doc = doc;
}

/// <summary>layout.json, written atomically (temp file + move) so a power cut never leaves half a map.</summary>
internal sealed class FileLayoutStore(string path) : ILayoutStore
{
    public const string FileName = "layout.json";

    private static readonly JsonSerializerOptions Json = CreateOptions();

    public ClubLayoutDoc? Load()
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > LayoutRules.MaxFileBytes)
        {
            return null;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<ClubLayoutDoc>(File.ReadAllText(path), Json);
            if (doc is null)
            {
                return null;
            }

            var (pcs, objects) = LayoutRules.Validate(new LayoutSaveRequest(doc.Pcs, doc.Objects));
            return doc with { Pcs = pcs, Objects = objects };
        }
        catch (Exception ex) when (ex is JsonException or SetupException or IOException)
        {
            return null;
        }
    }

    public void Save(ClubLayoutDoc doc)
    {
        var json = JsonSerializer.Serialize(doc, Json);
        if (json.Length > LayoutRules.MaxFileBytes)
        {
            throw new SetupException("layout_invalid", "layout too large");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        SetupHost.Configure(options);
        return options;
    }
}

/// <summary>Manajoy's copy of the map, as the bridge sees it (GameClass numbers, only PCs mapped to stations).</summary>
internal sealed record RemoteLayout(string Version, IReadOnlyList<RemotePc> Pcs, int OtherPlaced);

internal sealed record RemotePc(string Name, int? X, int? Y);

internal sealed record RemoteSaveResult(int Saved, IReadOnlyList<string> Skipped, string Version);

/// <summary>offline (network), key_rejected (401), failed (other HTTP errors).</summary>
internal sealed class LayoutRemoteException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
}

internal sealed class LayoutConflictException(RemoteLayout server) : Exception("layout_conflict")
{
    public RemoteLayout Server { get; } = server;
}

internal interface ILayoutRemote
{
    Task<RemoteLayout> GetAsync(CancellationToken cancellationToken);

    Task<RemoteSaveResult> PutAsync(IReadOnlyDictionary<string, LayoutPoint> pcs, string? baseVersion, bool force, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps layout.json and Manajoy in step. Rules:
/// - opening the map pulls Manajoy's points unless local edits are still unsent;
/// - saving writes locally first (works offline), then pushes with the version last seen (optimistic lock);
/// - if the map changed elsewhere meanwhile, the owner chooses (conflict) — nothing is overwritten silently;
/// - PCs Manajoy does not know yet (not mapped to a station) keep their local point and are listed as skipped.
/// </summary>
internal sealed class LayoutCoordinator(ILayoutStore store, Func<ILayoutRemote?> remoteFactory)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RemoteLayout? _conflict;
    private int _otherPlaced;

    public async Task<LayoutView> LoadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var doc = store.Load() ?? ClubLayoutDoc.Empty;
            var remote = remoteFactory();
            if (remote is null)
            {
                return View(doc, "local");
            }

            if (doc.Pending)
            {
                return await PushAsync(doc, remote, force: false, cancellationToken);
            }

            try
            {
                var server = await remote.GetAsync(cancellationToken);
                _otherPlaced = server.OtherPlaced;
                if (server.Version != doc.ServerVersion)
                {
                    doc = Merge(doc, server);
                    store.Save(doc);
                }

                return View(doc, "synced");
            }
            catch (LayoutRemoteException ex)
            {
                return View(doc, ex.Code == "offline" ? "offline" : "error", ex.Code);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LayoutView> SaveAsync(LayoutSaveRequest request, CancellationToken cancellationToken)
    {
        var (pcs, objects) = LayoutRules.Validate(request);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var current = store.Load() ?? ClubLayoutDoc.Empty;
            var doc = current with { Pcs = pcs, Objects = objects, UpdatedAt = DateTimeOffset.Now, Pending = true };
            store.Save(doc);
            var remote = remoteFactory();
            if (remote is null)
            {
                doc = doc with { Pending = false };
                store.Save(doc);
                return View(doc, "local");
            }

            return await PushAsync(doc, remote, force: false, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LayoutView> ResolveAsync(bool useServer, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var doc = store.Load() ?? ClubLayoutDoc.Empty;
            var remote = remoteFactory() ?? throw new SetupException("not_paired");
            if (!useServer)
            {
                return await PushAsync(doc with { Pending = true }, remote, force: true, cancellationToken);
            }

            try
            {
                var server = await remote.GetAsync(cancellationToken);
                _otherPlaced = server.OtherPlaced;
                doc = Merge(doc with { Pending = false }, server);
                store.Save(doc);
                _conflict = null;
                return View(doc, "synced");
            }
            catch (LayoutRemoteException ex)
            {
                return View(doc, ex.Code == "offline" ? "offline" : "error", ex.Code);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<LayoutView> PushAsync(ClubLayoutDoc doc, ILayoutRemote remote, bool force, CancellationToken cancellationToken)
    {
        try
        {
            var baseVersion = doc.ServerVersion;
            if (baseVersion is null && !force)
            {
                // Never synced: do not overwrite a map someone already drew in the Biznes app.
                var server = await remote.GetAsync(cancellationToken);
                _otherPlaced = server.OtherPlaced;
                if (server.Pcs.Any(pc => pc.X is not null))
                {
                    throw new LayoutConflictException(server);
                }

                baseVersion = server.Version;
            }

            var result = await remote.PutAsync(doc.Pcs, baseVersion, force, cancellationToken);
            doc = doc with { ServerVersion = result.Version, Pending = false, Skipped = result.Skipped.ToList() };
            store.Save(doc);
            _conflict = null;
            return View(doc, "synced");
        }
        catch (LayoutConflictException ex)
        {
            _conflict = ex.Server;
            store.Save(doc);
            return View(doc, "conflict");
        }
        catch (LayoutRemoteException ex)
        {
            store.Save(doc);
            return View(doc, ex.Code == "offline" ? "pending" : "error", ex.Code);
        }
    }

    /// <summary>Manajoy's points win for the PCs it knows; the rest keep their local point.</summary>
    internal static ClubLayoutDoc Merge(ClubLayoutDoc doc, RemoteLayout server)
    {
        var pcs = new Dictionary<string, LayoutPoint>(doc.Pcs, StringComparer.Ordinal);
        foreach (var pc in server.Pcs)
        {
            if (pc.X is { } x && pc.Y is { } y)
            {
                pcs[pc.Name] = new LayoutPoint(x, y);
            }
            else
            {
                pcs.Remove(pc.Name);
            }
        }

        var known = server.Pcs.Select(pc => pc.Name).ToHashSet(StringComparer.Ordinal);
        var skipped = pcs.Keys.Where(name => !known.Contains(name)).OrderBy(name => name.Length).ThenBy(name => name, StringComparer.Ordinal).ToList();
        return doc with { Pcs = pcs, ServerVersion = server.Version, Pending = false, Skipped = skipped };
    }

    private LayoutView View(ClubLayoutDoc doc, string state, string? detail = null) =>
        new(doc, new LayoutSyncInfo(state, detail ?? (state == "conflict" ? ConflictSummary() : null), _otherPlaced, doc.Skipped ?? []), LayoutRules.Grid);

    private string? ConflictSummary() =>
        _conflict is null ? null : $"{_conflict.Pcs.Count(pc => pc.X is not null)}";
}
