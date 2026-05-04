using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AnkerGamesClient.Services;

/// <summary>
/// Creates Windows .lnk shortcuts without requiring the IWshRuntimeLibrary COM reference.
/// Uses the native IShellLink COM interface directly.
/// </summary>
public class ShortcutService
{
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
            int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public void CreateDesktopShortcut(string targetPath, string name)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var lnkPath = Path.Combine(desktop, $"{SanitizeName(name)}.lnk");
            CreateShortcut(targetPath, lnkPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shortcut error: {ex.Message}");
        }
    }

    public void CreateStartMenuShortcut(string targetPath, string name)
    {
        try
        {
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
            Directory.CreateDirectory(startMenu);
            var lnkPath = Path.Combine(startMenu, $"{SanitizeName(name)}.lnk");
            CreateShortcut(targetPath, lnkPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Start menu shortcut error: {ex.Message}");
        }
    }

    private static void CreateShortcut(string targetPath, string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
        link.SetDescription(Path.GetFileNameWithoutExtension(targetPath));

        var persist = (IPersistFile)link;
        persist.Save(lnkPath, false);
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
