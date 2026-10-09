using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace FluentControl.Services;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FluentControl";
    private static readonly object ShortcutLock = new();
    private static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
    internal static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "FluentControl.lnk");
    internal static bool IsEnabled() => ShortcutMatches(ShortcutPath, Executable) || HasLegacyRegistration();
    private static bool HasLegacyRegistration()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey, false); return key?.GetValue(ValueName) is string command && command.EndsWith(" --background", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    internal static void SetEnabled(bool enabled)
    {
        // Use the current user's Startup folder instead of requiring Run-key write access.
        SetShortcut(ShortcutPath, Executable, enabled);
        if (HasLegacyRegistration())
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(RunKey, true); key?.DeleteValue(ValueName, false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                if (!enabled) throw new InvalidOperationException(Strings.T("旧版自启动项被 Windows 保护，请在 Windows 启动应用设置中关闭。", "Windows protects the legacy startup entry. Disable it in Windows Startup Apps."), ex);
            }
        }
    }
    internal static void SetShortcut(string path, string executable, bool enabled)
    {
        lock (ShortcutLock)
        {
            path = Path.GetFullPath(path);
            if (!enabled) { RetrySharingViolation(() => File.Delete(path), retryAccessDenied: true); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // A unique non-.lnk staging file avoids collisions and never becomes
            // a second executable startup entry while Explorer observes the folder.
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                SaveShortcut(temporary, executable); // releases the writer before any read/rename
                RetrySharingViolation(() =>
                {
                    if (!ShortcutMatches(temporary, executable)) throw new IOException("Startup shortcut verification failed.");
                }); // the verification reader is also released before the move
                // MoveFileEx can report ERROR_ACCESS_DENIED (not only a sharing
                // violation) when an existing destination is open without delete sharing.
                RetrySharingViolation(() => File.Move(temporary, path, true), retryAccessDenied: true);
            }
            finally
            {
                // Cleanup must not hide the real error or report failure after a successful move.
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    private static void SaveShortcut(string path, string executable)
    {
        object instance = new ShellLink();
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(executable); link.SetArguments("--background");
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!); link.SetDescription("Fluent Control"); link.SetShowCmd(7);
            link.SetIconLocation(executable, 0);
            var file = (IPersistFile)instance;
            file.Save(path, true);
            file.SaveCompleted(path);
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    internal static void RetrySharingViolation(Action operation, Action<int>? delay = null, bool retryAccessDenied = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { operation(); return; }
            catch (Exception ex) when (attempt < 5 &&
                ((ex is IOException or COMException) && (ex.HResult & 0xFFFF) is 32 or 33 ||
                 retryAccessDenied && ex is UnauthorizedAccessException && (ex.HResult & 0xFFFF) == 5))
            { (delay ?? Thread.Sleep)(40 * (attempt + 1)); }
        }
    }
    internal static bool ShortcutMatches(string path, string executable)
    {
        if (!File.Exists(path)) return false;
        object instance = new ShellLink();
        try
        {
            ((IPersistFile)instance).Load(path, 0x40); // STGM_READ | STGM_SHARE_DENY_NONE
            var link = (IShellLinkW)instance; var target = new StringBuilder(32768); var arguments = new StringBuilder(256);
            link.GetPath(target, target.Capacity, 0, 4); link.GetArguments(arguments, arguments.Capacity);
            return string.Equals(target.ToString(), executable, StringComparison.OrdinalIgnoreCase) && arguments.ToString() == "--background";
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, nint findData, uint flags);
        void GetIDList(out nint list); void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key); void SetHotkey(short key); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
