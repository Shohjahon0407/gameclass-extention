using System.Globalization;
using Manajoy.GameClass.Inspector.Configuration;

namespace Manajoy.GameClass.Inspector.Infrastructure;

/// <summary>
/// Every SQL statement the inspector can run. All of them are metadata SELECTs written to work
/// unchanged on SQL Server 2000 (MSDE) through current versions: INFORMATION_SCHEMA views,
/// compatibility views (sysdatabases, sysindexes, sysfiles) and built-in functions only.
/// sql_variant results are CAST so both SqlClient and ODBC read them as plain strings/ints.
/// </summary>
internal static class SqlQueries
{
    public const string Ping = "SELECT 1";

    public const string ServerInfo = """
        SELECT
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion,
            CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128)) AS ProductLevel,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition,
            CAST(SERVERPROPERTY('EngineEdition') AS int) AS EngineEdition,
            CAST(@@SERVERNAME AS nvarchar(256)) AS ServerName,
            CAST(SUSER_SNAME() AS nvarchar(256)) AS CurrentLogin
        """;

    public const string Databases = """
        SELECT
            d.name AS DatabaseName,
            CAST(DATABASEPROPERTYEX(d.name, 'Status') AS nvarchar(60)) AS State,
            CAST(d.cmptlevel AS int) AS CompatibilityLevel,
            HAS_DBACCESS(d.name) AS HasAccess
        FROM master.dbo.sysdatabases d
        ORDER BY d.name
        """;

    /// <summary>Runs inside the target database.</summary>
    public const string DatabaseSize = """
        SELECT CAST(SUM(CAST(f.size AS bigint)) * 8 / 1024.0 AS decimal(18, 2)) AS SizeMb
        FROM sysfiles f
        """;

    /// <summary>Row counts come from index metadata (sysindexes), never from scanning tables.</summary>
    public const string Tables = """
        SELECT
            t.TABLE_SCHEMA AS SchemaName,
            t.TABLE_NAME AS TableName,
            (SELECT MAX(CAST(i.rows AS bigint))
               FROM sysindexes i
              WHERE i.id = OBJECT_ID(QUOTENAME(t.TABLE_SCHEMA) + '.' + QUOTENAME(t.TABLE_NAME))
                AND i.indid IN (0, 1)) AS ApproxRows
        FROM INFORMATION_SCHEMA.TABLES t
        WHERE t.TABLE_TYPE = 'BASE TABLE'
          AND ISNULL(OBJECTPROPERTY(OBJECT_ID(QUOTENAME(t.TABLE_SCHEMA) + '.' + QUOTENAME(t.TABLE_NAME)), 'IsMSShipped'), 0) = 0
        ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
        """;

    /// <summary>Fallback when sysindexes is not readable for the current login.</summary>
    public const string TablesWithoutRowCounts = """
        SELECT
            t.TABLE_SCHEMA AS SchemaName,
            t.TABLE_NAME AS TableName,
            CAST(NULL AS bigint) AS ApproxRows
        FROM INFORMATION_SCHEMA.TABLES t
        WHERE t.TABLE_TYPE = 'BASE TABLE'
          AND ISNULL(OBJECTPROPERTY(OBJECT_ID(QUOTENAME(t.TABLE_SCHEMA) + '.' + QUOTENAME(t.TABLE_NAME)), 'IsMSShipped'), 0) = 0
        ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
        """;

    public const string Views = """
        SELECT
            v.TABLE_SCHEMA AS SchemaName,
            v.TABLE_NAME AS ViewName,
            v.VIEW_DEFINITION AS Definition
        FROM INFORMATION_SCHEMA.VIEWS v
        WHERE ISNULL(OBJECTPROPERTY(OBJECT_ID(QUOTENAME(v.TABLE_SCHEMA) + '.' + QUOTENAME(v.TABLE_NAME)), 'IsMSShipped'), 0) = 0
        ORDER BY v.TABLE_SCHEMA, v.TABLE_NAME
        """;

    public const string Columns = """
        SELECT
            c.TABLE_SCHEMA AS SchemaName,
            c.TABLE_NAME AS ObjectName,
            c.COLUMN_NAME AS ColumnName,
            CAST(c.ORDINAL_POSITION AS int) AS Ordinal,
            c.DATA_TYPE AS DataType,
            c.IS_NULLABLE AS IsNullable,
            CAST(c.CHARACTER_MAXIMUM_LENGTH AS int) AS MaxLength,
            CAST(c.NUMERIC_PRECISION AS int) AS NumericPrecision,
            CAST(c.NUMERIC_SCALE AS int) AS NumericScale
        FROM INFORMATION_SCHEMA.COLUMNS c
        ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    public const string PrimaryKeys = """
        SELECT
            k.TABLE_SCHEMA AS SchemaName,
            k.TABLE_NAME AS TableName,
            k.COLUMN_NAME AS ColumnName,
            CAST(k.ORDINAL_POSITION AS int) AS Ordinal
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
          ON k.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
         AND k.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
         AND k.TABLE_SCHEMA = tc.TABLE_SCHEMA
         AND k.TABLE_NAME = tc.TABLE_NAME
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        ORDER BY k.TABLE_SCHEMA, k.TABLE_NAME, k.ORDINAL_POSITION
        """;

    public const string ForeignKeys = """
        SELECT
            rc.CONSTRAINT_NAME AS ForeignKeyName,
            fk.TABLE_SCHEMA AS SchemaName,
            fk.TABLE_NAME AS TableName,
            fk.COLUMN_NAME AS ColumnName,
            pk.TABLE_SCHEMA AS ReferencedSchema,
            pk.TABLE_NAME AS ReferencedTable,
            pk.COLUMN_NAME AS ReferencedColumn,
            CAST(fk.ORDINAL_POSITION AS int) AS Ordinal
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk
          ON fk.CONSTRAINT_SCHEMA = rc.CONSTRAINT_SCHEMA
         AND fk.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
        LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE pk
          ON pk.CONSTRAINT_SCHEMA = rc.UNIQUE_CONSTRAINT_SCHEMA
         AND pk.CONSTRAINT_NAME = rc.UNIQUE_CONSTRAINT_NAME
         AND pk.ORDINAL_POSITION = fk.ORDINAL_POSITION
        ORDER BY fk.TABLE_SCHEMA, fk.TABLE_NAME, rc.CONSTRAINT_NAME, fk.ORDINAL_POSITION
        """;

    public const string Routines = """
        SELECT
            r.ROUTINE_SCHEMA AS SchemaName,
            r.ROUTINE_NAME AS RoutineName,
            r.ROUTINE_TYPE AS RoutineType
        FROM INFORMATION_SCHEMA.ROUTINES r
        WHERE ISNULL(OBJECTPROPERTY(OBJECT_ID(QUOTENAME(r.ROUTINE_SCHEMA) + '.' + QUOTENAME(r.ROUTINE_NAME)), 'IsMSShipped'), 0) = 0
        ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_NAME
        """;

    /// <summary>
    /// Builds the only data-reading statement. Identifiers must come from server metadata and are
    /// bracket-quoted; TOP is a validated literal (SQL Server 2000 does not accept TOP (@n)).
    /// NOLOCK keeps the read from blocking the live GameClass workload.
    /// </summary>
    public static string BuildSampleQuery(string schema, string table, IReadOnlyList<string> columns, int rowLimit)
    {
        if (rowLimit is < 1 or > InspectorOptions.SampleRowsHardLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(rowLimit), rowLimit, "Sample row limit is outside the allowed range.");
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        var columnList = string.Join(", ", columns.Select(SqlIdentifier.Quote));
        var top = rowLimit.ToString(CultureInfo.InvariantCulture);
        return $"SELECT TOP {top} {columnList} FROM {SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(table)} WITH (NOLOCK)";
    }
}

internal static class SqlIdentifier
{
    /// <summary>Equivalent of T-SQL QUOTENAME: wraps in brackets and doubles closing brackets.</summary>
    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
