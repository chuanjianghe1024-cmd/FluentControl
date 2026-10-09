using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FluentControl.Services;
namespace FluentControl;

internal static class PanelDiagnostics
{
    // Runs only with --ui-test on an isolated Windows runner. Two real background
    // colors must affect the panel pixels: a transparent brush property is not enough.
    internal static async Task VerifyAsync(DesktopPanelWindow panel)
    {
        var grid = new Grid();
        var background = new Window { Content = grid, Title = "Transparency test" };
        var presenter = (OverlappedPresenter)panel.AppWindow.Presenter;
        var originalPointer = ShellIntegration.PointerPosition();
        var hiddenRunnerWindows = new List<nint>();
        try
        {
            if (panel.BackdropAlpha is 0 or 255) throw new InvalidOperationException("Panel tint must be translucent.");
            panel.AppWindow.Move(new Windows.Graphics.PointInt32(100, 100));
            var before = panel.AppWindow.Position;
            panel.AppWindow.Show(false); panel.SetUnlocked(true); presenter.IsAlwaysOnTop = true;
            await DragGripAsync(panel, false, 37, 23);
            if (panel.AppWindow.Position.X != before.X + 37 || panel.AppWindow.Position.Y != before.Y + 23) throw new InvalidOperationException($"Move gesture did not move the panel: before={before.X},{before.Y}; after={panel.AppWindow.Position.X},{panel.AppWindow.Position.Y}");
            var original = ShellIntegration.ClientSize(panel.Handle);
            await DragGripAsync(panel, true, 60, 45);
            var resized = ShellIntegration.ClientSize(panel.Handle);
            if (resized.Width <= original.Width || resized.Height <= original.Height) throw new InvalidOperationException("Resize gesture did not resize the client area.");
            panel.CheckGeometryDelta(true, -5000, -5000); // exercise the minimum size
            await Task.Delay(250);
            StartupLog.Write($"Panel minimum layout: hintHeight={panel.HintHeight:0.00}, hintBottom={panel.HintBottom:0.00}, contentHeight={panel.ContentHeight:0.00}");
            if (panel.HintHeight < 11 || panel.HintBottom > panel.ContentHeight + 1) throw new InvalidOperationException($"Panel hint is clipped: height={panel.HintHeight}, bottom={panel.HintBottom}, content={panel.ContentHeight}");
            panel.CheckGeometryDelta(true, 80, 60);
            background.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(panel.AppWindow.Position.X - 30, panel.AppWindow.Position.Y - 30, panel.AppWindow.Size.Width + 60, panel.AppWindow.Size.Height + 60));
            grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 215, 70, 40));
            background.Activate(); panel.AppWindow.Show(false); panel.SetUnlocked(true); presenter.IsAlwaysOnTop = true;
            await Task.Delay(400); DwmFlush();
            var warm = Sample(panel);
            grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 100, 220));
            await Task.Delay(400); DwmFlush();
            var cool = Sample(panel);
            var difference = 0;
            for (var shift = 0; shift < 24; shift += 8) difference += Math.Abs((int)((warm >> shift) & 255) - (int)((cool >> shift) & 255));
            StartupLog.Write($"Desktop alpha check: warm={warm:X6}, cool={cool:X6}, difference={difference}; hint={panel.HintHeight:0}/{panel.ContentHeight:0}");
            if (difference < 60) throw new InvalidOperationException("Panel is opaque: changing the window behind it did not change its pixels.");
            presenter.IsAlwaysOnTop = false;
            panel.SetUnlocked(false); background.Activate();
            // Attempt the promotion which a child click used to cause.
            SetWindowPos(panel.Handle, 0, 0, 0, 0, 0, 0x13);
            await Task.Delay(150);
            var backgroundHandle = WinRT.Interop.WindowNative.GetWindowHandle(background);
            if (SendMessage(panel.Handle, 0x21, 0, 0) != 3 || IsAbove(panel.Handle, backgroundHandle)) throw new InvalidOperationException("Locked panel activated or rose above another application.");
            // Expose the upper left part of the panel, then test real single/double clicks.
            background.AppWindow.Move(new Windows.Graphics.PointInt32(panel.AppWindow.Position.X + panel.AppWindow.Size.Width / 2, panel.AppWindow.Position.Y));
            var click = new Point { X = 24, Y = 50 }; ClientToScreen(panel.Handle, ref click);
            var hit = WindowFromPoint(click); var hitRoot = GetAncestor(hit, 2);
            var hitClass = new System.Text.StringBuilder(128); GetClassName(hitRoot, hitClass, hitClass.Capacity);
            // The hosted runner's terminal is an ordinary window and correctly
            // occludes a locked panel. Expose the desktop without promoting the panel.
            if (hitClass.ToString() is "CASCADIA_HOSTING_WINDOW_CLASS" or "ConsoleWindowClass")
            {
                hiddenRunnerWindows.Add(hitRoot); ShowWindow(hitRoot, 0); await Task.Delay(150);
                hit = WindowFromPoint(click); hitRoot = GetAncestor(hit, 2); hitClass.Clear(); GetClassName(hitRoot, hitClass, hitClass.Capacity);
            }
            StartupLog.Write($"Locked panel hit: target={panel.Handle}, hit={hit}, root={hitRoot}, class={hitClass}");
            if (hitRoot != panel.Handle) throw new InvalidOperationException("The locked panel is hidden behind another window or wallpaper.");
            await ClickAsync(click, false);
            if (panel.IsUnlocked || IsAbove(panel.Handle, backgroundHandle)) throw new InvalidOperationException("A single click raised the locked panel.");
            await ClickAsync(click, true);
            if (!panel.IsUnlocked) throw new InvalidOperationException("A locked bottom-layer panel cannot unlock by double-click.");
            panel.SetUnlocked(false);
            StartupLog.Write("PASS: locked panel rejects activation/promotion; double-click still unlocks.");
            await VerifyShowDesktopAsync(panel, background);
        }
        finally
        {
            mouse_event(4, 0, 0, 0, 0); SetCursorPos(originalPointer.X, originalPointer.Y);
            presenter.IsAlwaysOnTop = false; background.Close();
            foreach (var window in hiddenRunnerWindows) ShowWindow(window, 8);
        }
    }
    private static async Task VerifyShowDesktopAsync(DesktopPanelWindow panel, Window background)
    {
        // Exercise actual Shell Show Desktop, not just WM_SYSCOMMAND. The runner is disposable.
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Explorer shell unavailable.");
        var shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Cannot create shell automation.");
        var toggled = false;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                background.Activate(); panel.SetUnlocked(i == 1);
                shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null); toggled = true;
                await Task.Delay(650);
                if (IsIconic(panel.Handle) || !IsWindowVisible(panel.Handle) || panel.IsUnlocked || GetForegroundWindow() == panel.Handle)
                    throw new InvalidOperationException("Show Desktop minimized, unlocked, hid or activated the desktop panel.");
                var point = new Point { X = 24, Y = 50 }; ClientToScreen(panel.Handle, ref point);
                if (GetAncestor(WindowFromPoint(point), 2) != panel.Handle)
                    throw new InvalidOperationException("Show Desktop covered the panel with Explorer's desktop.");
                shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null); toggled = false;
                await Task.Delay(350); background.Activate(); await Task.Delay(100);
                if (IsAbove(panel.Handle, WinRT.Interop.WindowNative.GetWindowHandle(background)))
                    throw new InvalidOperationException("Desktop panel covered an app after restoring Show Desktop.");
            }
            StartupLog.Write("PASS: Show Desktop toggles preserve panel visibility, lock, no-activation and bottom-layer ordering.");
        }
        finally
        {
            if (toggled) shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            Marshal.FinalReleaseComObject(shell);
        }
    }
    private static bool IsAbove(nint first, nint second)
    {
        for (var window = GetTopWindow(0); window != 0; window = GetWindow(window, 2))
        { if (window == first) return true; if (window == second) return false; }
        throw new InvalidOperationException("Test window missing from Z order.");
    }
    private static async Task ClickAsync(Point point, bool twice)
    {
        await Task.Run(async () =>
        {
            MovePointer(point.X, point.Y); await Task.Delay(100);
            mouse_event(2,0,0,0,0); mouse_event(4,0,0,0,0);
            if (twice) { await Task.Delay(100); mouse_event(2,0,0,0,0); mouse_event(4,0,0,0,0); }
            await Task.Delay(250);
        });
    }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint="GetClassNameW", CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nuint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    private static async Task DragGripAsync(DesktopPanelWindow panel, bool resize, int dx, int dy)
    {
        var size = ShellIntegration.ClientSize(panel.Handle);
        var scale = ShellIntegration.Dpi(panel.Handle) / 96d;
        var start = new Point { X = size.Width - (int)((resize ? 14 : 24) * scale), Y = resize ? size.Height - (int)(14 * scale) : (int)(24 * scale) };
        ClientToScreen(panel.Handle, ref start);
        // Drive input from a worker so Windows' native move-size modal loop can run.
        await Task.Run(async () =>
        {
            MovePointer(start.X, start.Y); await Task.Delay(150);
            mouse_event(2, 0, 0, 0, 0); await Task.Delay(150);
            MovePointer(start.X + dx, start.Y + dy); await Task.Delay(200);
            mouse_event(4, 0, 0, 0, 0); await Task.Delay(200);
        });
    }
    private static void MovePointer(int x, int y)
    {
        // Inject movement, not just a cursor-position warp: WinUI consumes pointer input.
        var nx = (uint)Math.Clamp((x - GetSystemMetrics(76) + .5) * 65536 / GetSystemMetrics(78), 0, 65535);
        var ny = (uint)Math.Clamp((y - GetSystemMetrics(77) + .5) * 65536 / GetSystemMetrics(79), 0, 65535);
        mouse_event(0xc001, nx, ny, 0, 0);
    }
    private static uint Sample(DesktopPanelWindow panel)
    {
        var size = ShellIntegration.ClientSize(panel.Handle);
        var point = new Point { X = size.Width / 2, Y = size.Height - 5 };
        ClientToScreen(panel.Handle, ref point);
        var dc = GetDC(0);
        try
        {
            var color = GetPixel(dc, point.X, point.Y);
            if (color == uint.MaxValue) throw new InvalidOperationException("Cannot sample desktop pixels.");
            return color;
        }
        finally { ReleaseDC(0, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
