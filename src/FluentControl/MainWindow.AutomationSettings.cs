using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;
public sealed partial class MainWindow
{
    private void BuildAutomationSettings(StackPanel section)
    {
        var s = state.Settings.Adaptive;
        TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        var enable = new ToggleSwitch { Header = T("启用自动调节", "Enable automatic adjustment"), IsOn = s.Enabled };
        enable.Toggled += (_, _) => { s.Enabled = enable.IsOn; Interlocked.Exchange(ref manualBrightnessHold, 0); SaveAdaptiveSettings(); };
        section.Children.Add(enable);
        section.Children.Add(Text(T("订阅光照数据，或按时间/日出日落调节。首次启用会实际调整选中的屏幕；模拟来源也会生效。", "Subscribe to light data or schedule by time/sunrise. Enabling adjusts the selected display, including when using simulated data.")));
        var mode = new ComboBox { Header = T("调节依据", "Adjustment basis"), MinWidth = 240,
            ItemsSource = new[] { T("光照强度", "Illuminance"), T("指定时间", "Fixed times"), T("日出日落", "Sunrise and sunset") }, SelectedIndex = s.Mode switch { "clock" => 1, "solar" => 2, _ => 0 } };
        mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex < 0) return; s.Mode = mode.SelectedIndex switch { 1 => "clock", 2 => "solar", _ => "ambient" }; automaticTheme = null; ApplyAppTheme(); SaveAdaptiveSettings(); BuildSettings(); };
        section.Children.Add(mode);
        var target = new ComboBox { Header = T("目标显示器", "Target display"), ItemsSource = displayDevices, DisplayMemberPath = "DisplayName",
            SelectedItem = displayDevices.FirstOrDefault(x => x.Id == s.TargetDisplayId), MinWidth = 240 };
        target.SelectionChanged += (_, _) => { if (target.SelectedItem is MonitorDevice d) { s.TargetDisplayId = d.Id; SaveAdaptiveSettings(); } };
        section.Children.Add(target);
        adaptiveLiveStatus = Text(s.Enabled ? T("等待数据…", "Waiting for data…") : T("自动调节已关闭", "Automatic adjustment is off")); section.Children.Add(adaptiveLiveStatus);
        section.Children.Add(Text(T("默认 1 秒采样，3 点中值与平滑；屏幕最多每 2 秒写一次，每次最多 5%，差异不足 2% 不写。手动调整后保持 10 分钟。", "Default: 1 s sampling, 3-sample median and smoothing; display writes at most every 2 s, up to 5% per step, with a 2% deadband. Manual changes hold for 10 minutes.")));
        void Number(string title, double value, double min, double max, Action<double> changed)
        {
            var box = new NumberBox { Header = title, Value = value, Minimum = min, Maximum = max, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            box.ValueChanged += (_, e) => { if (!double.IsFinite(e.NewValue)) return; changed(Math.Clamp(e.NewValue, min, max)); SaveAdaptiveSettings(); };
            if (s.Mode == "solar") box.LostFocus += (_, _) => BuildSettings();
            section.Children.Add(box);
        }
        if (s.Mode != "ambient")
        {
            var zone = new ComboBox { Header = T("时区", "Time zone"), ItemsSource = TimeZoneInfo.GetSystemTimeZones(), DisplayMemberPath = "DisplayName", MaxWidth = 520,
                SelectedItem = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(x => x.Id == s.TimeZoneId) };
            zone.SelectionChanged += (_, _) => { if (zone.SelectedItem is TimeZoneInfo z) { s.TimeZoneId = z.Id; SaveAdaptiveSettings(); BuildSettings(); } };
            section.Children.Add(zone);
            var theme = new ToggleSwitch { Header = T("同时切换 FC 浅色/深色主题", "Also switch FC light/dark theme"), IsOn = s.ThemeEnabled };
            theme.Toggled += (_, _) => { s.ThemeEnabled = theme.IsOn; SaveAdaptiveSettings(); }; section.Children.Add(theme);
            if (s.Mode == "clock")
            {
                Number(T("浅色开始（小时）", "Light theme starts (hour)"), s.LightThemeHour, 0, 24, x => s.LightThemeHour = x);
                Number(T("深色开始（小时）", "Dark theme starts (hour)"), s.DarkThemeHour, 0, 24, x => s.DarkThemeHour = x);
            }
            else
            {
                var city = new ComboBox { Header = T("地区快捷填入", "Region shortcut"), ItemsSource = new[] { "武汉 / Wuhan", "北京 / Beijing", "上海 / Shanghai", "深圳 / Shenzhen", "London", "New York", "Sydney" }, PlaceholderText = T("自定义经纬度", "Custom coordinates"), MinWidth = 240 };
                city.SelectionChanged += (_, _) =>
                {
                    if (city.SelectedIndex < 0) return;
                    var places = new[] { (30.5928,114.3055,"China Standard Time"), (39.9042,116.4074,"China Standard Time"), (31.2304,121.4737,"China Standard Time"), (22.5431,114.0579,"China Standard Time"), (51.5074,-.1278,"GMT Standard Time"), (40.7128,-74.0060,"Eastern Standard Time"), (-33.8688,151.2093,"AUS Eastern Standard Time") };
                    var place = places[city.SelectedIndex]; s.Latitude = place.Item1; s.Longitude = place.Item2; s.TimeZoneId = place.Item3; SaveAdaptiveSettings(); BuildSettings();
                };
                section.Children.Add(city);
                Number(T("纬度（北纬为正）", "Latitude (north positive)"), s.Latitude, -90, 90, x => s.Latitude = x);
                Number(T("经度（东经为正）", "Longitude (east positive)"), s.Longitude, -180, 180, x => s.Longitude = x);
                Number(T("日间亮度 %", "Day brightness %"), s.DayBrightness, 0, 100, x => s.DayBrightness = x);
                Number(T("夜间亮度 %", "Night brightness %"), s.NightBrightness, 0, 100, x => s.NightBrightness = x);
                var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId));
                var sun = SolarSchedule.Calculate(local.Date, s.Latitude, s.Longitude);
                section.Children.Add(Text(sun.Sunrise is { } rise && sun.Sunset is { } set
                    ? $"☀ {TimeZoneInfo.ConvertTime(rise, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId)):HH:mm} / ☾ {TimeZoneInfo.ConvertTime(set, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId)):HH:mm}"
                    : sun.PolarDay ? T("极昼", "Polar day") : T("极夜", "Polar night")));
                section.Children.Add(Text(T("按所选地区和时区本地估算，不上传位置。主题仅影响 FC，不修改 Windows 系统主题。", "Estimated locally for the selected coordinates and time zone; location is not uploaded. Theme changes affect FC only, not Windows.")));
            }
        }
        else
        {
            var sources = new List<LightSourceSubscription> { new() { Id = "simulation", Name = T("内置模拟光照", "Built-in simulated light") } }; sources.AddRange(s.LightSources);
            var picker = new ComboBox { Header = T("光照数据订阅", "Light data subscription"), ItemsSource = sources, SelectedItem = sources.FirstOrDefault(x => x.Id == s.ActiveSourceId), MinWidth = 240 };
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is LightSourceSubscription source) { s.ActiveSourceId = source.Id; SaveAdaptiveSettings(); BuildSettings(); } }; section.Children.Add(picker);
            var localPort = new TextBox { Header = T("本地传感器端口", "Local sensor port"), Text = "auto", PlaceholderText = "auto / COM5", MaxLength = 16 };
            section.Children.Add(localPort);
            var discover = new Button { Content = T("发现并订阅本地传感器", "Discover and subscribe to local sensors"), IsEnabled = !uiTest && s.LightSources.Count < 32 };
            discover.Click += async (_, _) =>
            {
                discover.IsEnabled = false;
                var revision = adaptiveRevision;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                localDiscovery?.Cancel(); localDiscovery = cancellation;
                try
                {
                    var devices = await LocalLightSensor.DiscoverAsync(localPort.Text.Trim(), cancellation.Token);
                    if (closed || revision != adaptiveRevision) return;
                    if (devices.Count == 0) { ShowStatus(T("未发现兼容传感器，请关闭 Thonny 并检查主板程序和端口。", "No compatible sensor found. Close Thonny and check firmware and port."), InfoBarSeverity.Warning); return; }
                    string? first = null;
                    foreach (var device in devices)
                    {
                        var item = s.LightSources.FirstOrDefault(x => x.DeviceId == device.Identity);
                        if (item is null && s.LightSources.Count < 32)
                        {
                            item = new LightSourceSubscription { Name = device.Identity, DeviceId = device.Identity,
                                Url = "fc-usb://" + (localPort.Text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ? "auto" : device.Port.ToLowerInvariant()) };
                            s.LightSources.Add(item);
                        }
                        if (item is not null)
                        {
                            item.Url = "fc-usb://" + (localPort.Text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ? "auto" : device.Port.ToLowerInvariant());
                            item.SignedDevice = false;
                        }
                        first ??= item?.Id;
                    }
                    if (first is not null) { s.ActiveSourceId = first; s.RecordLightHistory = true; s.Enabled = false; SaveAdaptiveSettings(); BuildSettings(); }
                }
                catch (OperationCanceledException) { }
                catch { if (!closed) ShowStatus(T("未发现兼容传感器，请关闭 Thonny 并检查主板程序和端口。", "No compatible sensor found. Close Thonny and check firmware and port."), InfoBarSeverity.Warning); }
                finally { if (ReferenceEquals(localDiscovery, cancellation)) localDiscovery = null; if (!closed) discover.IsEnabled = s.LightSources.Count < 32; }
            };
            section.Children.Add(discover);
            BuildSensorDeviceSettings(section, localPort);
            var name = new TextBox { Header = T("名称", "Name"), MaxLength = 100 };
            var url = new TextBox { Header = T("数据链接（HTTP / HTTPS）", "Data URL (HTTP / HTTPS)"), PlaceholderText = "http://127.0.0.1:8765/light.json", MaxLength = 2048 };
            section.Children.Add(name); section.Children.Add(url);
            var add = new Button { Content = T("添加光照来源", "Add light source"), IsEnabled = s.LightSources.Count < 32 };
            add.Click += (_, _) =>
            {
                try
                {
                    var uri = LightFeed.UriFor(url.Text.Trim());
                    if (s.LightSources.Any(x => x.Url == uri.AbsoluteUri) || string.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException();
                    var item = new LightSourceSubscription { Name = name.Text.Trim(), Url = uri.AbsoluteUri };
                    s.LightSources.Add(item); s.ActiveSourceId = item.Id; s.RecordLightHistory = true; s.Enabled = false; SaveAdaptiveSettings(); BuildSettings();
                }
                catch { ShowStatus(T("请输入名称和有效且不重复的 HTTP/HTTPS 数据链接。", "Enter a name and a valid, unique HTTP/HTTPS data URL."), InfoBarSeverity.Error); }
            }; section.Children.Add(add);
            if (s.LightSources.FirstOrDefault(x => x.Id == s.ActiveSourceId) is { } selected)
            {
                section.Children.Add(Text(selected.Url));
                BuildLightSamplingInterval(section, selected);
                var record = new ToggleSwitch { Header = T("保存光照历史（关闭自动调节时仍采集）", "Save light history (collect even when adjustment is off)"), IsOn = s.RecordLightHistory };
                record.Toggled += (_, _) => { s.RecordLightHistory = record.IsOn; SaveAdaptiveSettings(); };
                section.Children.Add(record);
                var history = new Button { Content = T("查看光照历史", "View light history") };
                history.Click += async (_, _) => await ShowLightHistoryAsync(selected);
                section.Children.Add(history);
                var remove = new Button { Content = T("删除", "Delete") };
                remove.Click += (_, _) => { s.LightSources.Remove(selected); s.ActiveSourceId = "simulation"; s.Enabled = false; SaveAdaptiveSettings(); BuildSettings(); }; section.Children.Add(remove);
            }
        }
    }

    private void BuildLightSamplingInterval(StackPanel section, LightSourceSubscription source)
    {
        bool updating = false;
        var input = new NumberBox { Header = T("采样间隔（秒）", "Sampling interval (seconds)"),
            Value = source.PollMilliseconds / 1000d, Minimum = .2, Maximum = 86400,
            SmallChange = .1, LargeChange = 1, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = .1,
            Value = 100 * Math.Log(source.PollMilliseconds / 200d) / Math.Log(432000), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Stretch };
        var intervals = new[] { 200, 1000, 5000, 10000, 60000, 600000 };
        var presets = new ComboBox { Header = T("间隔预设", "Interval presets"), MinWidth = 240,
            ItemsSource = new[] { T("最快（约 0.2 秒）", "Fastest (about 0.2 s)"), "1 s", "5 s", "10 s", "1 min", "10 min" },
            SelectedIndex = Array.IndexOf(intervals, source.PollMilliseconds) };
        void Apply(double milliseconds)
        {
            if (updating || !double.IsFinite(milliseconds)) return;
            updating = true;
            try
            {
                source.PollMilliseconds = (int)Math.Clamp(Math.Round(milliseconds), LightFeed.MinimumPollMilliseconds, LightFeed.MaximumPollMilliseconds);
                input.Value = source.PollMilliseconds / 1000d;
                slider.Value = 100 * Math.Log(source.PollMilliseconds / 200d) / Math.Log(432000);
                presets.SelectedIndex = Array.IndexOf(intervals, source.PollMilliseconds);
                SaveAdaptiveSettings();
            }
            finally { updating = false; }
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "light-sampling-seconds");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(slider, "light-sampling-slider");
        input.ValueChanged += (_, e) => Apply(e.NewValue * 1000);
        slider.ValueChanged += (_, e) => Apply(200 * Math.Pow(432000, e.NewValue / 100));
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex >= 0) Apply(intervals[presets.SelectedIndex]); };
        section.Children.Add(presets); section.Children.Add(input); section.Children.Add(slider);
    }

    private async Task ShowLightHistoryAsync(LightSourceSubscription source)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = T("仅保存在本机，所有来源合计最多 64 MiB，满后删除最早记录。断线不补造数据。", "Local only, up to 64 MiB across all sources. Oldest records are removed when full. Gaps are not filled."), TextWrapping = TextWrapping.Wrap });
        var rows = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.None };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var newer = new Button { Content = T("较新记录", "Newer records") };
        var older = new Button { Content = T("较早记录", "Older records") };
        var status = new TextBlock();
        navigation.Children.Add(newer); navigation.Children.Add(older);
        panel.Children.Add(rows); panel.Children.Add(navigation); panel.Children.Add(status);
        int page = 0;
        async Task Load()
        {
            newer.IsEnabled = older.IsEnabled = false;
            try
            {
                var entries = await lightHistory.ReadPageAsync(source.Id, page);
                rows.ItemsSource = entries.Select(x => $"{x.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}  ·  " +
                    (x.Valid ? $"{x.Lux:0.##} lux" : T("数据中断", "Data gap")) +
                    (x.Simulated ? " · " + T("模拟", "Simulated") : "")).ToArray();
                status.Text = entries.Count == 0 ? T("暂无记录", "No records yet") : $"{page + 1} · {entries.Count}";
                newer.IsEnabled = page > 0; older.IsEnabled = entries.Count == 100;
            }
            catch { status.Text = T("无法读取光照历史", "Cannot read light history"); newer.IsEnabled = page > 0; }
        }
        newer.Click += async (_, _) => { page--; await Load(); };
        older.Click += async (_, _) => { page++; await Load(); };
        var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, Title = source.Name,
            Content = panel, CloseButtonText = T("关闭", "Close"), RequestedTheme = ShellRoot.RequestedTheme };
        await Load();
        if (!closed) await dialog.ShowAsync();
    }
}
