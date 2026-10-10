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
    private readonly Grid header = new() { ColumnSpacing = 4, RowSpacing = 2 };
    private readonly Grid footer = new() { ColumnSpacing = 6, Margin = new Thickness(0, 0, 24, 0) };
    private readonly Button monitorMode = new() { FontSize = 11, MinHeight = 28, Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock groupName = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button previousGroup = SmallButton("‹"), nextGroup = SmallButton("›"), previousProfile = SmallButton("‹"), nextProfile = SmallButton("›");
    private const double MinimumHeight = 168;
    private readonly Grid shield = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock hint = new() { FontSize = 11, LineHeight = 15, TextWrapping = TextWrapping.Wrap, MinHeight = 30, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock profileName = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<Action> sync = new();
    private readonly Action changed;
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TransparentBackdrop backdrop = new();
    private readonly nint hwnd;
    private readonly DesktopLayer layer;
    private readonly FrameworkElement dragGrip, resizeGrip;
    private AppSettings settings;
    private bool active, closing, moving;
    private int rowGeneration, pendingChanges;
    private PointInt32 pointerStart, positionStart, lastPointer;
    private readonly DispatcherTimer gestureTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool resizing;
    private long previousPress;
    private PointInt32 previousPressPoint;
    private SizeInt32 sizeStart;
    internal bool UsesLightText => root.RequestedTheme == ElementTheme.Dark;
    internal bool HasPendingChanges => pendingChanges > 0;
    internal bool IsUnlocked => active;
    internal int RowCount { get; private set; }
    internal double HintHeight => hint.ActualHeight;
    internal double HintBottom => hint.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y + hint.ActualHeight;
    internal double ContentHeight => root.ActualHeight;
    internal byte BackdropAlpha => backdrop.TintColor.A;
    internal nint Handle => hwnd;
    internal string LayerDiagnosticState => layer.DiagnosticState;
    internal DesktopPanelWindow(AppSettings settings, Action<int> switchGroup, Action<int> switchProfile, Action<bool> setMonitorMode, Action changed)
    {
        this.settings = settings; this.changed = changed;
        root.RequestedTheme = settings.DesktopLightText ? ElementTheme.Dark : ElementTheme.Light;
        SystemBackdrop = backdrop;
        Content = root;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        layer = new DesktopLayer(hwnd, action => DispatcherQueue.TryEnqueue(() => action()), () => SetUnlocked(false));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false; p.SetBorderAndTitleBar(false, false); p.IsMaximizable = false; p.IsMinimizable = false;
        }
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = new GridLength(30) });
        header.RowDefinitions.Add(new() { Height = new GridLength(30) });
        foreach (var width in new[] { new GridLength(28), new GridLength(1, GridUnitType.Star), new GridLength(28), new GridLength(28) }) header.ColumnDefinitions.Add(new() { Width = width });
        void AddButton(Button button, int row, int column, string id, Action action)
        {
            Grid.SetRow(button, row); Grid.SetColumn(button, column);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => { if (active && !closing) action(); }; header.Children.Add(button);
        }
        AddButton(previousGroup, 0, 0, "desktop-previous-group", () => switchGroup(-1));
        AddButton(nextGroup, 0, 2, "desktop-next-group", () => switchGroup(1));
        AddButton(previousProfile, 1, 0, "desktop-previous-profile", () => switchProfile(-1));
        AddButton(nextProfile, 1, 2, "desktop-next-profile", () => switchProfile(1));
        Grid.SetColumn(groupName, 1); header.Children.Add(groupName);
        Grid.SetRow(profileName, 1); Grid.SetColumn(profileName, 1); header.Children.Add(profileName);
        body.Children.Add(header);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 32 };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(hint); Grid.SetColumn(monitorMode, 1); footer.Children.Add(monitorMode);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(monitorMode, "desktop-monitor-mode");
        monitorMode.Click += (_, _) => { if (active && !closing) setMonitorMode(!this.settings.GroupDesktopMonitors); };
        Grid.SetRow(footer, 2); body.Children.Add(footer);
        root.Children.Add(body); root.Children.Add(shield);
        dragGrip = Grip("&#xE7C2;", false); dragGrip.HorizontalAlignment = HorizontalAlignment.Right; dragGrip.VerticalAlignment = VerticalAlignment.Top; dragGrip.Margin = new Thickness(0, 10, 10, 0);
        resizeGrip = Grip("&#xE70A;", true); resizeGrip.HorizontalAlignment = HorizontalAlignment.Right; resizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(dragGrip); root.Children.Add(resizeGrip);
        ConfigureGrip(dragGrip, false); ConfigureGrip(resizeGrip, true);
        gestureTimer.Tick += (_, _) =>
        {
            if (!ShellIntegration.PrimaryButtonPressed()) { CompleteGesture(); return; }
            var now = ShellIntegration.PointerPosition();
            if (now.X == lastPointer.X && now.Y == lastPointer.Y) return;
            lastPointer = now;
            ApplyGeometryDelta(resizing, now.X - pointerStart.X, now.Y - pointerStart.Y);
        };
        shield.DoubleTapped += (_, e) => { e.Handled = true; if (!active) SetUnlocked(true); };
        shield.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(shield).Properties.IsLeftButtonPressed || active) return;
            var now = Environment.TickCount64; var point = ShellIntegration.PointerPosition();
            // WinUI can cancel its double-tap recognizer on a no-activate window.
            if (previousPress > 0 && now - previousPress <= ShellIntegration.DoubleClickMilliseconds &&
                Math.Abs(point.X - previousPressPoint.X) <= 4 * Scale && Math.Abs(point.Y - previousPressPoint.Y) <= 4 * Scale)
            { previousPress = 0; e.Handled = true; SetUnlocked(true); }
            else { previousPress = now; previousPressPoint = point; }
        };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CompleteGesture(); SetUnlocked(false); e.Handled = true; } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && !moving) SetUnlocked(false); };
        AppWindow.Changed += (_, e) => { if (e.DidPositionChange && !closing && !moving) { positionTimer.Stop(); positionTimer.Start(); } };
        positionTimer.Tick += (_, _) => { positionTimer.Stop(); SaveGeometry(false); };
        Closed += (_, _) => { closing = true; rowGeneration++; positionTimer.Stop(); gestureTimer.Stop(); layer.Dispose(); };
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
            e.Handled = true; moving = true; resizing = resize;
            pointerStart = lastPointer = ShellIntegration.PointerPosition(); positionStart = AppWindow.Position; sizeStart = ShellIntegration.ClientSize(hwnd);
            // Track only while the primary button is held. Borderless transparent
            // windows can cancel XAML capture when the pointer leaves a grip.
            gestureTimer.Start();
        }), true);
    }
    private void CompleteGesture()
    {
        gestureTimer.Stop();
        if (!moving || closing) return;
        var now = ShellIntegration.PointerPosition();
        ApplyGeometryDelta(resizing, now.X - pointerStart.X, now.Y - pointerStart.Y);
        moving = false; ClampPosition(); SaveGeometry(resizing);
    }
    private void ApplyGeometryDelta(bool resize, int x, int y)
    {
        if (resize)
        {
            var scale = Scale;
            AppWindow.ResizeClient(new SizeInt32((int)Math.Clamp(sizeStart.Width + x, 280 * scale, 900 * scale), (int)Math.Clamp(sizeStart.Height + y, MinimumHeight * scale, 1000 * scale)));
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
        previousPress = 0;
        active = value; shield.Visibility = value ? Visibility.Collapsed : Visibility.Visible; body.IsHitTestVisible = value;
        layer.SetLocked(!value);
        hint.Text = value ? Strings.T("已解锁 · Esc 锁定", "Unlocked · Esc to lock") : Strings.T("双击解锁", "Double-click to unlock");
        if (value)
        {
            Activate();
            // The second press was received under MA_NOACTIVATE. Complete its
            // foreground/Z-order transition now; do not wait for a third click.
            layer.BringUnlockedToForeground();
            (body.Children[0] as Grid)?.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled)?.Focus(FocusState.Programmatic);
        }
    }
    internal void UpdateRows(IReadOnlyList<PanelRow> source, AppSettings settings, Func<IReadOnlyList<ControlChannel>, double, bool, Task> apply)
    {
        this.settings = settings;
        monitorMode.Content = settings.GroupDesktopMonitors ? Strings.T("整体控制", "Overall control") : Strings.T("单独控制", "Individual");
        ToolTipService.SetToolTip(monitorMode, settings.GroupDesktopMonitors ? Strings.T("单独控制", "Individual") : Strings.T("整体控制", "Overall control"));
        Title = Strings.AppName + " · " + Strings.T("桌面面板", "Desktop panel");
        ToolTipService.SetToolTip(dragGrip, Strings.T("拖动面板", "Drag panel"));
        ToolTipService.SetToolTip(resizeGrip, Strings.T("拖动以调整大小", "Drag to resize"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dragGrip, Strings.T("拖动面板", "Drag panel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(resizeGrip, Strings.T("拖动以调整大小", "Drag to resize"));
        rowGeneration++; var version = rowGeneration;
        rows.Children.Clear(); sync.Clear(); RowCount = source.Count;
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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(row, "desktop-" + item.Key);
            var label = new TextBlock { Text = item.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(label, item.Name + (item.Detail.Length > 0 ? "\n" + item.Detail : "")); row.Children.Add(label);
            var first = item.Targets[0];
            var number = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(number, 2); row.Children.Add(number);
            var updating = false;
            CancellationTokenSource? pending = null;
            if ((item.Options ?? first.Options) is { } options)
            {
                var picker = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", MinHeight = 30, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
                var choices = new StackPanel { Spacing = 2 };
                choices.Children.Add(picker);
                var readback = new TextBlock { FontSize = 11, Opacity = .7, TextWrapping = TextWrapping.Wrap };
                if (first.IsRepeatableChoice) choices.Children.Add(readback);
                Grid.SetColumn(choices, 1); Grid.SetColumnSpan(choices, 2); row.Children.Add(choices); number.Visibility = Visibility.Collapsed;
                void Update()
                {
                    updating = true;
                    var same = item.Targets.All(c => c.Value == first.Value);
                    picker.SelectedItem = !first.IsRepeatableChoice && same ? options.FirstOrDefault(x => x.Value == first.Value) : null;
                    picker.PlaceholderText = first.IsRepeatableChoice ? Strings.T("选择要应用的选项（可重复发送）", "Choose an option to apply (repeatable)") : same && picker.SelectedItem is null
                        ? Strings.F("当前值 0x{0}（不可重放）", "Current 0x{0} (not replayable)", ((uint)first.Value).ToString("X")) : Strings.T("不同", "Mixed");
                    readback.Text = ControlOperations.ChoiceReadbackText(item.Targets);
                    updating = false;
                }
                sync.Add(Update); Update();
                picker.SelectionChanged += async (_, _) =>
                {
                    if (!active || updating || !picker.IsEnabled || version != rowGeneration || picker.SelectedItem is not ControlOption option) return;
                    pendingChanges++; picker.IsEnabled = false;
                    try
                    {
                        var targets = item.Targets.Where(c => c.Options?.Any(o => o.Value == option.Value) == true).ToArray();
                        await apply(targets, option.Value, item.Linked);
                        if (!closing && version == rowGeneration) RefreshValues();
                    }
                    finally { pendingChanges--; if (!closing && version == rowGeneration) { Update(); picker.IsEnabled = true; } }
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
                    updating = true;
                    var values = item.Targets.Select(x => ControlOperations.DisplayValue(x, item.Linked)).ToArray();
                    slider.Value = values.Average();
                    slider.IsEnabled = item.Targets.Any(c => c.IsAvailable);
                    number.Text = !slider.IsEnabled ? "—" : values.Max() - values.Min() > .5 ? "≠" : Math.Round(slider.Value) + first.Unit;
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
                        if (!closing && active && version == rowGeneration) await apply(item.Targets, value, item.Linked);
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
        footer.Measure(new Windows.Foundation.Size(width - 20, double.PositiveInfinity));
        header.Measure(new Windows.Foundation.Size(width - 20, double.PositiveInfinity));
        var minimum = Math.Max(MinimumHeight, 20 + header.DesiredSize.Height + 12 + 32 + footer.DesiredSize.Height);
        var visibleRowsHeight = rows.Children.OfType<FrameworkElement>().Take(settings.DesktopMaxRows).Sum(row => row.DesiredSize.Height);
        var automaticHeight = Math.Max(minimum, 20 + header.DesiredSize.Height + 12 + visibleRowsHeight + footer.DesiredSize.Height);
        var height = Math.Clamp(settings.DesktopHeight ?? automaticHeight, minimum, 1000);
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale)));
        ClampPosition();
    }
    internal void SetNavigation(string group, string profile, bool canSwitchGroup, bool canSwitchProfile)
    {
        previousGroup.Visibility = nextGroup.Visibility = groupName.Visibility = Visibility.Collapsed;
        header.RowDefinitions[0].Height = new GridLength(0);
        groupName.Text = group;
        profileName.Text = Strings.T("总配置", "Global profile") + " · " + profile;
        ToolTipService.SetToolTip(groupName, groupName.Text); ToolTipService.SetToolTip(profileName, profileName.Text);
        void Label(Button button, string text, bool enabled)
        {
            button.IsEnabled = enabled; ToolTipService.SetToolTip(button, text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        }
        Label(previousGroup, Strings.T("上一个分组", "Previous group"), canSwitchGroup);
        Label(nextGroup, Strings.T("下一个分组", "Next group"), canSwitchGroup);
        Label(previousProfile, Strings.T("上一个总配置", "Previous global profile"), canSwitchProfile);
        Label(nextProfile, Strings.T("下一个总配置", "Next global profile"), canSwitchProfile);
    }
    internal void RefreshValues() { foreach (var update in sync) update(); }
    internal void ShowPanel() { SetUnlocked(false); AppWindow.Show(false); layer.Lower(); }
    private static Button SmallButton(string content) => new() { Content = content, Width = 28, Height = 28, Padding = new Thickness(0), FontSize = 18, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
}
