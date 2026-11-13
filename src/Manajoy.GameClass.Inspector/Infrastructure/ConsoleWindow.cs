using System.Runtime.InteropServices;

namespace Manajoy.GameClass.Inspector.Infrastructure;

internal static class ConsoleWindow
{
    /// <summary>
    /// True when the console window was created just for this process (e.g. the exe was double-clicked),
    /// so the window would close before the operator can read the report.
    /// </summary>
    public static bool IsOwnedByThisProcess()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        try
        {
            var processIds = new uint[2];
            return GetConsoleProcessList(processIds, (uint)processIds.Length) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
