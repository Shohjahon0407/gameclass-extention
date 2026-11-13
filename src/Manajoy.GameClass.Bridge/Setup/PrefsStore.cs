using System.Text.Json;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// setup-prefs.json in %ProgramData%\Manajoy\GameClassBridge (demo: memory only). Only harmless UI choices,
/// validated against fixed lists, so a tampered file cannot inject anything into the page.
/// </summary>
internal sealed class PrefsStore(string? path)
{
    private static readonly HashSet<string> Langs = ["uz", "ru"];
    private static readonly HashSet<string> Tabs = ["map", "list"];
    private static readonly HashSet<string> Rails = ["open", "closed"];

    private SetupPrefs _memory = new(null, null);

    public SetupPrefs Load()
    {
        if (path is null)
        {
            return _memory;
        }

        try
        {
            var file = new FileInfo(path);
            return file is { Exists: true, Length: < 4096 }
                ? Clean(JsonSerializer.Deserialize<SetupPrefs>(File.ReadAllText(path)))
                : new SetupPrefs(null, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SetupPrefs(null, null);
        }
    }

    public SetupPrefs Save(SetupPrefs prefs)
    {
        var current = Load();
        var merged = Clean(new SetupPrefs(prefs.Lang ?? current.Lang, prefs.DashTab ?? current.DashTab, prefs.Rail ?? current.Rail));
        if (path is null)
        {
            _memory = merged;
            return merged;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(merged));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference is not worth an error dialog.
        }

        return merged;
    }

    internal static SetupPrefs Clean(SetupPrefs? prefs) => new(
        prefs?.Lang is { } lang && Langs.Contains(lang) ? lang : null,
        prefs?.DashTab is { } tab && Tabs.Contains(tab) ? tab : null,
        prefs?.Rail is { } rail && Rails.Contains(rail) ? rail : null);
}
