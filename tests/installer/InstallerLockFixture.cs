// Disposable Windows CI helper, compiled with the inbox .NET Framework compiler.
// This file is never included in the product or its MSI payload.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyFileVersion("0.0.1.0")]

internal static class InstallerLockFixture
{
    private static readonly StringBuilder messages = new StringBuilder();
    private static readonly InstallHandler handler = OnInstallerMessage;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "hold")
        {
            // Map the native image solely to hold a real loaded-module lock.
            // DONT_RESOLVE_DLL_REFERENCES: never call any function in this image.
            IntPtr module = LoadLibraryEx(args[1], IntPtr.Zero, 1);
            if (module == IntPtr.Zero) return Marshal.GetLastWin32Error();
            try
            {
                using (var window = new Form())
                {
                    window.Text = "FluentControl installer lock fixture";
                    window.Width = 300;
                    window.Height = 100;
                    window.Shown += delegate { File.WriteAllText(args[2], "ready"); };
                    Application.Run(window);
                }
            }
            finally { FreeLibrary(module); }
            return 0;
        }
        if (args.Length == 4 && args[0] == "uninstall")
        {
            // BASIC UI makes FilesInUse messages available. Handle them before
            // the built-in UI and cancel; never accept Ignore or close apps.
            MsiSetInternalUI(3, IntPtr.Zero);
            MsiSetExternalUI(handler, 0x0200013f, IntPtr.Zero);
            MsiEnableLog(0x00003fff, args[3], 0);
            uint result = MsiConfigureProductEx(args[1], 0, 2, "REBOOT=ReallySuppress");
            File.WriteAllText(args[2], "Result=" + result + Environment.NewLine + messages);
            GC.KeepAlive(handler);
            return 0;
        }
        return 87;
    }

    private static int OnInstallerMessage(IntPtr context, uint type, string message)
    {
        uint kind = type & 0xff000000;
        if (kind == 0x08000000) messages.AppendLine("Action=" + message);
        if (kind == 0x05000000 || kind == 0x19000000)
        {
            messages.AppendLine((kind == 0x05000000 ? "FilesInUse=" : "RMFilesInUse=") + message);
            return 2; // IDCANCEL
        }
        if (kind <= 0x03000000) return 2; // Never block CI on an error dialog.
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int InstallHandler(IntPtr context, uint type, [MarshalAs(UnmanagedType.LPWStr)] string message);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("msi.dll")]
    private static extern uint MsiSetInternalUI(uint level, IntPtr window);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr MsiSetExternalUI(InstallHandler callback, uint filter, IntPtr context);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnableLog(uint mode, string path, uint attributes);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiConfigureProductEx(string productCode, int level, int state, string properties);
}
