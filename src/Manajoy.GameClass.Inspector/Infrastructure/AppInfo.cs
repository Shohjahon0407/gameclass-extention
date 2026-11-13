using System.Reflection;

namespace Manajoy.GameClass.Inspector.Infrastructure;

internal static class AppInfo
{
    public const string DisplayName = "Manajoy GameClass Inspector";

    /// <summary>Reported to SQL Server as the application name so DBAs can identify the connection.</summary>
    public const string SqlApplicationName = "Manajoy.GameClass.Inspector";

    public const string InspectionMode = "READ ONLY";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}

internal static class AppPaths
{
    /// <summary>Relative paths are anchored at the executable folder, not the current directory.</summary>
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    public static string LogFilePath(string logDirectory, DateTime date) =>
        Path.Combine(Resolve(logDirectory), $"inspector-{date:yyyyMMdd}.log");
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int InvalidConfiguration = 2;
    public const int Cancelled = 3;
}
