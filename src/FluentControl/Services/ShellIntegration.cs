using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FluentControl.Services;

internal sealed class ShellIntegration : IDisposable
{
    private const uint TrayMessage = 0x8001;
    internal static readonly uint ShowMessage = RegisterWindowMessage("FluentControl.ShowMain.v1");
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly nint window;
    private readonly SubclassProc callback;
    private readonly Action<int> command;
    private readonly Action showMenu;
    private readonly List<int> hotkeys = new();
    private NotifyData icon;
    private bool disposed;
    internal bool TrayAvailable { get; private set; }

    internal ShellIntegration(nint window, string iconPath, Action<int> command, Action showMenu)
    {
        this.window = window; this.command = command; this.showMenu = showMenu; callback = WindowMessage;
        if (!SetWindowSubclass(window, callback, 918, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        icon = new NotifyData
        {
            Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Flags = 1 | 2 | 4,
            Callback = TrayMessage, Icon = LoadImage(0, iconPath, 1, 32, 32, 0x10), Tip = "FluentControl", Info = "", Title = ""
        };
        TrayAvailable = ShellNotifyIcon(0, ref icon);
    }
    internal void UpdateLanguage() { icon.Tip = Strings.AppName; if (TrayAvailable) ShellNotifyIcon(1, ref icon); }
    internal List<string> ConfigureHotkeys(bool enabled)
    {
        foreach (var id in hotkeys) UnregisterHotKey(window, id);
        hotkeys.Clear();
        var errors = new List<string>();
        if (!enabled) return errors;
        foreach (var key in new[] { (Id: 1, Key: 0x20u, Label: "Space"), (Id: 2, Key: 0x25u, Label: "←"), (Id: 3, Key: 0x27u, Label: "→"), (Id: 4, Key: 0x28u, Label: "↓") })
        {
            if (RegisterHotKey(window, key.Id, 0x4000 | 1 | 2 | 4, key.Key)) hotkeys.Add(key.Id);
            else errors.Add("Ctrl + Alt + Shift + " + key.Label);
        }
        return errors;
    }
    private nint WindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        try
        {
            if (message == ShowMessage) { command(1); return 0; }
            if (message == taskbarCreated) { TrayAvailable = ShellNotifyIcon(0, ref icon); return 0; }
            if (message == 0x312) { command((int)wParam); return 0; }
            if (message == TrayMessage)
            {
                var mouse = (uint)(long)lParam & 0xffff;
                if (mouse == 0x203 || mouse == 0x400 || mouse == 0x401) command(1);
                else if (mouse == 0x205 || mouse == 0x7b) showMenu();
                return 0;
            }
        }
        catch (Exception ex) { StartupLog.Write("Shell command: " + ex); }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    internal Windows.Graphics.PointInt32 MenuAnchor()
    {
        var identifier = new NotifyIdentifier { Size = (uint)Marshal.SizeOf<NotifyIdentifier>(), Window = window, Id = icon.Id };
        return ShellNotifyIconGetRect(ref identifier, out var rect) == 0
            ? new(rect.Right, rect.Top) : PointerPosition();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref NotifyIdentifier identifier, out Rect rect);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var id in hotkeys) UnregisterHotKey(window, id);
        hotkeys.Clear(); ShellNotifyIcon(2, ref icon);
        RemoveWindowSubclass(window, callback, 918);
        if (icon.Icon != 0) DestroyIcon(icon.Icon);
    }
    internal static void SignalExisting() => PostMessage((nint)0xffff, ShowMessage, 0, 0);
    internal static void Focus(nint hwnd) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
    internal static void ToolWindow(nint hwnd, bool noActivate, bool clickThrough = false)
    {
        var style = (long)GetWindowLongPtr(hwnd, -20);
        style = (style | 0x80) & ~0x40000L;
        style = noActivate ? style | 0x08000000L : style & ~0x08000000L;
        style = clickThrough ? style | 0x20 : style & ~0x20L;
        SetWindowLongPtr(hwnd, -20, (nint)style);
    }
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")] internal static extern uint Dpi(nint hwnd);
    internal static Windows.Graphics.PointInt32 PointerPosition() { GetCursorPos(out var p); return new(p.X, p.Y); }
    internal static Windows.Graphics.SizeInt32 ClientSize(nint hwnd) { GetClientRect(hwnd, out var r); return new(r.Right - r.Left, r.Bottom - r.Top); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    internal static void Drag(nint hwnd) { ReleaseCapture(); SendMessage(hwnd, 0xa1, 2, 0); }
    internal static bool PrimaryButtonPressed() => (GetAsyncKeyState(GetSystemMetrics(23) == 0 ? 1 : 2) & 0x8000) != 0;
    internal static uint DoubleClickMilliseconds => GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] private static extern bool ShellNotifyIcon(uint operation, ref NotifyData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}
