using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>Reads the column metadata of every table and view in a database with a single query.</summary>
internal sealed class ColumnInspector(ReadOnlyQueryExecutor executor)
{
    private const string NullableYes = "YES";

    public async Task<ColumnLookup> GetColumnsAsync(ConnectionTarget target, string database, CancellationToken cancellationToken)
    {
        var rows = await executor.QueryAsync(
            target,
            database,
            SqlQueries.Columns,
            reader => (
                Key: ColumnLookup.Key(reader.GetRequiredString("SchemaName"), reader.GetRequiredString("ObjectName")),
                Column: new ColumnInfo
                {
                    Name = reader.GetRequiredString("ColumnName"),
                    Ordinal = reader.GetInt32OrNull("Ordinal") ?? 0,
                    DataType = reader.GetRequiredString("DataType"),
                    IsNullable = string.Equals(reader.GetStringOrNull("IsNullable"), NullableYes, StringComparison.OrdinalIgnoreCase),
                    MaxLength = reader.GetInt32OrNull("MaxLength"),
                    NumericPrecision = reader.GetInt32OrNull("NumericPrecision"),
                    NumericScale = reader.GetInt32OrNull("NumericScale"),
                }),
            cancellationToken);

        return new ColumnLookup(rows
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ColumnInfo>)group.Select(row => row.Column).OrderBy(column => column.Ordinal).ToList(),
                StringComparer.OrdinalIgnoreCase));
    }
}

internal sealed class ColumnLookup(IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns)
{
    public static ColumnLookup Empty { get; } = new(new Dictionary<string, IReadOnlyList<ColumnInfo>>());

    public IReadOnlyList<ColumnInfo> For(string schema, string name) => columns.GetValueOrDefault(Key(schema, name)) ?? [];

    public static string Key(string schema, string name) => $"{schema}\u001F{name}";
}
