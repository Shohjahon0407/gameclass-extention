using Manajoy.GameClass.Inspector.Discovery.Sources;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Discovery;

/// <summary>Runs every instance source in isolation and merges what they find into one list per instance.</summary>
internal sealed class SqlInstanceDiscovery(
    IEnumerable<ISqlInstanceSource> sources,
    InspectionErrors errors,
    ILogger<SqlInstanceDiscovery> logger)
{
    public IReadOnlyList<SqlInstanceInfo> Discover(CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, SqlInstanceInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources.Where(source => !source.IsFallback))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collect(source, found);
        }

        if (found.Count == 0)
        {
            logger.LogInformation("No instance found by services, registry or configuration; trying common local names");
            foreach (var source in sources.Where(source => source.IsFallback))
            {
                Collect(source, found);
            }
        }

        logger.LogInformation("Discovered {Count} SQL Server instance(s)", found.Count);
        return found.Values.OrderBy(instance => instance.ServerName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void Collect(ISqlInstanceSource source, Dictionary<string, SqlInstanceInfo> found)
    {
        if (!source.IsSupported)
        {
            logger.LogInformation("Discovery via {Source} skipped: not supported on this OS", source.Name);
            return;
        }

        try
        {
            var instances = source.Discover();
            logger.LogInformation("Discovery via {Source}: {Count} instance(s)", source.Name, instances.Count);
            foreach (var instance in instances)
            {
                found[instance.ServerName] = found.TryGetValue(instance.ServerName, out var existing)
                    ? Merge(existing, instance)
                    : instance;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discovery via {Source} failed", source.Name);
            errors.Warning(InspectionStage.InstanceDiscovery, source.Name, ex.Message);
        }
    }

    private static SqlInstanceInfo Merge(SqlInstanceInfo existing, SqlInstanceInfo other) => existing with
    {
        ServiceName = existing.ServiceName ?? other.ServiceName,
        ServiceStatus = existing.ServiceStatus ?? other.ServiceStatus,
        IsRunning = existing.IsRunning ?? other.IsRunning,
        Version = existing.Version ?? other.Version,
        Edition = existing.Edition ?? other.Edition,
        DiscoverySources = existing.DiscoverySources.Union(other.DiscoverySources, StringComparer.OrdinalIgnoreCase).ToList(),
    };
}
