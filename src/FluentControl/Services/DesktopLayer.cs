using System.Runtime.InteropServices;
namespace FluentControl.Services;

// A locked panel must neither activate nor be promoted when a XAML child is clicked.
internal sealed class DesktopLayer : IDisposable
{
    private readonly nint window;
    private readonly SubclassProc callback;
    private readonly WinEventProc eventCallback;
    private readonly Func<Action, bool> dispatch;
    private readonly Action lockPanel;
    private readonly nint foregroundHook, minimizeHook;
    private readonly nint desktopSentinel;
    private const nuint DesktopTimer = 0x46434453;
    private bool locked = true, disposed, placing, recoveryQueued, desktopBoosted, showDesktop;
    private int timerTicks;
    internal string DiagnosticState => $"sentinel={desktopSentinel}, raised={IsDesktopRaised()}, tracked={showDesktop}, boosted={desktopBoosted}, locked={locked}, ticks={timerTicks}";
    internal DesktopLayer(nint window, Func<Action, bool> dispatch, Action lockPanel)
    {
        this.window = window; this.dispatch = dispatch; this.lockPanel = lockPanel; callback = WindowProc;
        if (!SetWindowSubclass(window, callback, 920, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        eventCallback = OnWinEvent;
        foregroundHook = SetWinEventHook(3, 3, 0, eventCallback, 0, 0, 0); // foreground (including Explorer's desktop)
        minimizeHook = SetWinEventHook(0x16, 0x17, 0, eventCallback, (uint)Environment.ProcessId, 0, 0);
        // A hidden, disabled normal-band window marks the bottom of the app stack.
        // Show Desktop puts the shell above this marker, even without a foreground event.
        desktopSentinel = CreateWindowEx(0x08000080, "STATIC", "FluentControl.DesktopSentinel", 0x88000000,
            0, 0, 0, 0, 0, 0, 0, 0);
        if (desktopSentinel != 0) SetWindowPos(desktopSentinel, 1, 0, 0, 0, 0, 0x13);
        SetTimer(window, DesktopTimer, 200, 0);
    }
    internal void SetLocked(bool value)
    {
        locked = value;
        ShellIntegration.ToolWindow(window, value);
        if (value) Lower();
        else RemoveDesktopBoost();
    }
    internal void Lower()
    {
        if (!locked || disposed) return;
        showDesktop = IsDesktopRaised();
        // HWND_BOTTOM alone can put a window behind Explorer's wallpaper host.
        // Keep it immediately above that host, below ordinary application windows.
        nint anchor = 1, previous = 0, lastTopmost = 0;
        for (var item = GetTopWindow(0); item != 0; item = GetWindow(item, 2))
        {
            if (item == window || !IsWindowVisible(item) || IsIconic(item)) continue;
            var name = new System.Text.StringBuilder(128); GetClassName(item, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") { anchor = previous; break; }
            if (((long)GetWindowLongPtr(item, -20) & 8) == 0) previous = item;
            else lastTopmost = item;
        }
        placing = true;
        try
        {
            // Show Desktop raises Explorer over non-minimizable normal windows.
            // Use the topmost band only while the shell covers the app stack.
            // Never activate or cover the taskbar; leave this band on restoration.
            if (showDesktop)
            {
                desktopBoosted = true;
                SetWindowPos(window, -1, 0, 0, 0, 0, 0x13);
                if (lastTopmost != 0) SetWindowPos(window, lastTopmost, 0, 0, 0, 0, 0x13);
            }
            else
            {
                desktopBoosted = false;
                SetWindowPos(window, -2, 0, 0, 0, 0, 0x13);
                SetWindowPos(window, anchor, 0, 0, 0, 0, 0x13);
            }
        }
        finally { placing = false; }
    }
    private void RemoveDesktopBoost()
    {
        if (!desktopBoosted || disposed) return;
        desktopBoosted = false; placing = true;
        try { SetWindowPos(window, -2, 0, 0, 0, 0, 0x13); }
        finally { placing = false; }
    }
    private static bool IsDesktop(nint hwnd)
    {
        if (hwnd == 0) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is not ("Progman" or "WorkerW")) return false;
        GetWindowThreadProcessId(GetShellWindow(), out var shellProcess);
        GetWindowThreadProcessId(hwnd, out var process);
        return shellProcess != 0 && process == shellProcess;
    }
    private bool IsDesktopRaised()
    {
        if (desktopSentinel == 0) return IsDesktop(GetForegroundWindow());
        var shell = GetShellWindow();
        for (var item = GetTopWindow(0); item != 0; item = GetWindow(item, 2))
        {
            if (item == desktopSentinel) return false;
            if (IsWindowVisible(item) && IsDesktop(item) &&
                (item == shell || FindWindowEx(item, 0, "SHELLDLL_DefView", null) != 0)) return true;
        }
        return false;
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x113 && wParam == DesktopTimer)
        {
            timerTicks++;
            if (!disposed && IsWindowVisible(window))
            {
                var raised = IsDesktopRaised();
                if (raised)
                {
                    // Explorer/WinUI may finish restoring styles after focus loss.
                    // Repair a lost band as well as detecting entry into Show Desktop.
                    if (!showDesktop || !locked || ((long)GetWindowLongPtr(window, -20) & 8) == 0 || IsIconic(window)) QueueRecovery();
                }
                else if (showDesktop || desktopBoosted)
                {
                    if (locked) Lower();
                    else RemoveDesktopBoost();
                }
                showDesktop = raised;
            }
            return 0;
        }
        // Disabling the caption's minimize button does not stop Win+D/Show Desktop.
        if (message == 0x112 && (wParam & 0xFFF0) == 0xF020) { QueueRecovery(); return 0; }
        if (message == 0x5 && wParam == 1) QueueRecovery(); // WM_SIZE / SIZE_MINIMIZED
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
    private void OnWinEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (disposed) return;
        if (kind is 0x16 or 0x17 && hwnd == window) { QueueRecovery(); return; }
        if (kind != 3 || hwnd == 0) return;
        if (IsDesktop(hwnd)) QueueRecovery();
        else if (locked) dispatch(() => { if (!disposed && locked) { Lower(); _ = RecheckDesktopAsync(); } });
    }
    private void QueueRecovery()
    {
        if (disposed || recoveryQueued) return;
        recoveryQueued = true;
        if (!dispatch(() =>
        {
            recoveryQueued = false;
            if (disposed) return;
            lockPanel();
            if (IsIconic(window)) ShowWindow(window, 4); // SW_SHOWNOACTIVATE; never take focus
            if (IsWindowVisible(window)) Lower();
            _ = RecheckDesktopAsync();
        })) recoveryQueued = false;
    }
    private async Task RecheckDesktopAsync()
    {
        // Explorer can finish reordering after its foreground event. Recheck once
        // for a prompt response; the sentinel also detects transitions without focus.
        await Task.Delay(120);
        if (!disposed) dispatch(() => { if (!disposed && locked && IsWindowVisible(window)) Lower(); });
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        KillTimer(window, DesktopTimer);
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        if (minimizeHook != 0) UnhookWinEvent(minimizeHook);
        RemoveWindowSubclass(window, callback, 920);
        if (desktopSentinel != 0) DestroyWindow(desktopSentinel);
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public nint Window, After; public int X, Y, Width, Height; public uint Flags; }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    private delegate void WinEventProc(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder text, int length);
}
