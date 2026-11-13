namespace Manajoy.GameClass.Inspector.Models;

public sealed record DatabaseSchema
{
    public required string Server { get; init; }

    public required string Database { get; init; }

    public IReadOnlyList<TableInfo> Tables { get; init; } = [];

    public IReadOnlyList<ViewInfo> Views { get; init; } = [];

    /// <summary>Names only; definitions are not read and nothing is executed.</summary>
    public IReadOnlyList<RoutineInfo> Routines { get; init; } = [];
}
