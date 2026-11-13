using System.Runtime.InteropServices;
using System.Security.Principal;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Manajoy.GameClass.Inspector.Infrastructure;

internal sealed class EnvironmentInfoProvider(ILogger<EnvironmentInfoProvider> logger)
{
    private const string WindowsVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const int FirstWindows11Build = 22000;

    public EnvironmentInfo Collect() => new()
    {
        ApplicationName = AppInfo.DisplayName,
        ApplicationVersion = AppInfo.Version,
        MachineName = Environment.MachineName,
        OsName = GetOsName(),
        OsVersion = Environment.OSVersion.VersionString,
        OsArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        DotNetVersion = RuntimeInformation.FrameworkDescription,
        CurrentUser = string.IsNullOrEmpty(Environment.UserDomainName)
            ? Environment.UserName
            : $@"{Environment.UserDomainName}\{Environment.UserName}",
        IsElevated = IsElevated(),
        Timestamp = DateTimeOffset.Now,
        InspectionMode = AppInfo.InspectionMode,
    };

    private string GetOsName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(WindowsVersionKey, writable: false);
            var productName = key?.GetValue("ProductName") as string;
            var displayVersion = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string;
            if (string.IsNullOrEmpty(productName))
            {
                return RuntimeInformation.OSDescription;
            }

            // Windows 11 still reports "Windows 10" in ProductName; the build number tells them apart.
            if (Environment.OSVersion.Version.Build >= FirstWindows11Build)
            {
                productName = productName.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
            }

            return string.IsNullOrEmpty(displayVersion) ? productName : $"{productName} {displayVersion}";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read Windows product name");
            return RuntimeInformation.OSDescription;
        }
    }

    private bool? IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not determine elevation");
            return null;
        }
    }
}
