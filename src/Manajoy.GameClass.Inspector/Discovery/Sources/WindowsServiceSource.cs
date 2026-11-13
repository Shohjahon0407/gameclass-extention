using System.ServiceProcess;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Discovery.Sources;

/// <summary>Finds SQL Server / MSDE engine services (MSSQLSERVER, MSSQL$NAME). Only reads their status.</summary>
internal sealed class WindowsServiceSource(ILogger<WindowsServiceSource> logger) : ISqlInstanceSource
{
    private const string DefaultInstanceServiceName = "MSSQLSERVER";
    private const string NamedInstanceServicePrefix = "MSSQL$";

    public string Name => "Windows services";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsFallback => false;

    public IReadOnlyList<SqlInstanceInfo> Discover()
    {
        var services = ServiceController.GetServices();
        try
        {
            return services
                .Where(service => IsSqlEngineService(service.ServiceName))
                .Select(ToInstance)
                .ToList();
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    private static bool IsSqlEngineService(string serviceName) =>
        serviceName.Equals(DefaultInstanceServiceName, StringComparison.OrdinalIgnoreCase)
        || serviceName.StartsWith(NamedInstanceServicePrefix, StringComparison.OrdinalIgnoreCase);

    private SqlInstanceInfo ToInstance(ServiceController service)
    {
        var instanceName = service.ServiceName.Equals(DefaultInstanceServiceName, StringComparison.OrdinalIgnoreCase)
            ? SqlServerName.DefaultInstanceName
            : service.ServiceName[NamedInstanceServicePrefix.Length..];

        string? status = null;
        bool? isRunning = null;
        try
        {
            status = service.Status.ToString();
            isRunning = service.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Cannot read status of service {Service}: {Message}", service.ServiceName, ex.Message);
        }

        return new SqlInstanceInfo
        {
            InstanceName = instanceName,
            ServerName = SqlServerName.ForInstance(instanceName),
            ServiceName = service.ServiceName,
            ServiceStatus = status,
            IsRunning = isRunning,
            DiscoverySources = [Name],
        };
    }
}
