using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// "Open the panel when the PC starts": a Task Scheduler task that runs at every sign-in.
///
/// The bridge itself (the Windows service) always starts with Windows; this only opens the window for the
/// cashier. A Run registry key cannot do it: Windows silently skips programs that need administrator rights
/// there, and this exe does. A logon task with "highest available" rights starts it without a UAC prompt.
/// The task runs for any member of Users (whoever signs in), 20 seconds after sign-in so GameClass is up first.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AutoStart
{
    public const string TaskName = @"Manajoy\GameClass panel";
    public const string Switch = "--autostart";

    public static bool IsEnabled()
    {
        try
        {
            ServiceManager.RunTool("schtasks.exe", "/Query", "/TN", TaskName);
            return true;
        }
        catch (SetupException)
        {
            return false;
        }
    }

    public static void Enable(string exePath)
    {
        var xml = Path.Combine(Path.GetTempPath(), $"manajoy-autostart-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks reads task XML as UTF-16.
            File.WriteAllText(xml, TaskXml(exePath), Encoding.Unicode);
            ServiceManager.RunTool("schtasks.exe", "/Create", "/TN", TaskName, "/XML", xml, "/F");
        }
        finally
        {
            File.Delete(xml);
        }
    }

    public static void Disable()
    {
        if (IsEnabled())
        {
            ServiceManager.RunTool("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
        }
    }

    /// <summary><c>--autostart-on</c> / <c>--autostart-off</c> from the Windows installer and uninstaller.</summary>
    public static int RunSwitch(bool enable)
    {
        try
        {
            if (enable)
            {
                Enable(SetupEnvironment.CurrentExePath);
            }
            else
            {
                Disable();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static string TaskXml(string exePath)
    {
        var command = SecurityElement.Escape(exePath);
        var folder = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Manajoy</Author>
                <Description>Opens the Manajoy GameClass panel after sign-in. The bridge service itself starts with Windows on its own.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT20S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Users">
                  <GroupId>S-1-5-32-545</GroupId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Users">
                <Exec>
                  <Command>"{command}"</Command>
                  <Arguments>{Switch}</Arguments>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
