using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Discovery;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>
/// Optional, off by default. Reads at most <see cref="InspectorOptions.SampleRowsHardLimit"/> rows from tables
/// the engineer listed explicitly, and only if those tables exist in the metadata already collected.
/// Sensitive columns are not selected at all.
/// </summary>
internal sealed class SampleRowInspector(
    ReadOnlyQueryExecutor executor,
    IOptions<InspectorOptions> options,
    InspectionErrors errors,
    ILogger<SampleRowInspector> logger)
{
    private const string NotReadMarker = "<not read>";

    private readonly InspectorOptions _options = options.Value;

    public async Task<IReadOnlyList<SampleRowsResult>> CollectAsync(
        IReadOnlyList<ConnectedInstance> instances,
        IReadOnlyList<DatabaseSchema> schemas,
        CancellationToken cancellationToken)
    {
        if (!_options.AllowSampleRows)
        {
            return [];
        }

        if (_options.SampleTables.Count == 0)
        {
            logger.LogWarning("AllowSampleRows is true but no SampleTables are configured; nothing is sampled");
            return [];
        }

        var rowLimit = Math.Min(_options.MaxSampleRows, InspectorOptions.SampleRowsHardLimit);
        var results = new List<SampleRowsResult>();

        foreach (var request in _options.SampleTables)
        {
            var matches = FindTables(request, instances, schemas).ToList();
            if (matches.Count == 0)
            {
                var message = $"Sample table {request} was not found in the inspected metadata; skipped.";
                logger.LogWarning("{Message}", message);
                errors.Warning(InspectionStage.SampleRows, request.ToString(), message);
                continue;
            }

            foreach (var (instance, database, table) in matches)
            {
                results.Add(await SampleAsync(instance, database, table, rowLimit, cancellationToken));
            }
        }

        return results;
    }

    private static IEnumerable<(ConnectedInstance Instance, string Database, TableInfo Table)> FindTables(
        SampleTableOptions request,
        IReadOnlyList<ConnectedInstance> instances,
        IReadOnlyList<DatabaseSchema> schemas)
    {
        foreach (var instance in instances)
        {
            if (!string.IsNullOrWhiteSpace(request.Server) && !SqlServerName.AreSame(request.Server, instance.ServerName))
            {
                continue;
            }

            var schema = schemas.FirstOrDefault(s =>
                s.Server == instance.ServerName && s.Database.Equals(request.Database, StringComparison.OrdinalIgnoreCase));
            var table = schema?.Tables.FirstOrDefault(t =>
                t.Schema.Equals(request.Schema, StringComparison.OrdinalIgnoreCase)
                && t.Name.Equals(request.Table, StringComparison.OrdinalIgnoreCase));

            if (schema is not null && table is not null)
            {
                // Identifiers are taken from server metadata, not from configuration text.
                yield return (instance, schema.Database, table);
            }
        }
    }

    private async Task<SampleRowsResult> SampleAsync(
        ConnectedInstance instance,
        string database,
        TableInfo table,
        int rowLimit,
        CancellationToken cancellationToken)
    {
        var isPersonTable = SensitiveDataMasker.IsPersonTable(table.Name);
        var masked = new List<string>();
        var skipped = new List<string>();
        var selected = new List<string>();

        foreach (var column in table.Columns)
        {
            if (SensitiveDataMasker.IsSensitiveColumn(column.Name) || (isPersonTable && SensitiveDataMasker.IsTextType(column.DataType)))
            {
                masked.Add(column.Name);
            }
            else if (SensitiveDataMasker.IsUnreadableType(column.DataType))
            {
                skipped.Add(column.Name);
            }
            else
            {
                selected.Add(column.Name);
            }
        }

        var result = new SampleRowsResult
        {
            Server = instance.ServerName,
            Database = database,
            Table = table.FullName,
            Columns = table.Columns.Select(column => column.Name).ToList(),
            MaskedColumns = masked,
            SkippedColumns = skipped,
        };

        if (selected.Count == 0)
        {
            return result with { Note = "Every column is sensitive or binary; no rows were read." };
        }

        try
        {
            logger.LogInformation(
                "Reading up to {Limit} sample rows from {Server}/{Database}/{Table} ({Masked} column(s) masked)",
                rowLimit, instance.ServerName, database, table.FullName, masked.Count);

            var sql = SqlQueries.BuildSampleQuery(table.Schema, table.Name, selected, rowLimit);
            var ordinals = selected
                .Select((name, ordinal) => (name, ordinal))
                .ToDictionary(pair => pair.name, pair => pair.ordinal, StringComparer.OrdinalIgnoreCase);
            var rows = await executor.QueryAsync(
                instance.Target,
                database,
                sql,
                reader => ReadRow(reader, table.Columns, ordinals, masked),
                cancellationToken);

            return result with { Rows = rows };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Cannot sample {Table} in {Database}: {Message}", table.FullName, database, ex.Message);
            errors.Warning(InspectionStage.SampleRows, $"{instance.ServerName}/{database}/{table.FullName}", ex.Message);
            return result with { Note = $"Sampling failed: {ex.Message}" };
        }
    }

    private static Dictionary<string, string?> ReadRow(
        System.Data.Common.DbDataReader reader,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyDictionary<string, int> selectedOrdinals,
        IReadOnlyList<string> masked)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (selectedOrdinals.TryGetValue(column.Name, out var ordinal))
            {
                row[column.Name] = SensitiveDataMasker.MaskValue(reader.GetValue(ordinal));
            }
            else
            {
                row[column.Name] = masked.Contains(column.Name) ? SensitiveDataMasker.Mask : NotReadMarker;
            }
        }

        return row;
    }
}
