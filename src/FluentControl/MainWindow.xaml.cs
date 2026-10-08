using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using System.Globalization;

namespace FluentControl;
public sealed partial class MainWindow : Window
{
    private AudioService? audio;
    private MonitorService? monitors;
    private readonly SemaphoreSlim gate = new(1);
    private int generation;
    private bool refreshing;
    private bool closed;

    public MainWindow()
    {
        InitializeComponent();
        Title = "FluentControl";
        try { SystemBackdrop = new MicaBackdrop(); }
        catch (Exception ex) { StartupLog.Write("Mica unavailable: " + ex); }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 740));
        Root.Loaded += async (_, _) =>
        {
            StartupLog.Write("Main window content loaded");
            await RefreshAsync();
        };
        Closed += async (_, _) =>
        {
            closed = true;
            generation++;
            await gate.WaitAsync();
            try { audio?.Dispose(); monitors?.Dispose(); }
            finally { gate.Release(); }
        };
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (refreshing || closed) return;
        refreshing = true;
        var version = ++generation;
        AudioRows.Children.Clear();
        MonitorRows.Children.Clear();
        ShowStatus("正在读取设备…", InfoBarSeverity.Informational);
        await gate.WaitAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                audio?.Dispose(); monitors?.Dispose();
                audio = new AudioService(); monitors = new MonitorService();
                var errors = new List<string>();
                var a = new List<ControlChannel>();
                var m = new List<ControlChannel>();
                try { a = audio.Enumerate(); } catch (Exception ex) { errors.Add("音频：" + ex.Message); }
                try { m = monitors.Enumerate(); } catch (Exception ex) { errors.Add("显示器：" + ex.Message); }
                return (a, m, errors);
            });
            if (closed) return;
            foreach (var channel in result.a) AudioRows.Children.Add(CreateRow(channel, version));
            foreach (var channel in result.m) MonitorRows.Children.Add(CreateRow(channel, version));
            if (result.a.Count == 0) AudioRows.Children.Add(Empty("没有检测到可控制的音频设备。"));
            if (result.m.Count == 0) MonitorRows.Children.Add(Empty("没有读到 DDC/CI 属性。请开启显示器的 DDC/CI，再点击刷新。"));
            ShowStatus(result.errors.Count > 0 ? string.Join("；", result.errors) : "已读取设备 · 滑动调节，点击刷新可重新读取系统值。",
                result.errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { refreshing = false; gate.Release(); }
    }
    private FrameworkElement CreateRow(ControlChannel channel, int version)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(20, 16, 20, 16) };
        foreach (var width in new[] { new GridLength(32), new GridLength(230), new GridLength(1, GridUnitType.Star), new GridLength(50), new GridLength(44) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var icon = new FontIcon { Glyph = channel.Glyph, FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(icon);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = channel.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = channel.Detail, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        ToolTipService.SetToolTip(labels, channel.Name);
        Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Value = channel.Value, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, channel.Name + " " + channel.Detail);
        Grid.SetColumn(slider, 2); grid.Children.Add(slider);
        var number = new TextBlock { Text = Math.Round(channel.Value).ToString(CultureInfo.InvariantCulture) + "%", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(number, 3); grid.Children.Add(number);
        CancellationTokenSource? pending = null;
        slider.ValueChanged += async (_, args) =>
        {
            number.Text = Math.Round(args.NewValue) + "%";
            pending?.Cancel();
            var request = new CancellationTokenSource(); pending = request;
            try
            {
                await Task.Delay(150, request.Token);
                await gate.WaitAsync(request.Token);
                try
                {
                    if (version != generation || closed || request.IsCancellationRequested) return;
                    var target = args.NewValue;
                    await Task.Run(() => channel.Write(target));
                    channel.Value = target;
                    if (!closed && version == generation) ShowStatus("已发送调节指令 · " + channel.Detail, InfoBarSeverity.Success);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!closed && version == generation)
                {
                    // Keep the requested thumb position; show failure rather than claiming success.
                    ShowStatus(ex.Message + " 点击刷新重新读取设备。", InfoBarSeverity.Error);
                }
            }
            finally { if (ReferenceEquals(pending, request)) pending = null; request.Dispose(); }
        };
        if (channel.ReadMute is not null && channel.WriteMute is not null)
        {
            var toggle = new ToggleButton { Content = new FontIcon { Glyph = "\uE74F", FontSize = 16 }, IsChecked = channel.ReadMute(), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, channel.Name + " 静音");
            ToolTipService.SetToolTip(toggle, "静音 / 取消静音");
            toggle.Click += async (_, _) =>
            {
                var target = toggle.IsChecked == true;
                toggle.IsEnabled = false;
                await gate.WaitAsync();
                try
                {
                    if (version != generation || closed) return;
                    await Task.Run(() => channel.WriteMute(target));
                    if (!closed) ShowStatus(target ? "设备已静音" : "设备已取消静音", InfoBarSeverity.Success);
                }
                catch (Exception ex) { if (!closed) { toggle.IsChecked = !target; ShowStatus(ex.Message, InfoBarSeverity.Error); } }
                finally { gate.Release(); toggle.IsEnabled = true; }
            };
            Grid.SetColumn(toggle, 4); grid.Children.Add(toggle);
        }
        var border = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Child = grid
        };
        border.Transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection { new Microsoft.UI.Xaml.Media.Animation.EntranceThemeTransition() };
        return border;
    }
    private static TextBlock Empty(string message) => new() { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
    private void ShowStatus(string message, InfoBarSeverity severity) { Status.Message = message; Status.Severity = severity; }
}
