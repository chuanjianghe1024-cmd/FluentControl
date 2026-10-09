using System.ComponentModel;
using System.Runtime.InteropServices;
using FluentControl.Services;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FluentControl;

internal static class WindowPlacement
{
    internal static double Scale(Window window) => Math.Max(96, ShellIntegration.Dpi(WinRT.Interop.WindowNative.GetWindowHandle(window))) / 96d;

    internal static void Place(Window window, double width, double height, MonitorDevice? monitor = null, bool workArea = true)
    {
        if (monitor is { Width: > 0, Height: > 0 })
        {
            // Move the still-hidden HWND onto the target monitor first. Its DPI
            // may differ from the main window's, including on a negative-X screen.
            window.AppWindow.MoveAndResize(new RectInt32(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2, 1, 1));
        }
        ResizeAndCenter(window, width, height, workArea);
    }

    internal static void ResizeAndCenter(Window window, double width, double height, bool workArea = true)
    {
        var area = Area(window, workArea);
        var app = window.AppWindow;
        var size = WindowGeometry.ClientSize(width, height, Scale(window), area,
            app.Size.Width - app.ClientSize.Width, app.Size.Height - app.ClientSize.Height);
        app.ResizeClient(new SizeInt32(size.Width, size.Height));
        var position = WindowGeometry.Center(area, app.Size.Width, app.Size.Height);
        app.Move(new PointInt32(position.X, position.Y));
    }

    internal static WindowArea Area(Window window, bool workArea = true)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var rect = workArea ? info.Work : info.Monitor;
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
