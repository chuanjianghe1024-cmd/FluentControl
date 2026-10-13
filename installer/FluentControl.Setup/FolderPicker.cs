using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace FluentControl.Setup;

// The OS Common Item Dialog keeps the modern folder picker on .NET Framework.
// Windows SDK contracts: IFileDialog / IShellItem / SHCreateItemFromParsingName.
internal static class FolderPicker
{
    internal static string? Show(Window owner, string title, string initialDirectory)
    {
        var dialog = (IFileDialog)new FileOpenDialog();
        IShellItem? initial = null, selected = null;
        try
        {
            dialog.GetOptions(out var options);
            // PICKFOLDERS | FORCEFILESYSTEM | NOCHANGEDIR | PATHMUSTEXIST
            dialog.SetOptions(options | 0x20 | 0x40 | 0x8 | 0x800);
            dialog.SetTitle(title);
            if (Directory.Exists(initialDirectory))
            {
                var iid = typeof(IShellItem).GUID;
                // Shell parsing does not normalize filesystem dot segments in
                // the same way as System.IO; resolve them before creating an item.
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(initialDirectory), IntPtr.Zero, ref iid, out initial));
                dialog.SetFolder(initial);
            }
            var result = dialog.Show(new WindowInteropHelper(owner).EnsureHandle());
            if (result == unchecked((int)0x800704C7)) return null; // user cancelled
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out selected);
            selected.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        finally
        {
            if (selected is not null) Marshal.ReleaseComObject(selected);
            if (initial is not null) Marshal.ReleaseComObject(initial);
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), ClassInterface(ClassInterfaceType.None)]
    private class FileOpenDialog { }

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint location);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
