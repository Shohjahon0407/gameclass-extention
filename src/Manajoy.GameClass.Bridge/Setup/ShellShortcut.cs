using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Writes a .lnk file through the shell's own IShellLinkW / IPersistFile (strongly typed COM).
/// Not WScript.Shell with <c>dynamic</c>: the published exe is trimmed, and late-bound IDispatch through the
/// trimmed dynamic binder crashes the process (0xC0000005 in ITypeInfo.ReleaseTypeAttr, lab 2026-10-14).
/// Both interfaces are declared here, in this (untrimmed) assembly: the framework's ComTypes.IPersistFile lost
/// its unused methods to the trimmer, so Save landed in another vtable slot and silently wrote nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellShortcut
{
    public static void Create(string path, string target, string? arguments = null, string? workingDirectory = null,
        string? description = null, string? iconLocation = null, int iconIndex = 0)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            if (!string.IsNullOrEmpty(arguments))
            {
                link.SetArguments(arguments);
            }

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                link.SetWorkingDirectory(workingDirectory);
            }

            if (!string.IsNullOrEmpty(description))
            {
                link.SetDescription(description);
            }

            if (!string.IsNullOrEmpty(iconLocation))
            {
                link.SetIconLocation(iconLocation, iconIndex);
            }

            ((IPersistFile)link).Save(path, true);
            if (!File.Exists(path))
            {
                throw new IOException($"shortcut not written: {path}");
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
