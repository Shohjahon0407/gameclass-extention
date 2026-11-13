using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Discovery;

public sealed record ConnectionProbeResult
{
    public required bool Success { get; init; }

    public string? Provider { get; init; }

    public string? Authentication { get; init; }

    public ServerInfo? Server { get; init; }

    public IReadOnlyList<ConnectionAttempt> Attempts { get; init; } = [];

    public string? FailureReason { get; init; }

    /// <summary>What the operator can do about a failure, without the tool changing anything itself.</summary>
    public string? Hint { get; init; }
}

public sealed record ConnectionAttempt(string Provider, string Authentication, bool Succeeded, string? Error);

/// <summary>An instance the inspector could log into, with the way it got in.</summary>
internal sealed record ConnectedInstance(SqlInstanceInfo Instance, ConnectionTarget Target)
{
    public string ServerName => Instance.ServerName;
}
