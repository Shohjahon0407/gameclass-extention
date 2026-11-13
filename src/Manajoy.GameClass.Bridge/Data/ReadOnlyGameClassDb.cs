using System.Data;
using System.Data.Common;
using System.Globalization;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Inspector.Infrastructure;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Data;

/// <summary>
/// The only place in the bridge that executes SQL. Every statement passes the shared
/// <see cref="ReadOnlyQueryGuard"/> before it is sent, and no transaction is ever opened.
/// </summary>
internal sealed class ReadOnlyGameClassDb(GameClassConnectionFactory connections, IOptions<BridgeOptions> options)
{
    private readonly int _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;

    /// <summary>Runs several SELECTs on one connection (one round of polling).</summary>
    public async Task<T> WithConnectionAsync<T>(Func<QueryScope, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await work(new QueryScope(connection, _commandTimeoutSeconds));
    }

    internal sealed class QueryScope(DbConnection connection, int commandTimeoutSeconds)
    {
        public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> map, CancellationToken cancellationToken)
        {
            ReadOnlyQueryGuard.EnsureReadOnly(sql);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = commandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
    }
}

/// <summary>Provider-neutral readers: SqlClient and ODBC return different CLR types for the same SQL type.</summary>
internal static class DataReaderExtensions
{
    public static string? GetStringOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => Convert.ToString(value, CultureInfo.InvariantCulture)?.TrimEnd());

    public static string GetStringOrEmpty(this DbDataReader reader, string column) =>
        reader.GetStringOrNull(column) ?? string.Empty;

    public static int GetInt32(this DbDataReader reader, string column) =>
        reader.GetInt32OrNull(column) ?? throw new InvalidOperationException($"Column '{column}' returned NULL.");

    public static int? GetInt32OrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (int?)Convert.ToInt32(value, CultureInfo.InvariantCulture));

    public static decimal? GetDecimalOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (decimal?)Convert.ToDecimal(value, CultureInfo.InvariantCulture));

    public static double? GetDoubleOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (double?)Convert.ToDouble(value, CultureInfo.InvariantCulture));

    public static DateTime? GetDateTimeOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (DateTime?)Convert.ToDateTime(value, CultureInfo.InvariantCulture));

    private static T? Read<T>(DbDataReader reader, string column, Func<object, T?> convert)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value is DBNull or null ? default : convert(value);
    }
}
