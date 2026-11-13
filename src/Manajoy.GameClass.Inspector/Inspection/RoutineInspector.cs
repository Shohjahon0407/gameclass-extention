using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>Stored procedure / function names only. Definitions are not read and nothing is executed.</summary>
internal sealed class RoutineInspector(ReadOnlyQueryExecutor executor)
{
    public Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(ConnectionTarget target, string database, CancellationToken cancellationToken) =>
        executor.QueryAsync(
            target,
            database,
            SqlQueries.Routines,
            reader => new RoutineInfo(
                reader.GetRequiredString("SchemaName"),
                reader.GetRequiredString("RoutineName"),
                reader.GetStringOrNull("RoutineType") ?? "UNKNOWN"),
            cancellationToken);
}
