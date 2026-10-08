using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.Graphics;
namespace FluentControl;

internal sealed class DesktopPanelWindow : Window
{
    private readonly Grid root = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Grid body = new() { Margin = new Thickness(10), RowSpacing = 6 };
    private readonly StackPanel rows = new();
    private readonly Grid shield = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock hint = new() { FontSize = 11, LineHeight = 15, TextWrapping = TextWrapping.Wrap, MinHeight = 30, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock profileName = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<Action> sync = new();
    private readonly Action changed;
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TransparentBackdrop backdrop = new();
    private readonly nint hwnd;
    private readonly IDisposable sizeLimits;
    private readonly FrameworkElement dragGrip, resizeGrip;
    private AppSettings settings;
    private bool active, closing, moving;
    private int rowGeneration, pendingChanges;
    private PointInt32 pointerStart, positionStart;
    private SizeInt32 sizeStart;
    internal bool HasPendingChanges => pendingChanges > 0;
    internal bool IsUnlocked => active;
    internal int RowCount { get; private set; }
    internal double HintHeight => hint.ActualHeight;
    internal double HintBottom => hint.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y + hint.ActualHeight;
    internal double ContentHeight => root.ActualHeight;
    internal byte BackdropAlpha => backdrop.TintColor.A;
    internal nint Handle => hwnd;
    internal DesktopPanelWindow(AppSettings settings, Action<int> switchProfile, Action changed)
    {
        this.settings = settings; this.changed = changed;
        SystemBackdrop = backdrop;
        Content = root;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = true; p.SetBorderAndTitleBar(false, false); p.IsMaximizable = false; p.IsMinimizable = false;
        }
        sizeLimits = ShellIntegration.LimitPanelSize(hwnd);
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 4, MinHeight = 32 };
        foreach (var width in new[] { new GridLength(28), new GridLength(1, GridUnitType.Star), new GridLength(28), new GridLength(28) }) header.ColumnDefinitions.Add(new() { Width = width });
        var previous = SmallButton("‹"); previous.Click += (_, _) => { if (active) switchProfile(-1); }; header.Children.Add(previous);
        Grid.SetColumn(profileName, 1); header.Children.Add(profileName);
        var next = SmallButton("›"); next.Click += (_, _) => { if (active) switchProfile(1); }; Grid.SetColumn(next, 2); header.Children.Add(next);
        body.Children.Add(header);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 32 };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        Grid.SetRow(hint, 2); body.Children.Add(hint);
        root.Children.Add(body); root.Children.Add(shield);
        dragGrip = Grip("&#xE7C2;", false); dragGrip.HorizontalAlignment = HorizontalAlignment.Right; dragGrip.VerticalAlignment = VerticalAlignment.Top; dragGrip.Margin = new Thickness(0, 10, 10, 0);
        resizeGrip = Grip("&#xE70A;", true); resizeGrip.HorizontalAlignment = HorizontalAlignment.Right; resizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(dragGrip); root.Children.Add(resizeGrip);
        ConfigureGrip(dragGrip, false); ConfigureGrip(resizeGrip, true);
        shield.DoubleTapped += (_, e) => { e.Handled = true; SetUnlocked(true); };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { SetUnlocked(false); e.Handled = true; } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && !moving) SetUnlocked(false); };
        AppWindow.Changed += (_, e) => { if (e.DidPositionChange && !closing && !moving) { positionTimer.Stop(); positionTimer.Start(); } };
        positionTimer.Tick += (_, _) => { positionTimer.Stop(); SaveGeometry(false); };
        Closed += (_, _) => { closing = true; rowGeneration++; positionTimer.Stop(); sizeLimits.Dispose(); };
        var work = DisplayArea.Primary.WorkArea;
        AppWindow.Move(new PointInt32(settings.DesktopX ?? work.X + work.Width - 380, settings.DesktopY ?? work.Y + 60));
        SetUnlocked(false);
    }
    private static FrameworkElement Grip(string glyph, bool resize) => (FrameworkElement)XamlReader.Load(
        "<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='28' Height='28' Background='Transparent'>" +
        (resize ? "<Path Data='M2,14 L14,2 M7,14 L14,7 M12,14 L14,12' Width='16' Height='16' Stroke='{ThemeResource TextFillColorSecondaryBrush}' StrokeThickness='1.4'/>" : "<FontIcon Glyph='" + glyph + "' FontSize='12' Opacity='0.75' />") + "</Grid>");
    private void ConfigureGrip(FrameworkElement grip, bool resize)
    {
        grip.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            e.Handled = true; moving = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                try { ShellIntegration.MoveOrResize(hwnd, resize); }
                finally { moving = false; ClampPosition(); SaveGeometry(resize); }
            });
        }), true);
    }
    private void ApplyGeometryDelta(bool resize, int x, int y)
    {
        if (resize)
        {
            var scale = Scale;
            AppWindow.ResizeClient(new SizeInt32((int)Math.Clamp(sizeStart.Width + x, 280 * scale, 900 * scale), (int)Math.Clamp(sizeStart.Height + y, 144 * scale, 1000 * scale)));
        }
        else AppWindow.Move(new PointInt32(positionStart.X + x, positionStart.Y + y));
    }
    internal void CheckGeometryDelta(bool resize, int x, int y)
    {
        positionStart = AppWindow.Position; sizeStart = ShellIntegration.ClientSize(hwnd);
        ApplyGeometryDelta(resize, x, y); SaveGeometry(resize);
    }
    private double Scale => root.XamlRoot?.RasterizationScale ?? Math.Max(1, ShellIntegration.Dpi(hwnd) / 96d);
    private void SaveGeometry(bool resized)
    {
        if (closing) return;
        settings.DesktopX = AppWindow.Position.X; settings.DesktopY = AppWindow.Position.Y;
        if (resized) { var size = ShellIntegration.ClientSize(hwnd); settings.DesktopWidth = size.Width / Scale; settings.DesktopHeight = size.Height / Scale; }
        changed();
    }
    private void ClampPosition()
    {
        var p = AppWindow.Position; var s = AppWindow.Size;
        var area = DisplayArea.GetFromRect(new RectInt32(p.X, p.Y, s.Width, s.Height), DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(Math.Clamp(p.X, area.X, Math.Max(area.X, area.X + area.Width - s.Width)), Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - s.Height))));
    }
    internal void SetUnlocked(bool value)
    {
        if (closing) return;
        active = value; shield.Visibility = value ? Visibility.Collapsed : Visibility.Visible; body.IsHitTestVisible = value;
        ShellIntegration.ToolWindow(hwnd, false); // Activation must remain available to the native pointer capture used by the grips.
        hint.Text = value ? Strings.T("已解锁 · Esc 锁定", "Unlocked · Esc to lock") : Strings.T("双击解锁", "Double-click to unlock");
        if (value) { Activate(); (body.Children[0] as Grid)?.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Programmatic); }
    }
    internal void UpdateRows(IReadOnlyList<PanelRow> source, AppSettings settings, Func<IReadOnlyList<ControlChannel>, double, Task> apply)
    {
        this.settings = settings;
        Title = Strings.AppName + " · " + Strings.T("桌面面板", "Desktop panel");
        ToolTipService.SetToolTip(dragGrip, Strings.T("拖动面板", "Drag panel"));
        ToolTipService.SetToolTip(resizeGrip, Strings.T("拖动以调整大小", "Drag to resize"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dragGrip, Strings.T("拖动面板", "Drag panel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(resizeGrip, Strings.T("拖动以调整大小", "Drag to resize"));
        rowGeneration++; var version = rowGeneration;
        rows.Children.Clear(); sync.Clear(); RowCount = source.Count;
        root.RequestedTheme = settings.DesktopLightText ? ElementTheme.Dark : ElementTheme.Light;
        var alpha = (byte)Math.Round(Math.Clamp(settings.DesktopOpacity, 10, 85) * 2.55);
        backdrop.TintColor = settings.DesktopLightText ? Windows.UI.Color.FromArgb(alpha, 24, 28, 36) : Windows.UI.Color.FromArgb(alpha, 248, 250, 252);
        string? previousGroup = null;
        foreach (var item in source)
        {
            var row = new Grid { MinHeight = 32, ColumnSpacing = 7, Margin = new Thickness(0, previousGroup is not null && previousGroup != item.Group ? 9 : 0, 0, 0) };
            previousGroup = item.Group;
            row.ColumnDefinitions.Add(new() { Width = new GridLength(104) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(40) });
            var label = new TextBlock { Text = item.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(label, item.Name); row.Children.Add(label);
            var first = item.Targets[0];
            var number = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(number, 2); row.Children.Add(number);
            var updating = false;
            CancellationTokenSource? pending = null;
            if (first.Options is { } options)
            {
                var picker = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", MinHeight = 30, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
                Grid.SetColumn(picker, 1); Grid.SetColumnSpan(picker, 2); row.Children.Add(picker); number.Visibility = Visibility.Collapsed;
                void Update() { updating = true; picker.SelectedItem = options.FirstOrDefault(x => x.Value == first.Value); updating = false; }
                sync.Add(Update); Update();
                picker.SelectionChanged += async (_, _) =>
                {
                    if (!active || updating || picker.SelectedItem is not ControlOption option) return;
                    await apply(item.Targets, option.Value); if (!closing && version == rowGeneration) RefreshValues();
                };
            }
            else
            {
                var slider = new Slider { Minimum = first.Minimum, Maximum = first.Maximum, StepFrequency = 1, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(slider, 1); row.Children.Add(slider);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, item.Name);
                void Update()
                {
                    if (pending is not null) return;
                    updating = true; slider.Value = item.Targets.Average(x => x.Value);
                    number.Text = item.Targets.Max(x => x.Value) - item.Targets.Min(x => x.Value) > .5 ? "≠" : Math.Round(slider.Value) + first.Unit;
                    updating = false;
                }
                sync.Add(Update); Update();
                slider.ValueChanged += async (_, e) =>
                {
                    if (updating || !active) return;
                    var value = e.NewValue;
                    pending?.Cancel(); var request = new CancellationTokenSource(); pending = request;
                    pendingChanges++;
                    number.Text = Math.Round(value) + first.Unit;
                    try
                    {
                        await Task.Delay(150, request.Token);
                        if (!closing && active && version == rowGeneration) await apply(item.Targets, value);
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        if (ReferenceEquals(pending, request)) { pending = null; if (!closing && version == rowGeneration) Update(); }
                        request.Dispose();
                        pendingChanges--;
                    }
                };
            }
            rows.Children.Add(row);
        }
        if (source.Count == 0) rows.Children.Add(new TextBlock { Text = Strings.T("请在设置中选择控制项", "Choose rows in Settings"), FontSize = 12 });
        var width = Math.Clamp(settings.DesktopWidth, 280, 900);
        rows.Measure(new Windows.Foundation.Size(width - 20, double.PositiveInfinity));
        hint.Measure(new Windows.Foundation.Size(width - 44, double.PositiveInfinity));
        var automaticHeight = Math.Max(144, 20 + 32 + 12 + rows.DesiredSize.Height + hint.DesiredSize.Height);
        var height = Math.Clamp(settings.DesktopHeight ?? automaticHeight, 144, 1000);
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale)));
        ClampPosition();
    }
    internal void SetProfile(string name) { profileName.Text = name; ToolTipService.SetToolTip(profileName, name); }
    internal void RefreshValues() { foreach (var update in sync) update(); }
    internal void ShowPanel() { SetUnlocked(false); AppWindow.Show(false); }
    private static Button SmallButton(string content) => new() { Content = content, Width = 28, Height = 28, Padding = new Thickness(0), FontSize = 18, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
}
