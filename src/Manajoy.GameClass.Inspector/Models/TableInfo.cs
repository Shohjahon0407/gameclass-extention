namespace Manajoy.GameClass.Inspector.Models;

public sealed record TableInfo
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public string FullName => $"{Schema}.{Name}";

    /// <summary>Taken from index statistics, never from COUNT(*); null when unavailable.</summary>
    public long? ApproximateRowCount { get; init; }

    public IReadOnlyList<string> PrimaryKey { get; init; } = [];

    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; } = [];
}

public sealed record ForeignKeyInfo
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    public string? ReferencedSchema { get; init; }

    public string? ReferencedTable { get; init; }

    public IReadOnlyList<string> ReferencedColumns { get; init; } = [];
}
