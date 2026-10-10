using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private string selectedSettingsSection = "general";
    private NavigationView? settingsNavigation;
    private ScrollViewer? settingsScroll;
    private readonly Dictionary<string, StackPanel> settingsSections = new();
    private void SelectSettingsSection(string id)
    {
        if (settingsNavigation is null) return;
        settingsNavigation.SelectedItem = settingsNavigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(x => (string)x.Tag == id)
            ?? settingsNavigation.MenuItems[0];
    }
    private void BuildSettings()
    {
        SettingsPanel.Children.Clear(); settingsSections.Clear();
        settingsNavigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Top, IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsPaneToggleButtonVisible = false, Height = 56
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(settingsNavigation, "settings-navigation");
        var contents = new Grid();
        settingsScroll = new ScrollViewer { Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(settingsScroll, 1);
        SettingsPanel.Children.Add(settingsNavigation); SettingsPanel.Children.Add(settingsScroll);
        StackPanel section = null!;
        void Section(string id, string title)
        {
            section = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
            settingsSections.Add(id, section); contents.Children.Add(section);
            settingsNavigation.MenuItems.Add(new NavigationViewItem { Content = title, Tag = id });
            section.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        }
        InitializationText.Text = T("正在初始化设备…", "Initializing devices…");
        InitializationDetail.Text = T("正在读取显示器信息与控制能力，请稍候。", "Reading display information and supported controls. Please wait.");
        void Toggle(string title, string description, bool initial, Action<bool> changed)
        {
            var toggle = new ToggleSwitch { IsOn = initial, OnContent = T("开", "On"), OffContent = T("关", "Off") };
            if (title == T("使用浅色文字", "Light text")) Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "setting-light-text");
            toggle.Toggled += (_, _) => changed(toggle.IsOn);
            section.Children.Add(SettingsRow(title, description, toggle));
        }
        Section("general", T("常规", "General"));
        var languageChoices = LanguageOptions();
        var languages = new ComboBox { ItemsSource = languageChoices, DisplayMemberPath = "Name", SelectedItem = languageChoices.FirstOrDefault(x => x.Code == state.Settings.Language) ?? languageChoices[0], MinWidth = 170 };
        languages.SelectionChanged += async (_, _) =>
        {
            if (languages.SelectedItem is not LanguageOption selectedLanguage) return;
            state.Settings.Language = selectedLanguage.Code;
            SetLanguage(state.Settings.Language); SaveState(); LocalizeUi(); BuildSettings(); await RefreshAsync();
        };
        section.Children.Add(SettingsRow(T("语言", "Language"), T("标题、导航、托盘和提示统一切换", "Updates titles, navigation, tray menus and messages"), languages));
        Toggle(T("关闭窗口后留在托盘", "Keep running in the tray"), T("从托盘菜单可彻底退出", "Use the tray menu to exit completely"), state.Settings.CloseToTray, value => { state.Settings.CloseToTray = value; SaveState(); });
        bool startup;
        try { startup = !uiTest && StartupService.IsEnabled(); } catch { startup = false; }
        Toggle(T("开机自启动", "Launch at sign-in"), T("使用当前用户的启动文件夹，登录后在托盘运行；移动程序后请重新启用", "Uses your Startup folder and starts in the tray. Re-enable after moving the app."), startup, value =>
        {
            try { if (!uiTest) StartupService.SetEnabled(value); }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); BuildSettings(); }
        });
        var startupSettings = new HyperlinkButton { Content = T("Windows 启动应用设置", "Windows Startup Apps"), Tag = "ms-settings:startupapps" };
        startupSettings.Click += OpenSettings_Click; section.Children.Add(startupSettings);
        Section("displays", T("显示器", "Displays"));
        var rescanDisplays = new Button { Content = T("重新检测", "Detect again") };
        rescanDisplays.Click += async (_, _) =>
        {
            if (refreshing) return;
            rescanDisplays.IsEnabled = false;
            try { await RefreshAsync(forceMonitorCapabilities: true); }
            finally { if (!closed) rescanDisplays.IsEnabled = true; }
        };
        section.Children.Add(SettingsRow(T("显示器功能检测", "Display capability scan"), T("通常复用能力缓存；更换连接或显示器模式后，可重新完整检测", "Normally reuses cached capabilities. Detect again after changing connections or monitor modes."), rescanDisplays));
        Section("shortcuts", T("全局快捷键", "Global shortcuts"));
        Toggle(T("启用快捷键", "Enable shortcuts"), T("若组合已被占用，会显示冲突而不抢占", "Conflicts are reported without taking over other shortcuts"), state.Settings.HotkeysEnabled, value =>
        {
            state.Settings.HotkeysEnabled = value; hotkeyProblems = shell?.ConfigureHotkeys(value && !uiTest) ?? new(); SaveState(); BuildSettings();
        });
        section.Children.Add(Empty("Ctrl + Alt + Shift + Space   —   " + T("打开面板", "Open panel") + "\nCtrl + Alt + Shift + ← / →   —   " + T("跨分组：上一个 / 下一个配置", "Across groups: previous / next profile") + "\nCtrl + Alt + Shift + ↓   —   " + T("隐藏主面板", "Hide panel")));
        if (hotkeyProblems.Count > 0) section.Children.Add(Empty(T("以下快捷键未注册：", "Unavailable shortcuts: ") + string.Join(", ", hotkeyProblems)));
        if (shell?.TrayAvailable != true) section.Children.Add(Empty(T("托盘不可用，关闭按钮将退出应用。", "Tray unavailable. Closing the window exits the app.")));
        Section("desktop", T("桌面控制面板", "Desktop controls"));
        Toggle(T("显示桌面面板", "Show desktop panel"), T("半透明背景 · 双击解锁 · 拖动右上角移动，右下角缩放", "Translucent · Double-click to unlock · Drag top-right to move, bottom-right to resize"), state.Settings.DesktopPanelEnabled, value => { state.Settings.DesktopPanelEnabled = value; SaveState(); RefreshDesktopPanel(); });
        var desktopMode = new ComboBox { ItemsSource = new[] { T("单独控制", "Individual"), T("整体控制", "Overall control") }, SelectedIndex = state.Settings.GroupDesktopMonitors ? 1 : 0, MinWidth = 150 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(desktopMode, "setting-desktop-mode");
        desktopMode.SelectionChanged += async (_, _) => { if (desktopMode.SelectedIndex >= 0) await SetDesktopMonitorModeAsync(desktopMode.SelectedIndex == 1); };
        section.Children.Add(SettingsRow(T("显示器控制", "Monitor controls"), T("不同型号也一起调节；关闭后每台屏幕单独控制", "Link different models too. Turn off to control each display separately"), desktopMode));
        Toggle(T("使用浅色文字", "Light text"), T("按桌面壁纸明暗选择文字颜色", "Choose text contrast for your wallpaper"), state.Settings.DesktopLightText, value => { state.Settings.DesktopLightText = value; SaveState(); RefreshDesktopPanel(); });
        var opacity = new Slider { Minimum = 10, Maximum = 85, StepFrequency = 5, Value = state.Settings.DesktopOpacity, Width = 170 };
        opacity.ValueChanged += (_, e) => { state.Settings.DesktopOpacity = e.NewValue; SaveState(); RefreshDesktopPanel(); };
        section.Children.Add(SettingsRow(T("背景不透明度", "Background opacity"), T("数值越低越通透，文字保持清晰", "Lower values reveal more of the desktop; text stays opaque"), opacity));
        var resetSize = new Button { Content = T("恢复自动尺寸", "Reset to automatic size") };
        resetSize.Click += (_, _) => { state.Settings.DesktopWidth = 340; state.Settings.DesktopHeight = null; SaveState(); RefreshDesktopPanel(); };
        section.Children.Add(SettingsRow(T("面板尺寸", "Panel size"), T("拖动后记住宽高；空间不足时控制行可滚动", "Remembers resized dimensions; control rows scroll when space is limited"), resetSize));
        var maximum = new NumberBox { Minimum = 1, Maximum = 16, Value = state.Settings.DesktopMaxRows, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 100 };
        maximum.ValueChanged += (_, e) => { if (double.IsNaN(e.NewValue)) return; state.Settings.DesktopMaxRows = (int)Math.Clamp(e.NewValue, 1, 16); SaveState(); RefreshDesktopPanel(); };
        section.Children.Add(SettingsRow(T("最多显示行数", "Maximum rows"), T("限制自动高度，超出的控制项可滚动查看", "Limits automatic height; scroll to see additional controls"), maximum));
        var choices = new StackPanel { Spacing = 3, Padding = new Thickness(16) };
        foreach (var row in DesktopCandidates())
        {
            var check = new CheckBox { Content = row.Name, IsChecked = state.Settings.DesktopRows.Contains(row.Key) };
            void Change()
            {
                if (check.IsChecked == true) { if (!state.Settings.DesktopRows.Contains(row.Key)) state.Settings.DesktopRows.Add(row.Key); }
                else state.Settings.DesktopRows.Remove(row.Key);
                SaveState(); RefreshDesktopPanel();
            }
            check.Checked += (_, _) => Change(); check.Unchecked += (_, _) => Change(); choices.Children.Add(check);
        }
        section.Children.Add(Card(choices));
        Section("crosshair", T("软件准星", "Software crosshair"));
        Toggle(T("显示准星", "Show crosshair"), T("用于窗口 / 无边框模式，不控制显示器自带准星", "For windowed / borderless apps; independent of the monitor's own crosshair"), state.Settings.CrosshairEnabled, value => { state.Settings.CrosshairEnabled = value; SaveState(); RefreshCrosshair(); });
        var target = new ComboBox { ItemsSource = displayDevices, DisplayMemberPath = "DisplayName", SelectedItem = displayDevices.FirstOrDefault(x => x.Id == state.Settings.CrosshairMonitorId) ?? displayDevices.FirstOrDefault(x => x.IsPrimary) ?? displayDevices.FirstOrDefault() };
        target.SelectionChanged += (_, _) => { if (target.SelectedItem is MonitorDevice d) { state.Settings.CrosshairMonitorId = d.Id; SaveState(); RefreshCrosshair(); } };
        section.Children.Add(SettingsRow(T("显示屏幕", "Target display"), "", target));
        Section("about", T("关于与更新", "About & updates"));
        BuildUpdateSettings(section);
        var exit = new Button { Content = T("退出聚合控制", "Exit Fluent Control") }; exit.Click += (_, _) => ShellCommand(6); section.Children.Add(exit);
        settingsNavigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem { Tag: string id })
            {
                selectedSettingsSection = id;
                foreach (var item in settingsSections) item.Value.Visibility = item.Key == id ? Visibility.Visible : Visibility.Collapsed;
                settingsScroll.ChangeView(null, 0, null, true);
            }
        };
        SelectSettingsSection(selectedSettingsSection);
    }
    private static Border SettingsRow(string title, string description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (description.Length > 0) text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(control); return Card(grid);
    }
}
