using System.Text.Json.Serialization;

namespace Manajoy.GameClass.Inspector.Discovery;

public sealed record SqlInstanceInfo
{
    /// <summary>"MSSQLSERVER" for the default instance, otherwise the instance name.</summary>
    public required string InstanceName { get; init; }

    /// <summary>Name used to connect, e.g. "." or ".\GAMECLASS".</summary>
    public required string ServerName { get; init; }

    public string? ServiceName { get; init; }

    public string? ServiceStatus { get; init; }

    /// <summary>Null when the service state is unknown (e.g. instance came from configuration).</summary>
    public bool? IsRunning { get; init; }

    /// <summary>Version recorded in the registry, before connecting.</summary>
    public string? Version { get; init; }

    public string? Edition { get; init; }

    public IReadOnlyList<string> DiscoverySources { get; init; } = [];

    public ConnectionProbeResult? Connection { get; init; }

    [JsonIgnore]
    public int? RegistryMajorVersion => SqlVersion.ParseMajor(Version);
}
