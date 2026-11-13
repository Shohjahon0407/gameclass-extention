namespace Manajoy.GameClass.Inspector.Discovery.Sources;

/// <summary>Well-known local names, tried only when nothing else found an instance.</summary>
internal sealed class CommonCandidateSource : ISqlInstanceSource
{
    private static readonly string[] Candidates = ["(local)", @"(local)\SQLEXPRESS"];

    public string Name => "Common local candidate";

    public bool IsSupported => true;

    public bool IsFallback => true;

    public IReadOnlyList<SqlInstanceInfo> Discover() =>
        Candidates
            .Select(SqlServerName.Normalize)
            .Select(server => new SqlInstanceInfo
            {
                InstanceName = SqlServerName.InstanceNameOf(server),
                ServerName = server,
                DiscoverySources = [Name],
            })
            .ToList();
}
