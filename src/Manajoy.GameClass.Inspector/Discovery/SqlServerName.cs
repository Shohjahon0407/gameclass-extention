namespace Manajoy.GameClass.Inspector.Discovery;

/// <summary>Canonical local server names, so the same instance found by several sources is probed once.</summary>
internal static class SqlServerName
{
    public const string DefaultInstanceName = "MSSQLSERVER";
    public const string LocalHost = ".";

    private static readonly string[] LocalHostAliases = ["(local)", "localhost", ".", "127.0.0.1"];

    public static bool IsDefaultInstance(string? instanceName) =>
        string.IsNullOrEmpty(instanceName) || instanceName.Equals(DefaultInstanceName, StringComparison.OrdinalIgnoreCase);

    public static string ForInstance(string instanceName) =>
        IsDefaultInstance(instanceName) ? LocalHost : $@"{LocalHost}\{instanceName}";

    /// <summary>
    /// "(local)", "localhost", the machine name → "."; "MSSQLSERVER" → default instance.
    /// Names with a protocol prefix ("np:", "tcp:") or a port ("host,1433") are kept exactly as typed.
    /// </summary>
    public static string Normalize(string server)
    {
        var trimmed = server.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal) || trimmed.Contains(',', StringComparison.Ordinal))
        {
            return trimmed;
        }

        var separator = trimmed.IndexOf('\\', StringComparison.Ordinal);
        var host = separator < 0 ? trimmed : trimmed[..separator];
        var instance = separator < 0 ? null : trimmed[(separator + 1)..];

        if (IsLocalHost(host))
        {
            host = LocalHost;
        }

        return IsDefaultInstance(instance) ? host : $@"{host}\{instance}";
    }

    public static string InstanceNameOf(string normalizedServer)
    {
        var separator = normalizedServer.IndexOf('\\', StringComparison.Ordinal);
        if (separator >= 0)
        {
            return normalizedServer[(separator + 1)..];
        }

        return normalizedServer == LocalHost ? DefaultInstanceName : normalizedServer;
    }

    public static bool AreSame(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalHost(string host) =>
        LocalHostAliases.Contains(host, StringComparer.OrdinalIgnoreCase)
        || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
