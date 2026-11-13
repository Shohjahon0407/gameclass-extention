using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Manajoy.GameClass.Bridge.Hosting;

internal static class BridgeInfo
{
    public const string DisplayName = "Manajoy GameClass Bridge";

    public static string Version { get; } =
        typeof(BridgeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BridgeInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}

internal static class BridgePaths
{
    /// <summary>Relative paths are anchored at the executable folder, not the current directory (services start in System32).</summary>
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}

internal static class BridgeJson
{
    // Cyrillic club/tariff names stay readable instead of \uXXXX escapes.
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    public static readonly JsonSerializerOptions Compact = new() { Encoder = Encoder };

    public static readonly JsonSerializerOptions Indented = new() { Encoder = Encoder, WriteIndented = true };

    /// <summary>The analytics push (Manajoy's Python API speaks snake_case).</summary>
    public static readonly JsonSerializerOptions SnakeCase = new() { Encoder = Encoder, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

internal static class BridgeHosts
{
    /// <summary>
    /// Semicolon-separated AllowedHosts for HostFilteringMiddleware. Wildcard bindings (0.0.0.0, +, *) cannot be
    /// narrowed to a host name, so they fall back to "*"; such bindings always require an ApiKey.
    /// </summary>
    public static string AllowedHosts(IEnumerable<string> urls)
    {
        var hosts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "[::1]" };
        foreach (var url in urls)
        {
            var host = url.Split("://", 2).ElementAtOrDefault(1)?.Split('/', 2)[0] ?? string.Empty;
            host = host.StartsWith('[') ? host[..(host.IndexOf(']') + 1)] : host.Split(':')[0];
            if (host is "" or "*" or "+" or "0.0.0.0" or "[::]")
            {
                return "*";
            }

            hosts.Add(host);
        }

        return string.Join(';', hosts);
    }
}
