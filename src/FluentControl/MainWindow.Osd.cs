using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private Window? monitorOsd;
    private void CloseMonitorOsd() { monitorOsd?.Close(); monitorOsd = null; }
    private void ShowMonitorOsd(MonitorDevice device)
    {
        if (closed || refreshing || monitorAdaptationDialog is not null || !displayDevices.Contains(device)) return;
        CloseMonitorOsd();
        var version = generation;
        var window = new Window { Title = MonitorTitle(device) + " · " + T("FC 屏幕菜单", "FC on-screen menu") };
        monitorOsd = window;
        var lifetime = new CancellationTokenSource();
        var syncs = new List<Action>();
        var root = new Grid { RequestedTheme = Root.ActualTheme, Padding = new Thickness(16), RowSpacing = 10 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Background = new SolidColorBrush(Root.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = MonitorTitle(device) + " · " + device.Model, FontSize = 20, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(Empty(T("FC 软件菜单，控制当前屏幕。Esc 关闭。", "FC software menu for this display. Esc to close.")));
        root.Children.Add(heading);
        var body = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 3, IsTextSelectionEnabled = true };
        Grid.SetRow(status, 2); root.Children.Add(status);
        bool Active() => !closed && !lifetime.IsCancellationRequested && generation == version && ReferenceEquals(monitorOsd, window);
        async Task Apply(ControlChannel channel, double value, CancellationToken token)
        {
            pendingWrites++;
            try
            {
                if (channel.Options is null) await Task.Delay(150, token);
                if (!Active() || token.IsCancellationRequested) return;
                if (channel.RequiresConfirmation && !await ConfirmMonitorChangeAsync(channel, new[] { channel }, root.XamlRoot)) return;
                await gate.WaitAsync(token);
                try
                {
                    if (!Active() || token.IsCancellationRequested) return;
                    var errors = await Task.Run(() => ControlOperations.Apply(new[] { channel }, value));
                    if (closed || version != generation) return;
                    MarkProfileModified(); SynchronizeValues();
                    if (Active()) status.Text = errors.Count == 0 ? F("已更新{0}", "{0} updated", Channel(channel)) : string.Join("; ", errors);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Active()) status.Text = ex.Message; }
            finally { pendingWrites--; if (Active()) foreach (var sync in syncs) sync(); }
        }
        foreach (var category in VcpCatalog.All.Select(c => c.Category).Distinct())
        {
            var channels = device.Channels.Where(c => !c.CompatibilityOnly && !c.IsAction && (VcpCatalog.Find(c.PropertyKey)?.Category ?? "color") == category).ToArray();
            if (channels.Length == 0 && category != "osd") continue;
            var rows = new StackPanel { Spacing = 10 };
            if (category == "osd")
            {
                rows.Children.Add(Empty(device.InstalledAdapter?.NativeMenu.Count > 0 ?
                    T("已安装原厂菜单适配；在主窗口的型号扩展中操作。", "Native menu adapter installed. Use Model extensions in the main window.") :
                    T("菜单开关只控制原厂 OSD 是否可用，不会弹出菜单。可在型号适配中查找原厂菜单适配包。", "The OSD switch enables the native menu; it does not open it. Look for a native menu package in Model adapters.")));
                if (channels.Length == 0) rows.Children.Add(Empty(T("未检测到可控制的原厂 OSD 设置。", "No controllable native OSD settings detected.")));
            }
            foreach (var channel in channels)
            {
                var panel = new StackPanel { Spacing = 2, Padding = new Thickness(8) };
                AutomationProperties.SetAutomationId(panel, "osd-" + channel.PropertyKey);
                var label = new TextBlock { Text = Channel(channel), TextWrapping = TextWrapping.Wrap };
                panel.Children.Add(label);
                var syncing = false;
                if (channel.Options is { } options)
                {
                    var choice = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(choice, Channel(channel)); panel.Children.Add(choice);
                    void Sync()
                    {
                        syncing = true;
                        choice.SelectedItem = options.FirstOrDefault(o => o.Value == channel.Value);
                        choice.PlaceholderText = F("当前值 0x{0}（不可重放）", "Current 0x{0} (not replayable)", ((uint)channel.Value).ToString("X"));
                        syncing = false;
                    }
                    syncs.Add(Sync); Sync();
                    choice.SelectionChanged += async (_, _) =>
                    {
                        if (syncing || !Active() || choice.SelectedItem is not ControlOption selected) return;
                        choice.IsEnabled = false;
                        try { await Apply(channel, selected.Value, lifetime.Token); }
                        finally { if (Active()) choice.IsEnabled = true; }
                    };
                }
                else
                {
                    var slider = new Slider { Minimum = channel.Minimum, Maximum = channel.Maximum, StepFrequency = 1 };
                    AutomationProperties.SetName(slider, Channel(channel)); panel.Children.Add(slider);
                    CancellationTokenSource? pending = null;
                    void Sync()
                    {
                        if (pending is not null) return;
                        syncing = true; slider.Value = channel.Value;
                        label.Text = Channel(channel) + " · " + Math.Round(channel.Value) + channel.Unit;
                        syncing = false;
                    }
                    syncs.Add(Sync); Sync();
                    slider.ValueChanged += async (_, e) =>
                    {
                        if (syncing || !Active()) return;
                        pending?.Cancel();
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); pending = request;
                        label.Text = Channel(channel) + " · " + Math.Round(e.NewValue) + channel.Unit;
                        try { await Apply(channel, e.NewValue, request.Token); }
                        finally { if (ReferenceEquals(pending, request)) { pending = null; if (Active()) Sync(); } }
                    };
                }
                rows.Children.Add(Card(panel));
            }
            body.Children.Add(new Expander { Header = VcpCatalog.Category(category), IsExpanded = category == "picture", Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        refreshRows.AddRange(syncs);
        root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; window.Close(); } };
        window.Content = root;
        window.Closed += (_, _) =>
        {
            lifetime.Cancel(); lifetime.Dispose();
            foreach (var sync in syncs) refreshRows.Remove(sync);
            if (ReferenceEquals(monitorOsd, window)) monitorOsd = null;
        };
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsAlwaysOnTop = true; presenter.IsMaximizable = false; presenter.IsMinimizable = false; }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int dark = Root.ActualTheme == ElementTheme.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        try { window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FluentControl.ico")); } catch { }
        var width = Math.Min(580, Math.Max(320, device.Width - 40));
        var height = Math.Min(660, Math.Max(320, device.Height - 80));
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(device.Left + (device.Width - width) / 2, device.Top + (device.Height - height) / 2, width, height));
        window.Activate();
    }
}
