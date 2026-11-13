using System.Data;
using System.Data.Common;
using System.Globalization;
using Manajoy.GameClass.Inspector.Configuration;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Infrastructure;

/// <summary>
/// The only place in the application that creates and executes database commands.
/// Every statement passes <see cref="ReadOnlyQueryGuard"/> before a connection is even opened,
/// and no transaction is ever started.
/// </summary>
internal sealed class ReadOnlyQueryExecutor(SqlConnectionFactory connectionFactory, IOptions<InspectorOptions> options)
{
    private readonly int _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        ConnectionTarget target,
        string? database,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        ReadOnlyQueryGuard.EnsureReadOnly(sql);

        await using var connection = connectionFactory.Create(target, database);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _commandTimeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }
}

/// <summary>Provider-neutral readers: SqlClient and ODBC return different CLR types for the same SQL type.</summary>
internal static class DataReaderExtensions
{
    public static string? GetStringOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => Convert.ToString(value, CultureInfo.InvariantCulture));

    public static string GetRequiredString(this DbDataReader reader, string column) =>
        reader.GetStringOrNull(column) ?? throw new InvalidOperationException($"Column '{column}' returned NULL.");

    public static int? GetInt32OrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (int?)Convert.ToInt32(value, CultureInfo.InvariantCulture));

    public static long? GetInt64OrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (long?)Convert.ToInt64(value, CultureInfo.InvariantCulture));

    public static decimal? GetDecimalOrNull(this DbDataReader reader, string column) =>
        Read(reader, column, value => (decimal?)Convert.ToDecimal(value, CultureInfo.InvariantCulture));

    private static T? Read<T>(DbDataReader reader, string column, Func<object, T?> convert)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value is DBNull or null ? default : convert(value);
    }
}
