using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using static FluentControl.Services.Strings;

namespace FluentControl;

// A separate, transparent owner lets the native WinUI popup open while the main
// panel remains hidden. WinAppSDK 1.4+ supports unconstrained desktop flyouts.
internal sealed class TrayMenuWindow : Window, IDisposable
{
    private readonly Grid root = new();
    private readonly Action<int> command;
    private bool disposed, pending;
    internal MenuFlyout? Menu { get; private set; }
    internal ElementTheme MenuTheme => root.ActualTheme;

    internal TrayMenuWindow(Action<int> command)
    {
        this.command = command;
        Title = AppName;
        Content = root;
        SystemBackdrop = new TransparentBackdrop();
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        ShellIntegration.ToolWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), false);
        AppWindow.Resize(new SizeInt32(2, 2));
        root.Loaded += (_, _) => OpenPending();
        AppWindow.Closing += (_, e) => { if (!disposed) { e.Cancel = true; Dismiss(); } };
    }

    internal void ApplyTheme(ElementTheme theme)
    {
        root.RequestedTheme = theme;
        if (Menu is null) return;
        // Explicitly theme the popup presenter, which has a separate HWND.
        Menu.MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter))
        {
            Setters = { new Setter(FrameworkElement.RequestedThemeProperty, theme),
                        new Setter(FrameworkElement.MinWidthProperty, 240d) }
        };
        foreach (var item in Menu.Items) item.RequestedTheme = theme;
        if (root.XamlRoot is not null)
            foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
                if (popup.Child is MenuFlyoutPresenter presenter) presenter.RequestedTheme = theme;
    }

    internal void Show(PointInt32 anchor, ElementTheme theme)
    {
        if (disposed) return;
        Dismiss();
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        Menu = menu;
        void Item(int id, string text, string glyph)
        {
            var item = new MenuFlyoutItem { Text = text, Tag = id, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => { Dismiss(); command(id); };
            menu.Items.Add(item);
        }
        Item(1, T("打开主面板", "Open panel"), "\uE8A7");
        Item(4, T("隐藏主面板", "Hide panel"), "\uE8BB");
        Item(5, T("显示 / 隐藏桌面面板", "Toggle desktop panel"), "\uE7F4");
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(2, T("上一个配置", "Previous profile"), "\uE76B");
        Item(3, T("下一个配置", "Next profile"), "\uE76C");
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(6, T("退出聚合控制", "Exit Fluent Control"), "\uE8AC");
        menu.Closed += (_, _) =>
        {
            if (!disposed && ReferenceEquals(Menu, menu)) { pending = false; AppWindow.Hide(); }
        };
        ApplyTheme(theme);
        var area = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(Math.Clamp(anchor.X, area.X, area.X + Math.Max(0, area.Width - 2)),
                                     Math.Clamp(anchor.Y, area.Y, area.Y + Math.Max(0, area.Height - 2))));
        pending = true;
        Activate();
        ShellIntegration.Focus(WinRT.Interop.WindowNative.GetWindowHandle(this));
        OpenPending();
    }

    private void OpenPending()
    {
        if (disposed || !pending || root.XamlRoot is null || !root.IsLoaded || Menu is null) return;
        pending = false;
        Menu.XamlRoot = root.XamlRoot;
        Menu.ShowAt(root, new FlyoutShowOptions { Position = new Point(0, 0), ShowMode = FlyoutShowMode.Standard });
    }

    internal void Dismiss()
    {
        pending = false;
        var old = Menu; Menu = null;
        old?.Hide();
        if (!disposed) AppWindow.Hide();
    }
    public void Dispose()
    {
        if (disposed) return;
        Dismiss(); disposed = true; Close();
    }
}
