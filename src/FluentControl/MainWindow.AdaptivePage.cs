using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private string selectedAdaptiveSection = "presets";
    private void OpenAdaptivePage(string section = "presets")
    {
        selectedAdaptiveSection = section;
        var item = Navigation.MenuItems.OfType<NavigationViewItem>().Single(x => (string)x.Tag == "adaptive");
        if (ReferenceEquals(Navigation.SelectedItem, item)) BuildSettings(); else Navigation.SelectedItem = item;
    }
    private void BuildAdaptivePage()
    {
        adaptiveCurveLiveUpdate = null;
        SettingsPanel.Children.Clear(); settingsSections.Clear();
        settingsNavigation = new NavigationView { PaneDisplayMode = NavigationViewPaneDisplayMode.Top, IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsPaneToggleButtonVisible = false, Height = 56 };
        AutomationProperties.SetAutomationId(settingsNavigation, "adaptive-navigation");
        var contents = new Grid();
        settingsScroll = new ScrollViewer { Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(settingsScroll, 1); SettingsPanel.Children.Add(settingsNavigation); SettingsPanel.Children.Add(settingsScroll);
        void Section(string id, string title, Action<StackPanel> build)
        {
            var body = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
            body.Children.Add(new TextBlock { Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap });
            settingsSections[id] = body; contents.Children.Add(body);
            settingsNavigation.MenuItems.Add(new NavigationViewItem { Content = title, Tag = id }); build(body);
        }
        Section("presets", T("预设与调节", "Presets & adjustment"), BuildAdaptiveSettings);
        Section("devices", T("订阅 FCS 设备", "Subscribe to FCS devices"), BuildLightDeviceSettings);
        Section("history", T("历史记录", "History"), BuildSensorHistoryPage);
        settingsNavigation.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is not NavigationViewItem { Tag: string id }) return;
            selectedAdaptiveSection = id;
            foreach (var item in settingsSections) item.Value.Visibility = item.Key == id ? Visibility.Visible : Visibility.Collapsed;
            settingsScroll.ChangeView(null, 0, null, true);
        };
        SelectSettingsSection(selectedAdaptiveSection);
    }
    private void BuildSensorOnboarding(StackPanel section)
    {
        section.Children.Add(new TextBlock { Text = T("已有设备：连接 USB，发现并订阅；无线设备可使用 Wi-Fi 配网向导。确认读数后，再到预设页启用调节。", "Have a device? Connect USB and discover it; use the Wi-Fi wizard for wireless devices. Check readings before enabling adjustment on the presets page."), TextWrapping = TextWrapping.Wrap });
        var guide = new Expander { Header = T("还没有设备？选购与兼容说明", "Need a device? Buying & compatibility guide"), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(guide, "sensor-buying-guide");
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = T("无需设备也能使用定时调节，或先用内置模拟光照体验。光照调节需要兼容传感器及对应固件；购买裸板后还需组装和烧录。", "Schedules work without a sensor; try built-in simulated light first. Light-based adjustment needs a compatible sensor and firmware. Bare boards require assembly and flashing."), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = T("ESP32-C3 + BH1750：USB / Wi-Fi 方案。RP2040 + BH1750：USB 有线方案。CH552T + BH1750：USB 测试方案，需要专用 C 固件，不能使用 MicroPython。", "ESP32-C3 + BH1750: USB / Wi-Fi. RP2040 + BH1750: wired USB. CH552T + BH1750: experimental USB, requiring dedicated C firmware, not MicroPython."), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = T("选购前核对具体板型、电压、接线和固件支持；名称相同不代表即插即用。兼容资料中的验证状态为准。", "Before buying, check the exact board, voltage, wiring and firmware support. Matching names do not guarantee plug-and-play operation; consult the documented validation status."), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new HyperlinkButton { Content = T("查看硬件与接入指南", "View hardware and setup guide"), NavigateUri = new Uri("https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/device-signals.md") });
        guide.Content = body; section.Children.Add(guide);
    }
    private void BuildSensorHistoryPage(StackPanel section)
    {
        var sources = state.Settings.Adaptive.LightSources;
        if (sources.Count == 0)
        {
            section.Children.Add(new TextBlock { Text = T("添加光照来源后可查看历史记录。", "Add a light source to view its history."), TextWrapping = TextWrapping.Wrap });
            var connect = new Button { Content = T("管理与接入设备", "Manage and connect devices") };
            connect.Click += (_, _) => OpenAdaptivePage("devices"); section.Children.Add(connect); return;
        }
        var picker = new ComboBox { Header = T("光照数据订阅", "Light data subscription"), ItemsSource = sources,
            SelectedItem = sources.FirstOrDefault(x => x.Id == state.Settings.Adaptive.ActiveSourceId) ?? sources[0], MinWidth = 240 };
        section.Children.Add(picker);
        var history = new Button { Content = T("查看光照历史", "View light history") };
        history.Click += async (_, _) => { if (picker.SelectedItem is LightSourceSubscription source) await ShowLightHistoryAsync(source); };
        section.Children.Add(history);
        section.Children.Add(new TextBlock { Text = T("查看历史不会切换正在使用的光照来源。", "Viewing history does not switch the active light source."), TextWrapping = TextWrapping.Wrap });
    }
}
