using Manajoy.GameClass.Inspector.Analysis;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Tests;

public class GameClassCandidateScorerTests
{
    private readonly GameClassCandidateScorer _scorer = new();

    [Fact]
    public void Club_like_schema_scores_high_and_finds_structures()
    {
        var schema = Schema("club",
            Table("tblComputers", ("Id", "int"), ("Number", "int"), ("IPAddress", "varchar"), ("State", "int"), ("GroupId", "int")),
            Table("Sessions", ("Id", "int"), ("ComputerId", "int"), ("StartTime", "datetime"), ("EndTime", "datetime"), ("TariffId", "int")),
            Table("Tariffs", ("Id", "int"), ("Name", "nvarchar"), ("PricePerHour", "money")),
            Table("Company", ("Id", "int"), ("Title", "nvarchar")));

        var candidate = _scorer.Score(schema);

        Assert.Equal(CandidateConfidence.High, candidate.Confidence);
        Assert.Contains(candidate.InterestingStructures, s => s.Category == "Computers" && s.ObjectName == "dbo.tblComputers");
        Assert.Contains(candidate.InterestingStructures, s => s.Category == "Sessions" && s.ObjectName == "dbo.Sessions");
        Assert.Contains(candidate.Reasons, reason => reason.Contains("network columns", StringComparison.Ordinal));
        Assert.DoesNotContain(candidate.InterestingStructures, s => s.ObjectName == "dbo.Company");
    }

    [Fact]
    public void Russian_names_are_recognised()
    {
        var schema = Schema("клуб",
            Table("Компьютеры", ("Ид", "int"), ("IP", "varchar")),
            Table("Сеансы", ("Начало", "datetime"), ("Конец", "datetime")),
            Table("Тарифы", ("Цена", "money")));

        Assert.Equal(CandidateConfidence.High, _scorer.Score(schema).Confidence);
    }

    [Fact]
    public void Unrelated_schema_scores_none()
    {
        var schema = Schema("accounting",
            Table("Ledger", ("Id", "int"), ("Debit", "decimal")),
            Table("Company", ("Id", "int"), ("Title", "nvarchar")));

        var candidate = _scorer.Score(schema);

        Assert.Equal(CandidateConfidence.None, candidate.Confidence);
        Assert.Empty(candidate.InterestingStructures);
    }

    [Fact]
    public void Database_name_alone_never_exceeds_low()
    {
        var schema = Schema("GameClass", Table("Settings", ("Key", "nvarchar"), ("Value", "nvarchar")));

        var candidate = _scorer.Score(schema);

        Assert.Equal(CandidateConfidence.Low, candidate.Confidence);
        Assert.Contains(candidate.Reasons, reason => reason.Contains("name alone is not evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_named_categories_score_medium()
    {
        var schema = Schema("shop", Table("Clients", ("Id", "int")), Table("Payments", ("Amount", "money")));

        Assert.Equal(CandidateConfidence.Medium, _scorer.Score(schema).Confidence);
    }

    private static DatabaseSchema Schema(string database, params TableInfo[] tables) => new()
    {
        Server = ".",
        Database = database,
        Tables = tables,
    };

    private static TableInfo Table(string name, params (string Name, string Type)[] columns) => new()
    {
        Schema = "dbo",
        Name = name,
        Columns = columns
            .Select((column, index) => new ColumnInfo { Name = column.Name, DataType = column.Type, Ordinal = index + 1, IsNullable = true })
            .ToList(),
    };
}
