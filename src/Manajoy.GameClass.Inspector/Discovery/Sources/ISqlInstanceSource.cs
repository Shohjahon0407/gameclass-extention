namespace Manajoy.GameClass.Inspector.Discovery.Sources;

/// <summary>One independent way of finding local SQL Server instances. A failing source never stops the others.</summary>
internal interface ISqlInstanceSource
{
    string Name { get; }

    bool IsSupported { get; }

    /// <summary>Fallback sources are consulted only when the primary sources found nothing.</summary>
    bool IsFallback { get; }

    IReadOnlyList<SqlInstanceInfo> Discover();
}
