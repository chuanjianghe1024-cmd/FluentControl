using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
namespace FluentControl;

internal sealed class DesktopPanelWindow : Window
{
    private readonly Grid root = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly StackPanel body = new() { Spacing = 0, Margin = new Thickness(8) };
    private readonly StackPanel rows = new();
    private readonly Grid shield = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock hint = new() { FontSize = 10, Opacity = .8, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock profileName = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<Action> sync = new();
    private readonly Action<int> switchProfile;
    private readonly Action<int, int> savePosition;
    private readonly DispatcherTimer positionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly nint hwnd;
    private bool active, closing;
    private int rowGeneration, pendingChanges;
    internal bool HasPendingChanges => pendingChanges > 0;
    internal bool IsUnlocked => active;
    internal int RowCount { get; private set; }
    internal DesktopPanelWindow(AppSettings settings, Action<int> switchProfile, Action<int, int> savePosition)
    {
        this.switchProfile = switchProfile; this.savePosition = savePosition;
        Title = "FluentControl Desktop";
        SystemBackdrop = new TransparentBackdrop();
        Content = root; root.Children.Add(body); root.Children.Add(shield);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false); p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false;
        }
        var header = new Grid { ColumnSpacing = 4, Height = 30 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        var previous = SmallButton("‹"); previous.Click += (_, _) => switchProfile(-1); header.Children.Add(previous);
        Grid.SetColumn(profileName, 1); header.Children.Add(profileName);
        var next = SmallButton("›"); next.Click += (_, _) => switchProfile(1); Grid.SetColumn(next, 2); header.Children.Add(next);
        var drag = SmallButton("⠿"); Grid.SetColumn(drag, 3); header.Children.Add(drag);
        ToolTipService.SetToolTip(drag, Strings.T("拖动面板", "Drag panel"));
        drag.PointerPressed += (_, e) => { if (active && e.GetCurrentPoint(drag).Properties.IsLeftButtonPressed) ShellIntegration.Drag(hwnd); };
        body.Children.Add(header); body.Children.Add(rows); body.Children.Add(hint);
        shield.DoubleTapped += (_, e) => { e.Handled = true; SetUnlocked(true); };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { SetUnlocked(false); e.Handled = true; } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) SetUnlocked(false); };
        AppWindow.Changed += (_, e) => { if (e.DidPositionChange && !closing) { positionTimer.Stop(); positionTimer.Start(); } };
        positionTimer.Tick += (_, _) => { positionTimer.Stop(); savePosition(AppWindow.Position.X, AppWindow.Position.Y); };
        Closed += (_, _) => { closing = true; rowGeneration++; positionTimer.Stop(); };
        var work = DisplayArea.Primary.WorkArea;
        var x = settings.DesktopX ?? work.X + work.Width - 340;
        var y = settings.DesktopY ?? work.Y + 60;
        // Recover a panel stranded on a disconnected monitor.
        var area = DisplayArea.GetFromRect(new Windows.Graphics.RectInt32(x, y, 320, 100), DisplayAreaFallback.Nearest).WorkArea;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - 320));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - 100));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        SetUnlocked(false);
    }
    internal void SetUnlocked(bool value)
    {
        if (closing) return;
        active = value;
        shield.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        body.IsHitTestVisible = value;
        ShellIntegration.ToolWindow(hwnd, !value);
        hint.Text = value ? Strings.T("可调节 · Esc 锁定 · 拖动 ⠿ 移动", "Active · Esc locks · Drag ⠿ to move") : Strings.T("双击解锁", "Double-click to unlock");
        if (value) { Activate(); (body.Children[0] as Grid)?.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Programmatic); }
    }
    internal void UpdateRows(IReadOnlyList<PanelRow> source, AppSettings settings, Func<IReadOnlyList<ControlChannel>, double, Task> apply)
    {
        rowGeneration++; var version = rowGeneration;
        rows.Children.Clear(); sync.Clear(); RowCount = source.Count;
        root.RequestedTheme = settings.DesktopLightText ? ElementTheme.Dark : ElementTheme.Light;
        string? previousGroup = null;
        foreach (var item in source)
        {
            var row = new Grid { Height = 30, ColumnSpacing = 7, Margin = new Thickness(0, previousGroup is not null && previousGroup != item.Group ? 9 : 0, 0, 0) };
            previousGroup = item.Group;
            row.ColumnDefinitions.Add(new() { Width = new GridLength(100) });
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
                var picker = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", MinHeight = 26, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
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
        var height = 65 + source.Count * 30 + Math.Max(0, source.Select(x => x.Group).Distinct().Count() - 1) * 9;
        var scale = ShellIntegration.Dpi(hwnd) / 96d;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(320 * scale), (int)(Math.Max(100, height) * scale)));
    }
    internal void SetProfile(string name) => profileName.Text = name;
    internal void RefreshValues() { foreach (var update in sync) update(); }
    internal void ShowPanel() { SetUnlocked(false); AppWindow.Show(false); }
    private static Button SmallButton(string content) => new() { Content = content, Width = 26, Height = 26, Padding = new Thickness(0), FontSize = 16, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
}
