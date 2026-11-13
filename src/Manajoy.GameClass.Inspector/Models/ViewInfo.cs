namespace Manajoy.GameClass.Inspector.Models;

public sealed record ViewInfo
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public string FullName => $"{Schema}.{Name}";

    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>View source as exposed by INFORMATION_SCHEMA (max 4000 chars, null if encrypted).</summary>
    public string? Definition { get; init; }
}

public sealed record RoutineInfo(string Schema, string Name, string Type);
