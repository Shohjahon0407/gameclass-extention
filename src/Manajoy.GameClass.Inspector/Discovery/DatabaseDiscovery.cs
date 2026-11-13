using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Discovery;

internal sealed class DatabaseDiscovery(
    ReadOnlyQueryExecutor executor,
    InspectionErrors errors,
    ILogger<DatabaseDiscovery> logger)
{
    private const string OnlineState = "ONLINE";

    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb", "distribution",
    };

    private static readonly string[] SystemDatabasePrefixes = ["ReportServer"];

    /// <summary>Lists user databases. A database that cannot be opened is reported, not skipped silently.</summary>
    public async Task<IReadOnlyList<DatabaseInfo>> DiscoverAsync(ConnectedInstance instance, CancellationToken cancellationToken)
    {
        IReadOnlyList<RawDatabase> rows;
        try
        {
            rows = await executor.QueryAsync(
                instance.Target,
                null,
                SqlQueries.Databases,
                reader => new RawDatabase(
                    reader.GetRequiredString("DatabaseName"),
                    reader.GetStringOrNull("State"),
                    reader.GetInt32OrNull("CompatibilityLevel"),
                    reader.GetInt32OrNull("HasAccess")),
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Cannot list databases on {Server}: {Message}", instance.ServerName, ex.Message);
            errors.Warning(InspectionStage.DatabaseDiscovery, instance.ServerName, $"Cannot list databases: {ex.Message}");
            return [];
        }

        var databases = new List<DatabaseInfo>();
        foreach (var row in rows.Where(row => !IsSystemDatabase(row.Name)))
        {
            databases.Add(await DescribeAsync(instance, row, cancellationToken));
        }

        logger.LogInformation(
            "{Server}: {Count} user database(s), {Accessible} accessible",
            instance.ServerName, databases.Count, databases.Count(database => database.IsAccessible));
        return databases;
    }

    private async Task<DatabaseInfo> DescribeAsync(ConnectedInstance instance, RawDatabase row, CancellationToken cancellationToken)
    {
        var database = new DatabaseInfo
        {
            Server = instance.ServerName,
            DatabaseName = row.Name,
            State = row.State,
            CompatibilityLevel = row.CompatibilityLevel,
        };

        if (row.HasAccess == 0)
        {
            return Inaccessible(database, "The current login has no access to this database (HAS_DBACCESS = 0).");
        }

        if (row.State is not null && !row.State.Equals(OnlineState, StringComparison.OrdinalIgnoreCase))
        {
            return Inaccessible(database, $"Database state is {row.State}.");
        }

        try
        {
            // Opening a connection to the database is the real accessibility test.
            var size = await executor.QueryAsync(
                instance.Target, row.Name, SqlQueries.DatabaseSize, reader => reader.GetDecimalOrNull("SizeMb"), cancellationToken);
            return database with { IsAccessible = true, SizeMb = size.FirstOrDefault() };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Inaccessible(database, ex.Message);
        }
    }

    private DatabaseInfo Inaccessible(DatabaseInfo database, string reason)
    {
        logger.LogWarning("Database {Database} on {Server} is not accessible: {Reason}", database.DatabaseName, database.Server, reason);
        errors.Warning(InspectionStage.DatabaseDiscovery, $"{database.Server}/{database.DatabaseName}", reason);
        return database with { IsAccessible = false, AccessError = reason };
    }

    internal static bool IsSystemDatabase(string name) =>
        SystemDatabases.Contains(name)
        || SystemDatabasePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private sealed record RawDatabase(string Name, string? State, int? CompatibilityLevel, int? HasAccess);
}
