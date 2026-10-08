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
    private readonly List<int> hotkeys = new();
    private NotifyData icon;
    private bool disposed;
    internal bool TrayAvailable { get; private set; }

    internal ShellIntegration(nint window, string iconPath, Action<int> command)
    {
        this.window = window; this.command = command; callback = WindowMessage;
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
                else if (mouse == 0x205 || mouse == 0x7b) ShowMenu();
                return 0;
            }
        }
        catch (Exception ex) { StartupLog.Write("Shell command: " + ex); }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, 1, Strings.T("打开主面板", "Open panel"));
            AppendMenu(menu, 0, 4, Strings.T("隐藏主面板", "Hide panel"));
            AppendMenu(menu, 0, 5, Strings.T("显示 / 隐藏桌面面板", "Toggle desktop panel"));
            AppendMenu(menu, 0x800, 0, "");
            AppendMenu(menu, 0, 2, Strings.T("上一个配置", "Previous profile"));
            AppendMenu(menu, 0, 3, Strings.T("下一个配置", "Next profile"));
            AppendMenu(menu, 0x800, 0, "");
            AppendMenu(menu, 0, 6, Strings.T("退出聚合控制", "Exit Fluent Control"));
            GetCursorPos(out var point); SetForegroundWindow(window);
            var chosen = TrackPopupMenu(menu, 0x102, point.X, point.Y, 0, window, 0);
            PostMessage(window, 0, 0, 0);
            if (chosen != 0) command((int)chosen);
        }
        finally { DestroyMenu(menu); }
    }
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
    internal static void Drag(nint hwnd) => MoveOrResize(hwnd, false);
    internal static void MoveOrResize(nint hwnd, bool resize) { ReleaseCapture(); SendMessage(hwnd, 0xa1, resize ? 17u : 2u, 0); }
    internal static IDisposable LimitPanelSize(nint hwnd) => new PanelSizeLimits(hwnd);
    private sealed class PanelSizeLimits : IDisposable
    {
        private readonly nint hwnd;
        private readonly SubclassProc procedure;
        internal PanelSizeLimits(nint hwnd)
        {
            this.hwnd = hwnd; procedure = Message;
            if (!SetWindowSubclass(hwnd, procedure, 920, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        private nint Message(nint window, uint message, nuint w, nint l, nuint id, nuint data)
        {
            var result = DefSubclassProc(window, message, w, l);
            if (message == 0x24 && l != 0)
            {
                var info = Marshal.PtrToStructure<MinMaxInfo>(l);
                var scale = Dpi(window) / 96d;
                GetWindowRect(window, out var outer); var client = ClientSize(window);
                var borderX = Math.Max(0, outer.Right - outer.Left - client.Width);
                var borderY = Math.Max(0, outer.Bottom - outer.Top - client.Height);
                info.MinTrack = new Point { X = (int)(280 * scale) + borderX, Y = (int)(144 * scale) + borderY };
                info.MaxTrack = new Point { X = (int)(900 * scale) + borderX, Y = (int)(1000 * scale) + borderY };
                Marshal.StructureToPtr(info, l, false);
            }
            return result;
        }
        public void Dispose() => RemoveWindowSubclass(hwnd, procedure, 920);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);

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
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint item, string label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}
