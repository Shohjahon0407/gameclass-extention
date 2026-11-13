using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>Tables with approximate row counts, primary keys and foreign keys. Never reads table rows.</summary>
internal sealed class TableInspector(
    ReadOnlyQueryExecutor executor,
    InspectionErrors errors,
    ILogger<TableInspector> logger)
{
    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(
        ConnectionTarget target,
        string database,
        ColumnLookup columns,
        CancellationToken cancellationToken)
    {
        var tables = await GetTableNamesAsync(target, database, cancellationToken);
        var primaryKeys = await GetPrimaryKeysAsync(target, database, cancellationToken);
        var foreignKeys = await GetForeignKeysAsync(target, database, cancellationToken);

        return tables
            .Select(table =>
            {
                var key = ColumnLookup.Key(table.Schema, table.Name);
                var primaryKey = primaryKeys.GetValueOrDefault(key) ?? [];
                return new TableInfo
                {
                    Schema = table.Schema,
                    Name = table.Name,
                    ApproximateRowCount = table.ApproxRows,
                    PrimaryKey = primaryKey,
                    Columns = columns.For(table.Schema, table.Name)
                        .Select(column => column with { IsPrimaryKey = primaryKey.Contains(column.Name, StringComparer.OrdinalIgnoreCase) })
                        .ToList(),
                    ForeignKeys = foreignKeys.GetValueOrDefault(key) ?? [],
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<RawTable>> GetTableNamesAsync(ConnectionTarget target, string database, CancellationToken cancellationToken)
    {
        static RawTable Map(System.Data.Common.DbDataReader reader) => new(
            reader.GetRequiredString("SchemaName"),
            reader.GetRequiredString("TableName"),
            reader.GetInt64OrNull("ApproxRows"));

        try
        {
            return await executor.QueryAsync(target, database, SqlQueries.Tables, Map, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Row counts unavailable in {Database}, listing tables without them: {Message}", database, ex.Message);
            errors.Warning(InspectionStage.SchemaInspection, $"{target.Server}/{database}", $"Approximate row counts unavailable: {ex.Message}");
            return await executor.QueryAsync(target, database, SqlQueries.TablesWithoutRowCounts, Map, cancellationToken);
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> GetPrimaryKeysAsync(
        ConnectionTarget target, string database, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await executor.QueryAsync(
                target,
                database,
                SqlQueries.PrimaryKeys,
                reader => (
                    Key: ColumnLookup.Key(reader.GetRequiredString("SchemaName"), reader.GetRequiredString("TableName")),
                    Column: reader.GetRequiredString("ColumnName"),
                    Ordinal: reader.GetInt32OrNull("Ordinal") ?? 0),
                cancellationToken);

            return rows
                .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.OrderBy(row => row.Ordinal).Select(row => row.Column).ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ReportOptionalFailure(target, database, "primary keys", ex);
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<ForeignKeyInfo>>> GetForeignKeysAsync(
        ConnectionTarget target, string database, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await executor.QueryAsync(
                target,
                database,
                SqlQueries.ForeignKeys,
                reader => new RawForeignKeyColumn(
                    reader.GetRequiredString("ForeignKeyName"),
                    reader.GetRequiredString("SchemaName"),
                    reader.GetRequiredString("TableName"),
                    reader.GetRequiredString("ColumnName"),
                    reader.GetStringOrNull("ReferencedSchema"),
                    reader.GetStringOrNull("ReferencedTable"),
                    reader.GetStringOrNull("ReferencedColumn"),
                    reader.GetInt32OrNull("Ordinal") ?? 0),
                cancellationToken);

            return rows
                .GroupBy(row => ColumnLookup.Key(row.Schema, row.Table), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    table => table.Key,
                    table => (IReadOnlyList<ForeignKeyInfo>)table
                        .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(foreignKey =>
                        {
                            var ordered = foreignKey.OrderBy(row => row.Ordinal).ToList();
                            return new ForeignKeyInfo
                            {
                                Name = foreignKey.Key,
                                Columns = ordered.Select(row => row.Column).ToList(),
                                ReferencedSchema = ordered[0].ReferencedSchema,
                                ReferencedTable = ordered[0].ReferencedTable,
                                ReferencedColumns = ordered.Select(row => row.ReferencedColumn).OfType<string>().ToList(),
                            };
                        })
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ReportOptionalFailure(target, database, "foreign keys", ex);
            return new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void ReportOptionalFailure(ConnectionTarget target, string database, string what, Exception ex)
    {
        logger.LogWarning("Cannot read {What} in {Database}: {Message}", what, database, ex.Message);
        errors.Warning(InspectionStage.SchemaInspection, $"{target.Server}/{database}", $"Cannot read {what}: {ex.Message}");
    }

    private sealed record RawTable(string Schema, string Name, long? ApproxRows);

    private sealed record RawForeignKeyColumn(
        string Name,
        string Schema,
        string Table,
        string Column,
        string? ReferencedSchema,
        string? ReferencedTable,
        string? ReferencedColumn,
        int Ordinal);
}
