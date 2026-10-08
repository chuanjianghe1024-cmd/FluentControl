using System.Runtime.InteropServices;
namespace FluentControl.Services;

// A locked panel must neither activate nor be promoted when a XAML child is clicked.
internal sealed class DesktopLayer : IDisposable
{
    private readonly nint window;
    private readonly SubclassProc callback;
    private bool locked = true, disposed;
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
    internal void Lower() { if (locked && !disposed) SetWindowPos(window, 1, 0, 0, 0, 0, 0x13); }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (locked)
        {
            if (message is 0x21 or 0x24B) return 3; // MA_NOACTIVATE / PA_NOACTIVATE; keep pointer events for double-click.
            if (message == 0x46 && lParam != 0)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                if ((position.Flags & 4) == 0) { position.After = 1; Marshal.StructureToPtr(position, lParam, false); }
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
}
