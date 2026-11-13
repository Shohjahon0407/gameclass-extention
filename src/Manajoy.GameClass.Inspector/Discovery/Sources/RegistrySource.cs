using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Manajoy.GameClass.Inspector.Discovery.Sources;

/// <summary>
/// Reads (never writes) SQL Server installation keys. Both registry views are checked because
/// MSDE 2000 is a 32-bit product whose keys live under WOW6432Node on 64-bit Windows.
/// </summary>
internal sealed class RegistrySource(ILogger<RegistrySource> logger) : ISqlInstanceSource
{
    private const string SqlServerRootKey = @"SOFTWARE\Microsoft\Microsoft SQL Server";
    private const string InstanceNamesKey = SqlServerRootKey + @"\Instance Names\SQL";
    private const string InstalledInstancesValue = "InstalledInstances";

    // SQL Server 2000 / MSDE layout.
    private const string LegacyDefaultInstanceVersionKey = @"SOFTWARE\Microsoft\MSSQLServer\MSSQLServer\CurrentVersion";
    private const string LegacyNamedInstanceVersionKeyFormat = SqlServerRootKey + @"\{0}\MSSQLServer\CurrentVersion";
    private const string LegacyVersionValue = "CurrentVersion";

    // SQL Server 2005+ layout.
    private const string SetupKeyFormat = SqlServerRootKey + @"\{0}\Setup";

    public string Name => "Registry";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsFallback => false;

    public IReadOnlyList<SqlInstanceInfo> Discover()
    {
        var results = new List<SqlInstanceInfo>();
        foreach (var (view, label) in RegistryViews())
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            var instanceIds = ReadInstanceIds(localMachine);
            var names = ReadInstalledInstances(localMachine).Union(instanceIds.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                var (version, edition) = ReadVersion(localMachine, name, instanceIds.GetValueOrDefault(name));
                results.Add(new SqlInstanceInfo
                {
                    InstanceName = name,
                    ServerName = SqlServerName.ForInstance(name),
                    ServiceName = SqlServerName.IsDefaultInstance(name) ? SqlServerName.DefaultInstanceName : $"MSSQL${name}",
                    Version = version,
                    Edition = edition,
                    DiscoverySources = [$"{Name} ({label})"],
                });
            }
        }

        return results;
    }

    private static IEnumerable<(RegistryView View, string Label)> RegistryViews()
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            yield return (RegistryView.Default, "32-bit");
            yield break;
        }

        yield return (RegistryView.Registry64, "64-bit");
        yield return (RegistryView.Registry32, "32-bit");
    }

    private static IEnumerable<string> ReadInstalledInstances(RegistryKey localMachine)
    {
        using var root = localMachine.OpenSubKey(SqlServerRootKey, writable: false);
        return root?.GetValue(InstalledInstancesValue) as string[] ?? [];
    }

    /// <summary>Instance name → instance id (e.g. "SQLEXPRESS" → "MSSQL10_50.SQLEXPRESS"), SQL Server 2005+.</summary>
    private static Dictionary<string, string> ReadInstanceIds(RegistryKey localMachine)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = localMachine.OpenSubKey(InstanceNamesKey, writable: false);
        if (key is null)
        {
            return result;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string id && !string.IsNullOrWhiteSpace(id))
            {
                result[name] = id;
            }
        }

        return result;
    }

    private (string? Version, string? Edition) ReadVersion(RegistryKey localMachine, string instanceName, string? instanceId)
    {
        try
        {
            if (instanceId is not null)
            {
                using var setup = localMachine.OpenSubKey(string.Format(null, SetupKeyFormat, instanceId), writable: false);
                var version = setup?.GetValue("PatchLevel") as string ?? setup?.GetValue("Version") as string;
                if (version is not null)
                {
                    return (version, setup?.GetValue("Edition") as string);
                }
            }

            var legacyKey = SqlServerName.IsDefaultInstance(instanceName)
                ? LegacyDefaultInstanceVersionKey
                : string.Format(null, LegacyNamedInstanceVersionKeyFormat, instanceName);
            using var legacy = localMachine.OpenSubKey(legacyKey, writable: false);
            return (legacy?.GetValue(LegacyVersionValue) as string, null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogWarning("Cannot read registry version of instance {Instance}: {Message}", instanceName, ex.Message);
            return (null, null);
        }
    }
}
