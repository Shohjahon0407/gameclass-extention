using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Bar;

/// <summary>
/// The stock book: bar.json next to the «Hisobot» PIN file (%ProgramData%\Manajoy\GameClassBridge\analytics,
/// Administrators and SYSTEM only), so the service can read it for the push. Demo keeps it in memory. Lines are
/// only ever appended; a broken or unreadable file is reported, never silently replaced (it is the owner's stock).
/// </summary>
internal sealed class BarStore(string? path)
{
    public const string FileName = "bar.json";
    public const int MaxQty = 100_000;
    public const int MaxNote = 120;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private readonly object _sync = new();
    private BarFile? _memory;

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(Analytics.AnalyticsAccess.DefaultPath)!, FileName);

    public bool Exists => path is null ? _memory is not null : File.Exists(path);

    public BarLedger Load()
    {
        lock (_sync)
        {
            return ReadFile().ToLedger();
        }
    }

    public BarEntry Add(int itemId, string itemName, string? kind, int qty, string? note, DateTime at)
    {
        if (!BarEntryKind.IsValid(kind))
        {
            throw new SetupException("bar_kind");
        }

        // A count may be 0 (the shelf is empty); a delivery or write-off must move something.
        if (qty is < 0 or > MaxQty || (qty == 0 && kind != BarEntryKind.Count))
        {
            throw new SetupException("bar_qty");
        }

        if (itemId <= 0)
        {
            throw new SetupException("bar_item");
        }

        var entry = new BarEntry(Guid.NewGuid().ToString("N"), itemId, Trim(itemName, 150) ?? $"#{itemId}", kind!, qty, at,
            Trim(note, MaxNote));
        lock (_sync)
        {
            var file = ReadFile();
            file.Entries.Add(entry);
            var settings = file.Items.FirstOrDefault(i => i.ItemId == itemId);
            if (settings is { Tracked: false })
            {
                file.Items[file.Items.IndexOf(settings)] = settings with { Tracked = true };
            }

            WriteFile(file);
        }

        return entry;
    }

    public BarItemSettings SetItem(int itemId, int? lowThreshold, bool? tracked)
    {
        if (itemId <= 0)
        {
            throw new SetupException("bar_item");
        }

        if (lowThreshold is < 0 or > MaxQty)
        {
            throw new SetupException("bar_qty");
        }

        lock (_sync)
        {
            var file = ReadFile();
            var current = file.Items.FirstOrDefault(i => i.ItemId == itemId)
                ?? new BarItemSettings(itemId, 0, file.Entries.Any(e => e.ItemId == itemId));
            var updated = current with { LowThreshold = lowThreshold ?? current.LowThreshold, Tracked = tracked ?? current.Tracked };
            file.Items.Remove(current);
            file.Items.Add(updated);
            WriteFile(file);
            return updated;
        }
    }

    /// <summary>After a PC change: take the lines Manajoy kept (by uid, nothing is lost or doubled).</summary>
    public int Restore(IEnumerable<BarEntry> entries, IEnumerable<BarItemSettings> items)
    {
        lock (_sync)
        {
            var file = ReadFile();
            var known = file.Entries.Select(e => e.Uid).ToHashSet();
            var added = entries.Where(e => known.Add(e.Uid) && BarEntryKind.IsValid(e.Kind)).ToList();
            file.Entries.AddRange(added);
            file.Entries.Sort((a, b) => a.At.CompareTo(b.At));
            foreach (var item in items.Where(i => file.Items.All(x => x.ItemId != i.ItemId)))
            {
                file.Items.Add(item);
            }

            WriteFile(file);
            return added.Count;
        }
    }

    private static string? Trim(string? value, int max)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
    }

    private BarFile ReadFile()
    {
        if (path is null)
        {
            return _memory ?? new BarFile();
        }

        try
        {
            if (!File.Exists(path))
            {
                return new BarFile();
            }

            return JsonSerializer.Deserialize<BarFile>(File.ReadAllText(path), Json) ?? new BarFile();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SetupException("bar_file", ex.Message, ex);
        }
    }

    private void WriteFile(BarFile file)
    {
        if (path is null)
        {
            _memory = file;
            return;
        }

        var dir = Path.GetDirectoryName(path)!;
        var fresh = !Directory.Exists(dir);
        Directory.CreateDirectory(dir);
        if (fresh && OperatingSystem.IsWindows())
        {
            ServiceManager.RestrictFolder(dir);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>For the service (read-only, never throws): an empty book when the file is missing or unreadable.</summary>
    public static BarLedger? TryRead(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? JsonSerializer.Deserialize<BarFile>(File.ReadAllText(path), Json)?.ToLedger() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal sealed class BarFile
    {
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;

        public List<BarEntry> Entries { get; set; } = [];

        public List<BarItemSettings> Items { get; set; } = [];

        public BarLedger ToLedger() => new(Entries.OrderBy(e => e.At).ToList(), Items.ToList());
    }
}
