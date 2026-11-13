using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Analysis;

/// <summary>
/// Heuristically rates how much a database looks like a gaming-club management database,
/// using only object and column names plus column types. It never assumes a concrete schema.
/// </summary>
internal sealed class GameClassCandidateScorer
{
    private const int ObjectNameWeight = 3;
    private const int ColumnNameWeight = 1;
    private const int MaxObjectHitsPerCategory = 3;
    private const int MaxColumnHitsPerCategory = 5;
    private const int StructuralBonus = 2;
    private const int DatabaseNameBonus = 1;
    private const int MaxNamesInReason = 5;
    private const int MaxColumnsInStructure = 15;
    private const int MinTimeColumnsForSessions = 2;

    private static readonly HashSet<string> DateTimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "datetime", "smalldatetime", "datetime2", "datetimeoffset", "date", "time",
    };

    private static readonly HashSet<string> MoneyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "money", "smallmoney", "decimal", "numeric", "float", "real",
    };

    public GameClassCandidate Score(DatabaseSchema schema)
    {
        var objects = schema.Tables.Select(SchemaObject.From)
            .Concat(schema.Views.Select(SchemaObject.From))
            .ToList();

        var objectMatches = MatchObjects(objects);
        var columnMatches = MatchColumns(objects);

        var score = 0;
        var reasons = new List<string>();

        foreach (var category in DomainKeywords.AllCategories)
        {
            if (objectMatches.TryGetValue(category, out var matchedObjects))
            {
                score += Math.Min(matchedObjects.Count, MaxObjectHitsPerCategory) * ObjectNameWeight;
                reasons.Add($"Objects resembling {DomainKeywords.Describe(category)} data: {JoinLimited(matchedObjects.Select(o => o.FullName))}");
            }

            if (columnMatches.TryGetValue(category, out var matchedColumns))
            {
                score += Math.Min(matchedColumns.Count, MaxColumnHitsPerCategory) * ColumnNameWeight;
                reasons.Add($"{matchedColumns.Count} {DomainKeywords.Describe(category)}-related column name(s), e.g. {JoinLimited(matchedColumns)}");
            }
        }

        var structures = new List<InterestingStructure>();
        foreach (var (category, candidates) in objectMatches.OrderBy(pair => pair.Key))
        {
            var ranked = candidates
                .OrderByDescending(candidate => Rank(category, candidate))
                .ThenByDescending(candidate => candidate.ApproximateRowCount ?? 0)
                .ThenBy(candidate => candidate.FullName.Length)
                .ToList();
            var best = ranked[0];

            var signal = StructuralSignal(category, best);
            if (signal is not null)
            {
                score += StructuralBonus;
                reasons.Add($"{Capitalize(DomainKeywords.Describe(category))}-like {best.ObjectType} {best.FullName} {signal}");
            }

            structures.Add(new InterestingStructure
            {
                Category = category.ToString(),
                ObjectName = best.FullName,
                ObjectType = best.ObjectType,
                ApproximateRowCount = best.ApproximateRowCount,
                Columns = best.Columns.Take(MaxColumnsInStructure).Select(DescribeColumn).ToList(),
                OtherMatches = ranked.Skip(1).Take(MaxNamesInReason).Select(candidate => candidate.FullName).ToList(),
            });
        }

        if (DomainKeywords.MentionsGameClass(schema.Database))
        {
            score += DatabaseNameBonus;
            reasons.Add($"Database name '{schema.Database}' mentions GameClass (name alone is not evidence)");
        }

        return new GameClassCandidate
        {
            Server = schema.Server,
            Database = schema.Database,
            Confidence = Classify(objectMatches.Keys.ToHashSet(), score),
            Score = score,
            TableCount = schema.Tables.Count,
            ViewCount = schema.Views.Count,
            RoutineCount = schema.Routines.Count,
            Reasons = reasons,
            InterestingStructures = structures,
        };
    }

    internal static CandidateConfidence Classify(IReadOnlySet<DomainCategory> namedCategories, int score)
    {
        var hasCoreTriple = namedCategories.Contains(DomainCategory.Computers)
            && namedCategories.Contains(DomainCategory.Sessions)
            && (namedCategories.Contains(DomainCategory.Tariffs)
                || namedCategories.Contains(DomainCategory.Clients)
                || namedCategories.Contains(DomainCategory.Groups));

        if (hasCoreTriple)
        {
            return CandidateConfidence.High;
        }

        if (namedCategories.Count >= 2)
        {
            return CandidateConfidence.Medium;
        }

        return score > 0 ? CandidateConfidence.Low : CandidateConfidence.None;
    }

    private static Dictionary<DomainCategory, List<SchemaObject>> MatchObjects(IEnumerable<SchemaObject> objects)
    {
        var matches = new Dictionary<DomainCategory, List<SchemaObject>>();
        foreach (var schemaObject in objects)
        {
            foreach (var category in DomainKeywords.Match(schemaObject.Name))
            {
                if (!matches.TryGetValue(category, out var list))
                {
                    matches[category] = list = [];
                }

                list.Add(schemaObject);
            }
        }

        return matches;
    }

    private static Dictionary<DomainCategory, List<string>> MatchColumns(IEnumerable<SchemaObject> objects)
    {
        var matches = new Dictionary<DomainCategory, List<string>>();
        var columnNames = objects.SelectMany(o => o.Columns).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var columnName in columnNames)
        {
            foreach (var category in DomainKeywords.Match(columnName))
            {
                if (!matches.TryGetValue(category, out var list))
                {
                    matches[category] = list = [];
                }

                list.Add(columnName);
            }
        }

        return matches;
    }

    /// <summary>Prefers base tables with a supporting structural signal and a short, specific name.</summary>
    private static int Rank(DomainCategory category, SchemaObject candidate)
    {
        var rank = 0;
        if (StructuralSignal(category, candidate) is not null)
        {
            rank += 4;
        }

        if (candidate.IsTable)
        {
            rank += 2;
        }

        if (NameTokenizer.Tokenize(candidate.Name).Count <= 2)
        {
            rank += 1;
        }

        return rank;
    }

    /// <summary>Column evidence that the object really is what its name suggests; null when there is none.</summary>
    private static string? StructuralSignal(DomainCategory category, SchemaObject candidate)
    {
        switch (category)
        {
            case DomainCategory.Computers:
                var network = candidate.Columns.Where(c => DomainKeywords.IsNetworkColumn(c.Name)).Select(c => c.Name).ToList();
                return network.Count > 0 ? $"has network columns ({string.Join(", ", network)})" : null;

            case DomainCategory.Sessions:
            case DomainCategory.Reservations:
                var temporal = candidate.Columns
                    .Where(c => DateTimeTypes.Contains(c.DataType) || DomainKeywords.IsTimeColumn(c.Name))
                    .Select(c => c.Name)
                    .ToList();
                return temporal.Count >= MinTimeColumnsForSessions ? $"has time-range columns ({string.Join(", ", temporal)})" : null;

            case DomainCategory.Tariffs:
            case DomainCategory.Payments:
                var money = candidate.Columns
                    .Where(c => MoneyTypes.Contains(c.DataType) || DomainKeywords.IsPriceColumn(c.Name))
                    .Select(c => c.Name)
                    .ToList();
                return money.Count > 0 ? $"has amount columns ({string.Join(", ", money)})" : null;

            default:
                return null;
        }
    }

    private static string DescribeColumn(ColumnInfo column) =>
        column.IsPrimaryKey ? $"{column.Name} {column.TypeDisplay} PK" : $"{column.Name} {column.TypeDisplay}";

    private static string JoinLimited(IEnumerable<string> names)
    {
        var list = names.ToList();
        var shown = string.Join(", ", list.Take(MaxNamesInReason));
        return list.Count > MaxNamesInReason ? $"{shown} (+{list.Count - MaxNamesInReason} more)" : shown;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private sealed record SchemaObject(string Schema, string Name, string ObjectType, long? ApproximateRowCount, IReadOnlyList<ColumnInfo> Columns)
    {
        private const string TableType = "table";
        private const string ViewType = "view";

        public string FullName => $"{Schema}.{Name}";

        public bool IsTable => ObjectType == TableType;

        public static SchemaObject From(TableInfo table) => new(table.Schema, table.Name, TableType, table.ApproximateRowCount, table.Columns);

        public static SchemaObject From(ViewInfo view) => new(view.Schema, view.Name, ViewType, null, view.Columns);
    }
}
