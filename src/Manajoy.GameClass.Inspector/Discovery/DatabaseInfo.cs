namespace Manajoy.GameClass.Inspector.Discovery;

public sealed record DatabaseInfo
{
    public required string Server { get; init; }

    public required string DatabaseName { get; init; }

    public string? State { get; init; }

    public int? CompatibilityLevel { get; init; }

    public decimal? SizeMb { get; init; }

    public bool IsAccessible { get; init; }

    public string? AccessError { get; init; }
}
