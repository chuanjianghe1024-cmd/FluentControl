using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void InitializeTitleTheme()
    {
        Root.ActualThemeChanged += (_, _) => ApplyTitleTheme();
        Activated += (_, _) => ApplyTitleTheme();
        Root.Loaded += (_, _) => ApplyTitleTheme();
        ApplyTitleTheme();
    }
    private void ApplyTitleTheme()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int enabled = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        var title = AppWindow.TitleBar;
        var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var inactive = dark ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : Windows.UI.Color.FromArgb(255, 96, 96, 96);
        title.BackgroundColor = title.InactiveBackgroundColor = title.ButtonBackgroundColor = title.ButtonInactiveBackgroundColor = background;
        title.ForegroundColor = title.ButtonForegroundColor = foreground;
        title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = inactive;
        title.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 60, 60, 60) : Windows.UI.Color.FromArgb(255, 224, 224, 224);
        title.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 80, 80, 80) : Windows.UI.Color.FromArgb(255, 210, 210, 210);
        title.ButtonHoverForegroundColor = title.ButtonPressedForegroundColor = foreground;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
