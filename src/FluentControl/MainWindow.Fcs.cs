using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow
{
    private string selectedFcsSection = "devices";
    private readonly DispatcherTimer fcsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool fcsTimerInitialized, fcsBusy;
    private StackPanel? fcsDevicesPanel;
    private TextBlock? fcsGatewayStatus;

    private void BuildFcsPage()
    {
        SettingsPanel.Children.Clear(); settingsSections.Clear();
        settingsNavigation = new NavigationView { PaneDisplayMode = NavigationViewPaneDisplayMode.Top, IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsPaneToggleButtonVisible = false, Height = 56 };
        // FCS uses a dedicated screen but shares the established USB setup and subscription workflows.
        var contents = new Grid();
        settingsScroll = new ScrollViewer { Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        SettingsPanel.Children.Add(settingsNavigation); Grid.SetRow(settingsScroll, 1); SettingsPanel.Children.Add(settingsScroll);
        void Section(string id, string title, Action<StackPanel> create)
        {
            var body = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
            body.Children.Add(new TextBlock { Text = title, FontSize = 20 });
            settingsSections[id] = body; contents.Children.Add(body);
            settingsNavigation.MenuItems.Add(new NavigationViewItem { Tag = id, Content = title });
            create(body);
        }
        Section("devices", T("设备与接入", "Devices & setup"), body =>
        {
            BuildFcsGatewayPanel(body);
            BuildSensorOnboarding(body);
            BuildLightDeviceSettings(body);
        });
        Section("plugins", T("插件管理", "Plugins"), body =>
        {
            body.Children.Add(new TextBlock { Text = T("设备端代码插件仅允许 USB 安装。插件目录、依赖检查、升级回滚和安全模式正在规划中；本版本没有执行代码插件安装。", "Device-side executable plugins require USB. Plugin installation, dependency checking, rollback and safe mode are reserved for later; this build does not install code plugins."), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new HyperlinkButton { Content = T("查看架构规划", "Architecture plan"), NavigateUri = new Uri("https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/fcs-platform-plan.md") });
        });
        Section("rules", T("自动化规则", "Automation rules"), body =>
        {
            body.Children.Add(new TextBlock { Text = T("设备端规则引擎与事件动作尚未启用。目前仅支持 FC 的光照/时间自动亮度规则。", "The device-side rule engine and event actions are not enabled yet. FC's light/time-based brightness rules remain available."), TextWrapping = TextWrapping.Wrap });
            var preset = new Button { Content = T("前往自动亮度", "Open automatic brightness") };
            preset.Click += (_, _) => OpenAdaptivePage("presets"); body.Children.Add(preset);
        });
        Section("events", T("事件与日志", "Events & logs"), body =>
        {
            body.Children.Add(new TextBlock { Text = T("网关目前只在内存中保存发现和最新光照值；事件订阅、持久日志和云端同步尚未实现。", "The gateway currently holds discovery and recent light readings only in memory. Event subscriptions, persistent logs and cloud sync are not implemented."), TextWrapping = TextWrapping.Wrap });
        });
        settingsNavigation.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is not NavigationViewItem { Tag: string id }) return;
            selectedFcsSection = id;
            foreach (var section in settingsSections) section.Value.Visibility = section.Key == id ? Visibility.Visible : Visibility.Collapsed;
            settingsScroll.ChangeView(null, 0, null, true);
        };
        if (!fcsTimerInitialized)
        {
            fcsTimerInitialized = true;
            fcsTimer.Tick += async (_, _) =>
            {
                if (closed || (Navigation.SelectedItem as NavigationViewItem)?.Tag as string != "fcs" || selectedFcsSection != "devices") return;
                await RefreshFcsDevicesAsync(false);
            };
        }
        if (!uiTest) fcsTimer.Start();
        SelectSettingsSection(selectedFcsSection);
        if (!uiTest && selectedFcsSection == "devices") _ = RefreshFcsDevicesAsync(false);
    }

    private void BuildFcsGatewayPanel(StackPanel section)
    {
        section.Children.Add(new TextBlock { Text = T("FCS Gateway · 本机设备发现", "FCS Gateway · local device discovery"),
            FontSize = 17, TextWrapping = TextWrapping.Wrap });
        fcsGatewayStatus = new TextBlock { Text = T("正在读取网关状态…", "Reading gateway status…"), TextWrapping = TextWrapping.Wrap };
        section.Children.Add(fcsGatewayStatus);
        var refresh = new Button { Content = T("扫描 USB 与局域网", "Scan USB and LAN"), IsEnabled = !uiTest };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try { await RefreshFcsDevicesAsync(true); }
            finally { if (!closed) refresh.IsEnabled = true; }
        };
        section.Children.Add(refresh);
        fcsDevicesPanel = new StackPanel { Spacing = 8 };
        section.Children.Add(fcsDevicesPanel);
        section.Children.Add(new TextBlock { Text = T("首次配网：C3 接普通 USB 电源后，连接设备发出的 FCS-xxxxxx 热点，使用设备标签密码，在浏览器打开 http://192.168.4.1 配置 2.4GHz Wi-Fi。通过 USB 数据线也能使用下面的 Wi-Fi 向导及恢复出厂。", "First-time setup: power the C3, join its FCS-xxxxxx hotspot with the device-label password, then open http://192.168.4.1 to configure 2.4 GHz Wi-Fi. USB setup and factory reset remain available below."), TextWrapping = TextWrapping.Wrap });
    }

    private async Task RefreshFcsDevicesAsync(bool discover)
    {
        if (closed || uiTest || fcsBusy || fcsDevicesPanel is null || fcsGatewayStatus is null) return;
        var panel = fcsDevicesPanel; var status = fcsGatewayStatus;
        fcsBusy = true;
        if (discover) status.Text = T("正在扫描…", "Scanning…");
        try
        {
            FcsGatewayClient.EnsureStarted();
            if (discover)
            {
                using var scan = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token);
                scan.CancelAfter(TimeSpan.FromSeconds(30));
                await FcsGatewayClient.DiscoverUsbAsync("AUTO", scan.Token);
            }
            var devices = await FcsGatewayClient.ListAsync(discover);
            if (closed || !ReferenceEquals(panel, fcsDevicesPanel) || !ReferenceEquals(status, fcsGatewayStatus)) return;
            status.Text = F("网关已连接 · 已发现 {0} 台设备", "Gateway connected · {0} discovered devices", devices.Count);
            panel.Children.Clear();
            var saved = state.Settings.Adaptive.LightSources;
            foreach (var device in devices)
            {
                var existing = saved.FirstOrDefault(s => s.DeviceId == device.DeviceId);
                var online = device.Status == "online";
                var row = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 4) };
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                heading.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(online ? Microsoft.UI.Colors.MediumSeaGreen : Microsoft.UI.Colors.Gray),
                    VerticalAlignment = VerticalAlignment.Center });
                heading.Children.Add(new TextBlock { Text = existing?.Name ?? device.DeviceId, FontSize = 15 });
                heading.Children.Add(new TextBlock { Text = (existing is null ? T("可添加", "Available") : T("已添加", "Added")) +
                    " · " + (online ? T("在线", "Online") :
                        device.UsbPort is not null ? T("USB 已发现，待读数", "USB discovered · awaiting reading") :
                        T("离线", "Offline")) });
                row.Children.Add(heading);
                var routes = new List<string>();
                if (!string.IsNullOrEmpty(device.UsbPort)) routes.Add("USB " + device.UsbPort);
                if (!string.IsNullOrEmpty(device.Ip)) routes.Add("LAN " + device.Ip);
                var detail = string.Join(" · ", routes) +
                    (online && device.Lux is double lx ? $" · {lx:0} lux" : "") +
                    (device.LastSampleUtc is { } timestamp ? $" · {timestamp.ToLocalTime():HH:mm:ss}" : "");
                row.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = .65 });
                if (existing is null && (online || !string.IsNullOrEmpty(device.UsbPort)))
                {
                    var add = new Button { Content = T("添加光照来源", "Add light source") };
                    add.Click += async (_, _) =>
                    {
                        if (state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == device.DeviceId)) return;
                        add.IsEnabled = false;
                        try
                        {
                            var useUsb = !string.IsNullOrEmpty(device.UsbPort);
                            var source = new LightSourceSubscription { Name = device.DeviceId, DeviceId = device.DeviceId,
                                Url = useUsb ? "fc-usb://auto" : "http://" + device.Ip + ":8766/v1/sample",
                                SignedDevice = false };
                            // USB candidates must return a fresh, identity-matched reading before adoption.
                            if (useUsb) await FcsGatewayClient.ReadAutoAsync(source, sensorLifetime.Token);
                            if (closed || state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == device.DeviceId)) return;
                            AddSensorSubscription(source);
                            BuildFcsPage();
                        }
                        catch (Exception)
                        {
                            if (!closed) ShowStatus(T("无法验证设备光照，请检查连接并重试。", "Could not verify the sensor reading. Check the connection and retry."), InfoBarSeverity.Warning);
                        }
                        finally { if (!closed) add.IsEnabled = true; }
                    };
                    row.Children.Add(add);
                }
                panel.Children.Add(row);
            }
            // Keep saved offline devices visible rather than making them disappear when power moves to a charger.
            foreach (var missing in saved.Where(s => !string.IsNullOrWhiteSpace(s.DeviceId) && devices.All(d => d.DeviceId != s.DeviceId)))
            {
                var offline = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                heading.Children.Add(new Ellipse { Width = 9, Height = 9,
                    Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray), VerticalAlignment = VerticalAlignment.Center });
                heading.Children.Add(new TextBlock { Text = missing.Name, FontSize = 15 });
                heading.Children.Add(new TextBlock { Text = T("已添加 · 未发现", "Added · not discovered") });
                offline.Children.Add(heading);
                offline.Children.Add(new TextBlock { Text = missing.DeviceId + " · " +
                    (LocalLightProtocol.IsLocal(missing.Url) ? T("USB 来源 · 查看 USB 连接", "USB source · check USB connection") :
                    T("局域网离线，最后已保存的地址：", "LAN offline, last saved address: ") + missing.Url),
                    Opacity = .65, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(offline);
            }
            if (devices.Count == 0 && saved.Count == 0)
                panel.Children.Add(new TextBlock { Text = T("尚未发现局域网设备。纯供电的 C3 配好 Wi-Fi 后再扫描。", "No LAN device discovered yet. Configure Wi-Fi on the independently powered C3, then scan again."), TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception)
        {
            if (!closed && ReferenceEquals(status, fcsGatewayStatus))
                status.Text = T("FCS Gateway 未连接；检查随 FC 安装的网关程序，或重试。", "FCS Gateway is unavailable. Check the bundled gateway process and retry.");
        }
        finally { fcsBusy = false; }
    }
}
