namespace Manajoy.GameClass.Inspector.Models;

public enum CandidateConfidence
{
    None,
    Low,
    Medium,
    High,
}

public sealed record GameClassCandidate
{
    public required string Server { get; init; }

    public required string Database { get; init; }

    public required CandidateConfidence Confidence { get; init; }

    public required int Score { get; init; }

    public int TableCount { get; init; }

    public int ViewCount { get; init; }

    public int RoutineCount { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<InterestingStructure> InterestingStructures { get; init; } = [];
}

/// <summary>The object that best resembles a domain concept, e.g. the "Computers-like table".</summary>
public sealed record InterestingStructure
{
    public required string Category { get; init; }

    public required string ObjectName { get; init; }

    public required string ObjectType { get; init; }

    public long? ApproximateRowCount { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyList<string> OtherMatches { get; init; } = [];
}
