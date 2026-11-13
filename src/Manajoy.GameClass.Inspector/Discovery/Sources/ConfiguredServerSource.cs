using Manajoy.GameClass.Inspector.Configuration;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Discovery.Sources;

/// <summary>Servers the engineer listed explicitly in Inspection:AdditionalServers.</summary>
internal sealed class ConfiguredServerSource(IOptions<InspectorOptions> options) : ISqlInstanceSource
{
    public string Name => "Configuration";

    public bool IsSupported => true;

    public bool IsFallback => false;

    public IReadOnlyList<SqlInstanceInfo> Discover() =>
        options.Value.AdditionalServers
            .Select(SqlServerName.Normalize)
            .Select(server => new SqlInstanceInfo
            {
                InstanceName = SqlServerName.InstanceNameOf(server),
                ServerName = server,
                DiscoverySources = [Name],
            })
            .ToList();
}
