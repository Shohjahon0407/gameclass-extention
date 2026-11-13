namespace Manajoy.GameClass.Inspector.Models;

public sealed record SampleRowsResult
{
    public required string Server { get; init; }

    public required string Database { get; init; }

    public required string Table { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Columns never read from the server because they look sensitive.</summary>
    public IReadOnlyList<string> MaskedColumns { get; init; } = [];

    /// <summary>Binary / large-object columns that are not read.</summary>
    public IReadOnlyList<string> SkippedColumns { get; init; } = [];

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows { get; init; } = [];

    public string? Note { get; init; }
}
