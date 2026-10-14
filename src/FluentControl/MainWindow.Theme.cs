using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void InitializeTitleTheme()
    {
        ShellRoot.ActualThemeChanged += (_, _) => ApplyTitleTheme();
        Activated += (_, _) => ApplyTitleTheme();
        ShellRoot.Loaded += (_, _) => ApplyTitleTheme();
        ApplyTitleTheme();
    }
    private void ApplyAppTheme()
    {
        // Theme the whole window, including navigation and the startup screen.
        // Default keeps WinUI's live system-theme and accessibility behavior.
        ShellRoot.RequestedTheme = (automaticTheme ?? state.Settings.Theme) switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ApplyTitleTheme();
    }
    private void ApplyTitleTheme()
    {
        var theme = ShellRoot.ActualTheme;
        ApplyWindowTitleTheme(this, theme);
        if (monitorOsd?.Content is FrameworkElement osdRoot)
        {
            osdRoot.RequestedTheme = theme;
            ApplyWindowTitleTheme(monitorOsd, theme);
        }
        foreach (var window in identificationWindows)
            if (window.Content is FrameworkElement content) content.RequestedTheme = theme;
    }
    private static void ApplyWindowTitleTheme(Window window, ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int enabled = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        var title = window.AppWindow.TitleBar;
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
