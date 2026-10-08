using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace FluentControl;

// A transparent composition brush alone leaves the native HWND background black.
// Enable DWM alpha composition and clear the native client pixels as well.
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;
    private NativeBackdrop? native;
    private Windows.UI.Color tint = Microsoft.UI.Colors.Transparent;
    internal Windows.UI.Color TintColor { get => tint; set { tint = value; if (brush is not null) brush.Color = value; } }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
        compositor = new Windows.UI.Composition.Compositor();
        brush = compositor.CreateColorBrush(tint);
        target.SystemBackdrop = brush;
        native = new NativeBackdrop(Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId));
        base.OnTargetConnected(target, root);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        native?.Dispose(); native = null;
        target.SystemBackdrop = null;
        brush?.Dispose(); brush = null;
        compositor?.Dispose(); compositor = null;
        base.OnTargetDisconnected(target);
    }
    private sealed class NativeBackdrop : IDisposable
    {
        private readonly nint hwnd;
        private readonly SubclassProc procedure;
        internal NativeBackdrop(nint hwnd)
        {
            this.hwnd = hwnd; procedure = Message;
            if (!SetWindowSubclass(hwnd, procedure, 919, 0)) throw new InvalidOperationException("Unable to initialize transparent window.");
            Configure();
            var dc = GetDC(hwnd);
            try { Clear(dc); } finally { ReleaseDC(hwnd, dc); }
        }
        private void Configure()
        {
            var margins = new Margins();
            Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(hwnd, ref margins));
            var region = CreateRectRgn(-2, -2, -1, -1);
            try
            {
                var blur = new Blur { Flags = 3, Enable = true, Region = region };
                Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(hwnd, ref blur));
            }
            finally { DeleteObject(region); }
        }
        private bool Clear(nint dc) => dc != 0 && GetClientRect(hwnd, out var rect) && FillRect(dc, ref rect, GetStockObject(4)) != 0;
        private nint Message(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            try
            {
                if (message == 0x14 && Clear((nint)wParam)) return 1; // WM_ERASEBKGND
                if (message == 0x31e) Configure(); // DWM recreated the composition surface
            }
            catch (Exception ex) { StartupLog.Write("Backdrop: " + ex.Message); }
            return DefSubclassProc(window, message, wParam, lParam);
        }
        public void Dispose() => RemoveWindowSubclass(hwnd, procedure, 919);
        private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Blur { public uint Flags; [MarshalAs(UnmanagedType.Bool)] public bool Enable; public nint Region; [MarshalAs(UnmanagedType.Bool)] public bool Transition; }
        [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
        [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
        [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref Blur blur);
        [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
        [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
        [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
        [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    }
}
