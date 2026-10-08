using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Globalization;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow : Window
{
    private AudioService? audio;
    private MonitorService? monitors;
    private readonly SemaphoreSlim gate = new(1);
    private readonly List<Action> refreshRows = new();
    private readonly List<Window> identificationWindows = new();
    private readonly DispatcherTimer identificationTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly MonitorPreferences? preferences;
    private readonly string? preferencesError;
    private List<MonitorDevice> displayDevices = new();
    private int generation;
    private bool refreshing, closed, initialized;
    private readonly bool uiTest = Environment.GetCommandLineArgs().Contains("--ui-test");

    public MainWindow()
    {
        InitializeComponent();
        Title = "FluentControl";
        try { SystemBackdrop = new MicaBackdrop(); }
        catch (Exception ex) { StartupLog.Write("Mica unavailable: " + ex); }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));
        try
        {
            preferences = new MonitorPreferences(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluentControl", uiTest ? "test-monitor-names.json" : "monitor-names.json"));
        }
        catch (Exception ex) { preferencesError = "无法读取显示器名称：" + ex.Message; }
        InitializeFeatures();
        initialized = true;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        identificationTimer.Tick += (_, _) => CloseIdentification();
        Root.Loaded += async (_, _) =>
        {
            LocalizeUi();
            StartupLog.Write("Main window content loaded");
            await RefreshAsync();
            FinishLaunch();
            if (uiTest && !closed) await RunUiChecksAsync();
        };
        Closed += async (_, _) =>
        {
            closed = true;
            ShutdownFeatures();
            generation++;
            CloseIdentification();
            await gate.WaitAsync();
            try { audio?.Dispose(); monitors?.Dispose(); }
            finally { gate.Release(); }
        };
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!initialized) return;
        notificationContext++; ClearStatus();
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "monitors";
        DisplayPanel.Visibility = tag == "monitors" ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility = tag == "audio" ? Visibility.Visible : Visibility.Collapsed;
        MousePanel.Visibility = tag == "mouse" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePageTitle();
    }
    private void DisplayMode_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized) return;
        MonitorRows.Visibility = DisplayMode.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        CombinedRows.Visibility = DisplayMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (refreshing || closed) return;
        refreshing = true;
        RefreshButton.IsEnabled = false;
        IdentifyButton.IsEnabled = false;
        var version = ++generation;
        var page = notificationContext;
        desktopPanel?.SetUnlocked(false);
        audioChannels = new(); mouseChannels = new(); displayDevices = new();
        refreshRows.Clear();
        foreach (var panel in new[] { AudioRows, OtherAudioRows, MonitorRows, CombinedRows, MouseRows }) panel.Children.Clear();
        CloseIdentification();
        ShowStatus(T("正在读取设备…", "Reading devices…"), InfoBarSeverity.Informational);
        await gate.WaitAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                audio?.Dispose(); audio = null;
                monitors?.Dispose(); monitors = null;
                var errors = new List<string>();
                var a = new List<ControlChannel>();
                var m = new List<MonitorDevice>();
                var mouse = new List<ControlChannel>();
                if (uiTest) return (a: UiTestData.Audio(), m: UiTestData.Monitors(), mouse: UiTestData.Mouse(), errors);
                try { audio = new AudioService(); a = audio.Enumerate(); } catch (Exception ex) { errors.Add("音频：" + ex.Message); }
                try { monitors = new MonitorService(); m = monitors.Enumerate(); } catch (Exception ex) { errors.Add("显示器：" + ex.Message); }
                try { mouse = MouseService.Enumerate(); } catch (Exception ex) { errors.Add("鼠标：" + ex.Message); }
                return (a, m, mouse, errors);
            });
            if (closed) return;
            displayDevices = result.m; audioChannels = result.a; mouseChannels = result.mouse;
            if (preferencesError is not null) result.errors.Add(preferencesError);
            for (var i = 0; i < displayDevices.Count; i++)
            {
                var device = displayDevices[i];
                try { device.Preference = preferences?.GetOrAdd(device.Id) ?? new() { Label = "M" + (i + 1) }; }
                catch (Exception ex) { device.Preference = new() { Label = "M" + (i + 1) }; result.errors.Add("名称保存失败：" + ex.Message); }
                MonitorRows.Children.Add(CreateMonitorCard(device, version));
            }
            RenderCombined(version);
            foreach (var channel in result.a)
                (channel.IsDefaultAudio ? AudioRows : OtherAudioRows).Children.Add(CreateRow(channel, version));
            if (AudioRows.Children.Count == 0) AudioRows.Children.Add(Empty(T("没有可控制的默认音频设备。可展开其他设备，或检查 Windows 声音设置。", "No controllable default audio devices. Expand other devices or check Windows sound settings.")));
            var otherCount = result.a.Count(x => !x.IsDefaultAudio);
            OtherAudioExpander.Header = T($"其他音频设备 · {otherCount}", $"Other audio devices · {otherCount}");
            OtherAudioExpander.Visibility = otherCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var channel in result.mouse) MouseRows.Children.Add(CreateRow(channel, version));
            if (MouseRows.Children.Count == 0) MouseRows.Children.Add(Empty(T("无法读取鼠标设置，请使用下方 Windows 设置入口。", "Cannot read mouse settings. Open Windows Settings below.")));
            MonitorSummary.Text = T($"{displayDevices.Count} 台显示器 · M 编号为本应用标记", $"{displayDevices.Count} displays · M numbers are FluentControl labels");
            if (displayDevices.Count == 0) MonitorRows.Children.Add(Empty(T("没有检测到显示器。", "No displays detected.")));
            IdentifyButton.IsEnabled = displayDevices.Count > 0;
            BuildSettings(); RefreshDesktopPanel(); RefreshCrosshair();
            ShowStatus(result.errors.Count > 0 ? string.Join("；", result.errors) : T("已读取设备。", "Devices refreshed."),
                result.errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, page);
        }
        catch (Exception ex)
        {
            StartupLog.Write("Refresh failed: " + ex);
            if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error, page);
        }
        finally { refreshing = false; RefreshButton.IsEnabled = true; gate.Release(); }
    }

    private FrameworkElement CreateMonitorCard(MonitorDevice device, int version)
    {
        var body = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
        var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(4, 0, 4, 8) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4 };
        var title = new TextBlock { Text = MonitorTitle(device), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = $"{device.Model} · {device.Width} × {device.Height}" + (device.IsPrimary ? T(" · 主显示器", " · Primary") : ""), FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(text);
        var rename = new Button { Content = T("重命名", "Rename"), VerticalAlignment = VerticalAlignment.Center, IsEnabled = preferences is not null };
        AutomationProperties.SetName(rename, T("重命名 ", "Rename ") + device.Preference.Label);
        rename.Click += async (_, _) =>
        {
            var input = new TextBox { Text = device.DisplayName, MaxLength = 40, PlaceholderText = T("例如：左屏、右屏、竖屏", "Left, right, portrait…") };
            var dialog = new ContentDialog { Title = T("重命名 ", "Rename ") + device.Preference.Label, Content = input, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot };
            input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
            try
            {
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
                preferences!.Rename(device.Id, input.Text);
                title.Text = MonitorTitle(device);
                BuildSettings(); RefreshDesktopPanel();
                ShowStatus(T($"已保存名称：{device.DisplayName}", $"Name saved: {device.DisplayName}"), InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowStatus(T("名称未保存：", "Name not saved: ") + ex.Message, InfoBarSeverity.Error); }
        };
        Grid.SetColumn(rename, 1); header.Children.Add(rename); body.Children.Add(header);
        foreach (var channel in device.Channels) body.Children.Add(CreateRow(channel, version));
        if (device.Channels.Count == 0) body.Children.Add(Empty(T("此屏幕没有可读取的控制项。请检查显示器菜单中的 DDC/CI。", "No readable controls. Check DDC/CI in the monitor menu.")));
        return Card(body);
    }
    private static string MonitorTitle(MonitorDevice device) => device.DisplayName == device.Preference.Label ? device.Preference.Label : $"{device.Preference.Label} · {device.DisplayName}";

    private void RenderCombined(int version)
    {
        CombinedRows.Children.Add(Empty(T("拖动后，将支持该属性的显示器设为相同百分比；当前数值不同会标记为“不同”。", "Linked sliders apply the same percentage to supported displays. Different values are marked as Mixed.")));
        foreach (var key in new[] { "brightness", "contrast", "speaker", "temperature" })
        {
            var targets = displayDevices.SelectMany(x => x.Channels).Where(x => x.PropertyKey == key).ToList();
            if (targets.Count == 0) continue;
            var first = targets[0];
            var options = first.Options?.Where(x => targets.All(c => c.Options?.Any(o => o.Value == x.Value) == true)).ToArray();
            if (options is { Length: 0 }) continue;
            var aggregate = new ControlChannel { Name = first.Name, Detail = T($"{targets.Count} / {displayDevices.Count} 台支持", $"{targets.Count} / {displayDevices.Count} supported"), PropertyKey = key, Glyph = first.Glyph, Value = targets.Average(x => x.Value), Options = options, Write = _ => { } };
            CombinedRows.Children.Add(CreateRow(aggregate, version, targets));
        }
        if (CombinedRows.Children.Count == 1) CombinedRows.Children.Add(Empty(T("暂时没有可以一起调节的显示器属性。", "No display controls are available for linked adjustment.")));
    }

    private FrameworkElement CreateRow(ControlChannel channel, int version, IReadOnlyList<ControlChannel>? group = null)
    {
        if (channel.Options is not null) return CreateChoiceRow(channel, version, group);
        IReadOnlyList<ControlChannel> targets = group ?? new[] { channel };
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(16, 12, 16, 12) };
        foreach (var width in new[] { new GridLength(24), new GridLength(2, GridUnitType.Star), new GridLength(3, GridUnitType.Star), new GridLength(54), GridLength.Auto })
            grid.ColumnDefinitions.Add(new() { Width = width });
        grid.Children.Add(new FontIcon { Glyph = channel.Glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = (group is null ? "" : T("统一", "Linked ")) + Channel(channel), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var detailText = channel.PropertyKey switch
        {
            "mouse-speed" => T("慢 1 — 快 20 · Windows 系统速度", "Slow 1 — Fast 20 · Windows pointer speed"),
            "pointer-size" => T("小 1 — 大 15 · 保留指针颜色与主题", "Small 1 — Large 15 · Keeps pointer color and theme"),
            _ => channel.Detail
        };
        var detail = new TextBlock { Text = detailText, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap, Visibility = detailText.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
        labels.Children.Add(detail);
        ToolTipService.SetToolTip(labels, Channel(channel) + "\n" + detailText);
        Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var slider = new Slider { Minimum = channel.Minimum, Maximum = channel.Maximum, StepFrequency = 1, Value = channel.Value, VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
        AutomationProperties.SetName(slider, channel.Name + " " + channel.Detail);
        Grid.SetColumn(slider, 2); grid.Children.Add(slider);
        var number = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(number, 3); grid.Children.Add(number);
        CancellationTokenSource? pending = null;
        var synchronizing = false;
        void SyncRow()
        {
            if (pending is not null) return;
            synchronizing = true;
            var minimum = targets.Min(x => x.Value);
            var maximum = targets.Max(x => x.Value);
            slider.Value = targets.Average(x => x.Value);
            number.Text = maximum - minimum > .5 ? T("不同", "Mixed") : Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture) + channel.Unit;
            if (group is not null) detail.Text = channel.Detail + (maximum - minimum > .5 ? $" · {minimum:0}–{maximum:0}%" : "");
            synchronizing = false;
        }
        refreshRows.Add(SyncRow); SyncRow();
        slider.ValueChanged += async (_, args) =>
        {
            if (synchronizing) return;
            var target = args.NewValue;
            var page = notificationContext; pendingWrites++;
            number.Text = Math.Round(target).ToString(CultureInfo.InvariantCulture) + channel.Unit;
            pending?.Cancel();
            var request = new CancellationTokenSource(); pending = request;
            try
            {
                await Task.Delay(150, request.Token);
                await gate.WaitAsync(request.Token);
                try
                {
                    if (version != generation || closed || request.IsCancellationRequested) return;
                    var errors = await Task.Run(() => ControlOperations.Apply(targets, target));
                    if (closed || version != generation) return;
                    if (ReferenceEquals(pending, request)) pending = null;
                    MarkProfileModified(); SynchronizeValues();
                    if (!request.IsCancellationRequested)
                        ShowStatus(errors.Count == 0 ? T($"已更新{Channel(channel)}", $"{Channel(channel)} updated") : string.Join("；", errors), errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error, page);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed && version == generation) ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
            finally
            {
                if (ReferenceEquals(pending, request)) { pending = null; if (!closed && version == generation) SyncRow(); }
                request.Dispose(); pendingWrites--;
            }
        };
        if (channel.WriteMute is not null)
        {
            var toggle = new ToggleButton { Content = new FontIcon { Glyph = "\uE74F", FontSize = 16 }, IsChecked = channel.IsMuted, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, channel.Name + T(" 静音", " mute"));
            ToolTipService.SetToolTip(toggle, T("静音 / 取消静音", "Mute / unmute"));
            refreshRows.Add(() => toggle.IsChecked = channel.IsMuted);
            toggle.Click += async (_, _) =>
            {
                var target = toggle.IsChecked == true;
                var page = notificationContext;
                toggle.IsEnabled = false;
                await gate.WaitAsync();
                try
                {
                    if (version != generation || closed) return;
                    await Task.Run(() => channel.WriteMute(target));
                    channel.IsMuted = target; MarkProfileModified();
                    if (!closed) ShowStatus(target ? T("设备已静音", "Device muted") : T("设备已取消静音", "Device unmuted"), InfoBarSeverity.Success, page);
                }
                catch (Exception ex) { if (!closed) { toggle.IsChecked = channel.IsMuted; ShowStatus(ex.Message, InfoBarSeverity.Error, page); } }
                finally { gate.Release(); toggle.IsEnabled = true; }
            };
            Grid.SetColumn(toggle, 4); grid.Children.Add(toggle);
        }
        return Card(grid);
    }
    private static Border Card(UIElement content) => new()
    {
        CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Child = content,
        Transitions = new TransitionCollection { new EntranceThemeTransition() }
    };
    private static TextBlock Empty(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8), Opacity = .8 };
    private void ShowStatus(string message, InfoBarSeverity severity, int? context = null)
    {
        if (closed || (context is int c && c != notificationContext)) return;
        Status.Message = message; Status.Severity = severity; Status.Visibility = Visibility.Visible; Status.IsOpen = true;
        statusTimer.Stop(); statusTimer.Interval = TimeSpan.FromSeconds(severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? 8 : 4); statusTimer.Start();
    }

    private void Identify_Click(object sender, RoutedEventArgs e)
    {
        CloseIdentification();
        try
        {
            foreach (var device in displayDevices.GroupBy(x => (x.Left, x.Top)).Select(x => x.First()))
            {
                var window = new Window { Title = MonitorTitle(device) };
                var label = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = device.Preference.Label, FontSize = 56, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                label.Children.Add(new TextBlock { Text = device.DisplayName, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
                window.Content = new Border { Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"], BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], BorderThickness = new Thickness(3), Child = label };
                if (window.AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.SetBorderAndTitleBar(false, false);
                    presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
                }
                identificationWindows.Add(window);
                window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(device.Left + (device.Width - 440) / 2, device.Top + (device.Height - 220) / 2, 440, 220));
                window.Activate();
            }
            identificationTimer.Start();
        }
        catch (Exception ex) { CloseIdentification(); ShowStatus(T("无法显示识别标记：", "Cannot show display labels: ") + ex.Message, InfoBarSeverity.Error); }
    }
    private void CloseIdentification()
    {
        identificationTimer.Stop();
        foreach (var window in identificationWindows.ToArray()) { try { window.Close(); } catch { } }
        identificationWindows.Clear();
    }
    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { Tag: string uri } && !await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)))
                ShowStatus(T("无法打开 Windows 设置。", "Cannot open Windows Settings."), InfoBarSeverity.Error);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async Task RunUiChecksAsync()
    {
        try
        {
            if (MonitorRows.Children.Count != 2 || CombinedRows.Children.Count != 4) throw new InvalidOperationException("Monitor controls not rendered.");
            DisplayMode.SelectedIndex = 0;
            DisplayMode.SelectedIndex = 1;
            if (CombinedRows.Visibility != Visibility.Visible) throw new InvalidOperationException("Combined controls not visible.");
            DisplayMode.SelectedIndex = 0;
            Navigation.SelectedItem = Navigation.MenuItems[1];
            OtherAudioExpander.IsExpanded = true;
            if (AudioPanel.Visibility != Visibility.Visible || AudioRows.Children.Count != 2 || OtherAudioRows.Children.Count != 1) throw new InvalidOperationException("Default audio filtering failed.");
            Navigation.SelectedItem = Navigation.MenuItems[2];
            if (MousePanel.Visibility != Visibility.Visible || MouseRows.Children.Count != 2) throw new InvalidOperationException("Mouse controls not rendered.");
            OtherAudioExpander.IsExpanded = false;
            Navigation.SelectedItem = Navigation.MenuItems[0];
            await RunFeatureChecksAsync();
            StartupLog.Write("UI smoke checks passed");
        }
        catch (Exception ex) { StartupLog.Write("UI smoke checks failed: " + ex); }
    }
}
