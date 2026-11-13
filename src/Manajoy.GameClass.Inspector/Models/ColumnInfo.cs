using System.Text.Json.Serialization;

namespace Manajoy.GameClass.Inspector.Models;

public sealed record ColumnInfo
{
    public required string Name { get; init; }

    public required int Ordinal { get; init; }

    public required string DataType { get; init; }

    public required bool IsNullable { get; init; }

    /// <summary>Character length; -1 means MAX.</summary>
    public int? MaxLength { get; init; }

    public int? NumericPrecision { get; init; }

    public int? NumericScale { get; init; }

    public bool IsPrimaryKey { get; init; }

    [JsonIgnore]
    public string TypeDisplay => DataType.ToLowerInvariant() switch
    {
        "decimal" or "numeric" when NumericPrecision is not null => $"{DataType}({NumericPrecision},{NumericScale ?? 0})",
        _ when MaxLength == -1 => $"{DataType}(max)",
        _ when MaxLength is not null => $"{DataType}({MaxLength})",
        _ => DataType,
    };
}
