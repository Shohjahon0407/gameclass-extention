using System.Text.Json.Serialization;
using Manajoy.GameClass.Inspector.Discovery;

namespace Manajoy.GameClass.Inspector.Models;

public sealed record InspectionResult
{
    public required EnvironmentInfo Environment { get; init; }

    public required IReadOnlyList<SqlInstanceInfo> SqlInstances { get; init; }

    public required IReadOnlyList<DatabaseInfo> Databases { get; init; }

    [JsonPropertyName("gameclass_candidates")]
    public required IReadOnlyList<GameClassCandidate> GameClassCandidates { get; init; }

    /// <summary>Full schema of every database whose confidence is at least Low.</summary>
    public required IReadOnlyList<DatabaseSchema> Schemas { get; init; }

    /// <summary>Present only when sampling is enabled in configuration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SampleRowsResult>? SampleRows { get; init; }

    public required IReadOnlyList<InspectionError> Errors { get; init; }
}
