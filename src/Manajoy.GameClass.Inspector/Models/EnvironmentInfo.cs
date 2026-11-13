namespace Manajoy.GameClass.Inspector.Models;

public sealed record EnvironmentInfo
{
    public required string ApplicationName { get; init; }

    public required string ApplicationVersion { get; init; }

    public required string MachineName { get; init; }

    public required string OsName { get; init; }

    public required string OsVersion { get; init; }

    public required string OsArchitecture { get; init; }

    public required string ProcessArchitecture { get; init; }

    public required string DotNetVersion { get; init; }

    public required string CurrentUser { get; init; }

    /// <summary>Null when elevation cannot be determined (non-Windows).</summary>
    public bool? IsElevated { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string InspectionMode { get; init; }
}
