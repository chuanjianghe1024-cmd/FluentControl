using System.Runtime.InteropServices;
namespace FluentControl.Services;

// A locked panel must neither activate nor be promoted when a XAML child is clicked.
internal sealed class DesktopLayer : IDisposable
{
    private readonly nint window;
    private readonly SubclassProc callback;
    private bool locked = true, disposed, placing;
    internal DesktopLayer(nint window)
    {
        this.window = window; callback = WindowProc;
        if (!SetWindowSubclass(window, callback, 920, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    internal void SetLocked(bool value)
    {
        locked = value;
        ShellIntegration.ToolWindow(window, value);
        if (value) Lower();
    }
    internal void Lower()
    {
        if (!locked || disposed) return;
        // HWND_BOTTOM alone can put a window behind Explorer's wallpaper host.
        // Keep it immediately above that host, below ordinary application windows.
        nint anchor = 1, previous = 0;
        for (var item = GetTopWindow(0); item != 0; item = GetWindow(item, 2))
        {
            if (item == window || !IsWindowVisible(item)) continue;
            var name = new System.Text.StringBuilder(128); GetClassName(item, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") { anchor = previous; break; }
            previous = item;
        }
        placing = true;
        try { SetWindowPos(window, anchor, 0, 0, 0, 0, 0x13); }
        finally { placing = false; }
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (locked)
        {
            if (message is 0x21 or 0x24B) return 3; // MA_NOACTIVATE / PA_NOACTIVATE; keep pointer events for double-click.
            if (message == 0x46 && lParam != 0 && !placing)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                if ((position.Flags & 4) == 0) { position.Flags |= 4; Marshal.StructureToPtr(position, lParam, false); }
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose() { if (disposed) return; disposed = true; RemoveWindowSubclass(window, callback, 920); }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public nint Window, After; public int X, Y, Width, Height; public uint Flags; }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder text, int length);
}
