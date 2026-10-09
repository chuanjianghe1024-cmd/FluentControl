using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
namespace FluentControl;

internal sealed class CrosshairWindow : Window
{
    internal CrosshairWindow(MonitorDevice monitor)
    {
        Title = Strings.AppName + " · " + Strings.T("软件准星", "Software crosshair");
        SystemBackdrop = new TransparentBackdrop();
        var root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var color = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 235, 180));
        root.Children.Add(new Rectangle { Width = 22, Height = 2, Fill = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(new Rectangle { Width = 2, Height = 22, Fill = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Content = root;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        }
        ShellIntegration.ToolWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), true, true);
        WindowPlacement.Place(this, 48, 48, monitor, workArea: false);
        AppWindow.Show(false);
    }
}
