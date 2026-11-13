using System.Reflection;
using Manajoy.GameClass.Inspector.Infrastructure;

namespace Manajoy.GameClass.Inspector.Tests;

public class ReadOnlyQueryGuardTests
{
    public static TheoryData<string> AllCatalogQueries()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(SqlQueries).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(field => field.IsLiteral && field.FieldType == typeof(string)))
        {
            data.Add(field.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCatalogQueries))]
    public void Every_catalog_query_passes_the_guard(string fieldName)
    {
        var sql = (string)typeof(SqlQueries).GetField(fieldName)!.GetRawConstantValue()!;

        Assert.True(ReadOnlyQueryGuard.IsReadOnly(sql, out var reason), $"{fieldName}: {reason}");
    }

    [Fact]
    public void Sample_query_with_hostile_identifiers_passes_and_stays_quoted()
    {
        var sql = SqlQueries.BuildSampleQuery("dbo", "Update", ["Delete", "Set", "x]; DROP TABLE t; --"], 5);

        Assert.True(ReadOnlyQueryGuard.IsReadOnly(sql, out var reason), reason);
        Assert.Contains("[x]]; DROP TABLE t; --]", sql);
        Assert.StartsWith("SELECT TOP 5 ", sql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(1000)]
    public void Sample_query_rejects_row_limits_outside_the_hard_limit(int rowLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SqlQueries.BuildSampleQuery("dbo", "T", ["Id"], rowLimit));
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("MERGE t USING s ON 1 = 1 WHEN MATCHED THEN DELETE")]
    [InlineData("DROP TABLE t")]
    [InlineData("ALTER TABLE t ADD c int")]
    [InlineData("CREATE TABLE t (id int)")]
    [InlineData("TRUNCATE TABLE t")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("SELECT * INTO copy FROM t")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'x', 'SELECT 1')")]
    [InlineData("SELECT 1 /* comment */ ; TRUNCATE TABLE t")]
    [InlineData("WITH x AS (SELECT 1 AS a) DELETE FROM t")]
    [InlineData("SELECT name FROM master.dbo.sysdatabases WHERE 1 = 1 UNION SELECT xp_cmdshell")]
    [InlineData("SELECT 1 WAITFOR DELAY '00:00:10'")]
    [InlineData("  ")]
    [InlineData("SELECT [unterminated FROM t")]
    [InlineData("SELECT 'unterminated FROM t")]
    public void Write_or_multi_statement_sql_is_rejected(string sql)
    {
        Assert.False(ReadOnlyQueryGuard.IsReadOnly(sql, out _));
        Assert.Throws<ReadOnlyViolationException>(() => ReadOnlyQueryGuard.EnsureReadOnly(sql));
    }

    [Theory]
    [InlineData("SELECT [Update], [Delete] FROM [Set]")]
    [InlineData("SELECT 'DROP TABLE t' AS Text")]
    [InlineData("SELECT KEY_COLUMN_USAGE FROM x -- DROP TABLE t")]
    public void Keywords_inside_identifiers_literals_or_comments_do_not_trigger(string sql)
    {
        Assert.True(ReadOnlyQueryGuard.IsReadOnly(sql, out var reason), reason);
    }
}
