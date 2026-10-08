using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private UserStateStore? stateStore;
    private UserState state = new();
    private ShellIntegration? shell;
    private DesktopPanelWindow? desktopPanel;
    private CrosshairWindow? crosshair;
    private List<ControlChannel> audioChannels = new(), mouseChannels = new();
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer pendingStateTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool rebuildingProfiles, applyingProfile, exitRequested, profileDirty = true;
    private int notificationContext, pendingWrites;
    private List<string> hotkeyProblems = new();

    private void InitializeFeatures()
    {
        try
        {
            stateStore = new UserStateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", uiTest ? "test-user-state.json" : "user-state.json"));
            state = stateStore.State;
        }
        catch (Exception ex) { StartupLog.Write("User state cannot be read: " + ex); }
        SetLanguage(state.Settings.Language);
        statusTimer.Tick += (_, _) => ClearStatus();
        pendingStateTimer.Tick += (_, _) => { pendingStateTimer.Stop(); SaveState(); };
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "FluentControl.ico");
            AppWindow.SetIcon(icon);
            shell = new ShellIntegration(WinRT.Interop.WindowNative.GetWindowHandle(this), icon, id => DispatcherQueue.TryEnqueue(() => ShellCommand(id)));
            hotkeyProblems = shell.ConfigureHotkeys(state.Settings.HotkeysEnabled && !uiTest);
        }
        catch (Exception ex) { StartupLog.Write("Tray unavailable: " + ex); }
        AppWindow.Closing += (_, e) =>
        {
            if (!exitRequested && state.Settings.CloseToTray && shell?.TrayAvailable == true)
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
        };
        LocalizeUi();
        RefreshProfiles();
        BuildSettings();
    }
    private void ShutdownFeatures()
    {
        statusTimer.Stop(); pendingStateTimer.Stop();
        if (desktopPanel is not null)
        {
            state.Settings.DesktopX = desktopPanel.AppWindow.Position.X;
            state.Settings.DesktopY = desktopPanel.AppWindow.Position.Y;
            SaveState();
        }
        desktopPanel?.Close(); desktopPanel = null; crosshair?.Close(); crosshair = null;
        shell?.Dispose(); shell = null;
    }
    private void ClearStatus()
    {
        statusTimer.Stop(); Status.IsOpen = false; Status.Visibility = Visibility.Collapsed;
    }
    private bool SaveState()
    {
        try
        {
            if (stateStore is null) throw new IOException(T("配置文件无法读取，为保留原文件，当前更改未保存。", "Settings could not be loaded; the original file has been preserved."));
            stateStore.Save(); return true;
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); return false; }
    }
    private bool launchFinished;
    internal bool TryStartInBackground()
    {
        if (!Environment.GetCommandLineArgs().Contains("--background") || shell?.TrayAvailable != true) return false;
        launchFinished = true; _ = RefreshAsync(); return true;
    }
    internal void FinishLaunch()
    {
        if (launchFinished) return; launchFinished = true;
        if (Environment.GetCommandLineArgs().Contains("--background") && shell?.TrayAvailable == true) AppWindow.Hide();
    }
    private async void ShellCommand(int id)
    {
        try
        {
            if (closed) return;
            switch (id)
            {
                case 1: AppWindow.Show(); Activate(); ShellIntegration.Focus(WinRT.Interop.WindowNative.GetWindowHandle(this)); StartupLog.Write("Main window activated"); break;
                case 2: await SwitchProfileAsync(-1); break;
                case 3: await SwitchProfileAsync(1); break;
                case 4: if (shell?.TrayAvailable == true) AppWindow.Hide(); break;
                case 5:
                    state.Settings.DesktopPanelEnabled = !state.Settings.DesktopPanelEnabled;
                    SaveState(); RefreshDesktopPanel(); BuildSettings(); break;
                case 6: exitRequested = true; Close(); break;
            }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private void LocalizeUi()
    {
        Navigation.PaneTitle = AppName;
        shell?.UpdateLanguage();
        ((NavigationViewItem)Navigation.MenuItems[0]).Content = T("显示器", "Displays");
        ((NavigationViewItem)Navigation.MenuItems[1]).Content = T("声音与麦克风", "Audio");
        ((NavigationViewItem)Navigation.MenuItems[2]).Content = T("鼠标与指针", "Mouse & pointer");
        if (Navigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = T("设置", "Settings");
        var index = DisplayMode.SelectedIndex;
        DisplayMode.Items[0] = T("单独控制", "Individual"); DisplayMode.Items[1] = T("一起控制", "Linked"); DisplayMode.SelectedIndex = index;
        RefreshButton.Content = T("刷新", "Refresh"); IdentifyButton.Content = T("识别显示器", "Identify");
        DefaultAudioTitle.Text = T("Windows 当前默认设备", "Current Windows default devices");
        SoundSettingsLink.Content = T("Windows 声音设置", "Windows sound settings");
        PointerSettingsLink.Content = T("Windows 指针设置", "Windows pointer settings");
        MouseDescription.Text = T("调整后立即生效。更多指针颜色与样式，可在 Windows 指针设置中选择。", "Changes apply immediately. More pointer colors and styles are available in Windows Settings.");
        SpecialFeatures.Header = T("更多显示功能", "More display features");
        SpecialFeaturesText.Text = T("功能按显示器能力与当前模式识别。HDR、预设或转接器可能限制调节；更改硬件模式后请刷新。厂商 SDK、硬件准星与 FPS 已预留，尚未接入。", "Controls depend on display capabilities and current mode. HDR, presets or adapters may restrict adjustments; refresh after changing hardware modes. Vendor SDKs, hardware crosshairs and FPS are reserved for future support.");
        HideUnavailable.Content = T("隐藏不可设置项", "Hide unavailable controls");
        HideUnavailable.IsChecked = state.Settings.HideUnavailableMonitorControls;
        ProfileExchangeButton.Content = T("分享 / 导入", "Share / import");
        NightLightLink.Content = T("Windows 夜间模式", "Windows night light");
        ProfilePicker.PlaceholderText = T("选择配置", "Choose a profile");
        SaveProfileButton.Content = T("保存为配置", "Save as profile"); UpdateProfileButton.Content = T("更新", "Update"); DeleteProfileButton.Content = T("删除", "Delete");
        ToolTipService.SetToolTip(PreviousProfileButton, T("上一个配置", "Previous profile")); ToolTipService.SetToolTip(NextProfileButton, T("下一个配置", "Next profile"));
        UpdatePageTitle();
    }
    private void UpdatePageTitle()
    {
        var tag = Navigation.SelectedItem == Navigation.SettingsItem ? "settings" : (Navigation.SelectedItem as NavigationViewItem)?.Tag as string ?? "monitors";
        PageTitle.Text = tag switch { "audio" => T("声音与麦克风", "Audio"), "mouse" => T("鼠标与指针", "Mouse & pointer"), "settings" => T("设置", "Settings"), _ => T("显示器", "Displays") };
        Title = AppName + " · " + PageTitle.Text;
        PageDescription.Text = tag switch
        {
            "audio" => T("常用设备在前，其他设备按需展开。", "Your default devices first. Expand the rest when needed."),
            "mouse" => T("找到适合自己的移动速度与指针大小。", "Tune movement speed and pointer size."),
            "settings" => T("按自己的习惯使用聚合控制。", "Make Fluent Control work your way."),
            _ => T("单独微调，或让所有屏幕一起变化。", "Tune each display, or adjust them together.")
        };
    }
    private Dictionary<string, ControlChannel> AllChannels()
    {
        var result = new Dictionary<string, ControlChannel>();
        foreach (var d in displayDevices) foreach (var c in d.Channels.Where(c => c.CanSave && !c.IsAction)) result[$"monitor/{Uri.EscapeDataString(d.Id)}/{c.PropertyKey}"] = c;
        foreach (var c in audioChannels) result[$"audio/{Uri.EscapeDataString(c.DeviceId)}/{c.PropertyKey}"] = c;
        foreach (var c in mouseChannels) result["mouse/" + c.PropertyKey] = c;
        return result;
    }
    private void RefreshProfiles()
    {
        rebuildingProfiles = true;
        ProfilePicker.ItemsSource = null; ProfilePicker.ItemsSource = state.Profiles;
        ProfilePicker.SelectedItem = state.Profiles.FirstOrDefault(x => x.Id == state.SelectedProfileId);
        UpdateProfileButton.IsEnabled = DeleteProfileButton.IsEnabled = ProfilePicker.SelectedItem is not null;
        PreviousProfileButton.IsEnabled = NextProfileButton.IsEnabled = state.Profiles.Count > 0;
        rebuildingProfiles = false;
        UpdateDesktopProfile();
    }
    private void UpdateDesktopProfile()
    {
        var name = state.Profiles.FirstOrDefault(x => x.Id == state.SelectedProfileId)?.Name;
        desktopPanel?.SetProfile(name is null ? T("未选择配置", "No profile selected") : name + (profileDirty ? " *" : ""));
    }
    private void MarkProfileModified() { profileDirty = true; UpdateDesktopProfile(); }
    private async void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!rebuildingProfiles && ProfilePicker.SelectedItem is ControlProfile profile) await ApplyProfileAsync(profile);
    }
    private async void PreviousProfile_Click(object sender, RoutedEventArgs e) => await SwitchProfileAsync(-1);
    private async void NextProfile_Click(object sender, RoutedEventArgs e) => await SwitchProfileAsync(1);
    private async Task SwitchProfileAsync(int delta)
    {
        var index = UserStateStore.NextIndex(state.Profiles, state.SelectedProfileId, delta);
        if (index < 0) { ShowStatus(T("请先保存一个配置。", "Save a profile first."), InfoBarSeverity.Informational); return; }
        await ApplyProfileAsync(state.Profiles[index]);
    }
    private async Task WaitForWritesAsync()
    {
        while ((pendingWrites > 0 || desktopPanel?.HasPendingChanges == true) && !closed) await Task.Delay(25);
    }
    private async Task ApplyProfileAsync(ControlProfile profile)
    {
        if (applyingProfile || refreshing || closed) return;
        applyingProfile = true; var page = notificationContext;
        try
        {
            await WaitForWritesAsync();
            foreach (var mapping in profile.BrightnessMappings.Values) mapping.Validate();
            if (!await ConfirmProfileInputAsync(profile)) return;
            await gate.WaitAsync();
            try
            {
                if (closed) return;
                var available = AllChannels();
                var result = await Task.Run(() =>
                {
                    var errors = new List<string>(); var missing = 0; var applied = 0;
                    foreach (var entry in profile.Values.OrderBy(x => available.TryGetValue(x.Key, out var c) ? c.ApplyOrder : 50))
                    {
                        if (!available.TryGetValue(entry.Key, out var channel)) { missing++; continue; }
                        var failures = ControlOperations.Apply(new[] { channel }, entry.Value.Value);
                        errors.AddRange(failures);
                        if (failures.Count > 0) continue;
                        applied++;
                        if (entry.Value.Muted is bool muted && channel.WriteMute is not null)
                        {
                            try { channel.WriteMute(muted); channel.IsMuted = muted; }
                            catch (Exception ex) { errors.Add(channel.Name + ": " + ex.Message); }
                        }
                    }
                    return (errors, missing, applied);
                });
                if (closed) return;
                foreach (var device in displayDevices)
                    if (profile.BrightnessMappings.TryGetValue(device.Id, out var mapping)) { mapping.Validate(); device.Preference.Brightness = mapping.Copy(); BindBrightnessMapping(device); }
                preferences?.Save();
                state.SelectedProfileId = profile.Id; profileDirty = result.errors.Count > 0 || result.missing > 0;
                SaveState(); RefreshProfiles(); SynchronizeValues();
                var message = F("配置「{0}」：已更新 {1} 项", "Profile '{0}': {1} controls applied", profile.Name, result.applied);
                if (result.missing > 0) message += F("，跳过 {0} 个未连接控制项", "; {0} unavailable controls skipped", result.missing);
                if (result.errors.Count > 0) message += " · " + string.Join("; ", result.errors);
                ShowStatus(message, result.errors.Count > 0 || result.missing > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, page);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
        finally { applyingProfile = false; }
    }
    private Dictionary<string, SavedValue> CaptureProfile() => AllChannels()
        .Where(x => !x.Key.StartsWith("audio/") || x.Value.IsDefaultAudio)
        .ToDictionary(x => x.Key, x => new SavedValue { Value = x.Value.Value, Muted = x.Value.WriteMute is null ? null : x.Value.IsMuted });
    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = T("例如：阅读、游戏、夜间", "Reading, gaming, evening…"), MaxLength = 40 };
        var dialog = new ContentDialog { Title = T("保存当前配置", "Save current controls"), Content = input, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            await WaitForWritesAsync(); await gate.WaitAsync();
            try
            {
                var name = input.Text.Trim();
                if (state.Profiles.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) { ShowStatus(T("配置名称已存在，可用“更新”覆盖。", "That name exists. Use Update to replace it."), InfoBarSeverity.Warning); return; }
                var profile = new ControlProfile { Name = name, Values = CaptureProfile(), BrightnessMappings = CaptureMappings() };
                if (profile.Values.Count == 0) { ShowStatus(T("没有可保存的控制项。", "No controls are available to save."), InfoBarSeverity.Warning); return; }
                state.Profiles.Add(profile); state.SelectedProfileId = profile.Id; profileDirty = false;
                if (SaveState()) ShowStatus(T("配置已保存。", "Profile saved."), InfoBarSeverity.Success);
                RefreshProfiles();
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async void UpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not ControlProfile profile) return;
        await WaitForWritesAsync(); await gate.WaitAsync();
        try
        {
            profile.Values = CaptureProfile(); profile.BrightnessMappings = CaptureMappings(); profileDirty = false;
            if (SaveState()) ShowStatus(T("配置已更新。", "Profile updated."), InfoBarSeverity.Success);
            RefreshProfiles();
        }
        finally { gate.Release(); }
    }
    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not ControlProfile profile) return;
        var dialog = new ContentDialog { Title = T("删除配置", "Delete profile"), Content = profile.Name, PrimaryButtonText = T("删除", "Delete"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            state.Profiles.Remove(profile); state.SelectedProfileId = null; SaveState(); RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private void SynchronizeValues()
    {
        foreach (var refresh in refreshRows) refresh(); desktopPanel?.RefreshValues();
    }
    private List<PanelRow> DesktopCandidates()
    {
        var result = new List<PanelRow>();
        if (state.Settings.GroupDesktopMonitors)
        {
            foreach (var key in new[] { "brightness", "contrast", "speaker" })
            {
                var targets = displayDevices.SelectMany(x => x.Channels).Where(x => x.PropertyKey == key).ToArray();
                if (targets.Length > 0) result.Add(new() { Key = "monitor/all/" + key, Name = Channel(targets[0]), Group = "monitor", Targets = targets, Linked = true });
            }
        }
        else foreach (var display in displayDevices) foreach (var c in display.Channels.Where(x => !x.IsAction && !x.RequiresConfirmation))
            result.Add(new() { Key = $"monitor/{Uri.EscapeDataString(display.Id)}/{c.PropertyKey}", Name = display.DisplayName + " · " + Channel(c), Group = display.Id, Targets = new[] { c } });
        foreach (var c in audioChannels.Where(x => x.IsDefaultAudio)) result.Add(new() { Key = $"audio/{Uri.EscapeDataString(c.DeviceId)}/{c.PropertyKey}", Name = c.Name, Group = "audio", Targets = new[] { c } });
        foreach (var c in mouseChannels) result.Add(new() { Key = "mouse/" + c.PropertyKey, Name = Channel(c), Group = "mouse", Targets = new[] { c } });
        return result;
    }
    private void RefreshDesktopPanel()
    {
        try
        {
            if (!state.Settings.DesktopPanelEnabled || closed) { desktopPanel?.Close(); desktopPanel = null; return; }
            desktopPanel ??= new DesktopPanelWindow(state.Settings, delta => _ = SwitchProfileAsync(delta), () =>
            {
                pendingStateTimer.Stop(); pendingStateTimer.Start();
            });
            var version = generation;
            var selected = DesktopCandidates().Where(x => state.Settings.DesktopRows.Contains(x.Key)).Take(state.Settings.DesktopMaxRows).ToList();
            desktopPanel.UpdateRows(selected, state.Settings, async (targets, value) =>
            {
                var page = notificationContext;
                pendingWrites++;
                await gate.WaitAsync();
                try
                {
                    if (closed || version != generation) return;
                    var errors = await Task.Run(() => ControlOperations.Apply(targets, value, state.Settings.GroupDesktopMonitors));
                    if (closed) return;
                    MarkProfileModified(); SynchronizeValues();
                    if (errors.Count > 0) ShowStatus(string.Join("; ", errors), InfoBarSeverity.Error, page);
                }
                catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
                finally { gate.Release(); pendingWrites--; }
            });
            UpdateDesktopProfile(); desktopPanel.ShowPanel();
        }
        catch (Exception ex) { ShowStatus(T("桌面面板：", "Desktop panel: ") + ex.Message, InfoBarSeverity.Error); }
    }
    private void RefreshCrosshair()
    {
        crosshair?.Close(); crosshair = null;
        if (!state.Settings.CrosshairEnabled || closed) return;
        var display = displayDevices.FirstOrDefault(x => x.Id == state.Settings.CrosshairMonitorId) ?? displayDevices.FirstOrDefault(x => x.IsPrimary) ?? displayDevices.FirstOrDefault();
        if (display is not null)
        {
            try { crosshair = new CrosshairWindow(display); }
            catch (Exception ex) { ShowStatus(T("准星：", "Crosshair: ") + ex.Message, InfoBarSeverity.Error); }
        }
    }
    private void BuildSettings()
    {
        SettingsPanel.Children.Clear();
        void Heading(string text) => SettingsPanel.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        void Toggle(string title, string description, bool initial, Action<bool> changed)
        {
            var toggle = new ToggleSwitch { IsOn = initial, OnContent = T("开", "On"), OffContent = T("关", "Off") };
            toggle.Toggled += (_, _) => changed(toggle.IsOn);
            SettingsPanel.Children.Add(SettingsRow(title, description, toggle));
        }
        Heading(T("常规", "General"));
        var languageChoices = LanguageOptions();
        var languages = new ComboBox { ItemsSource = languageChoices, DisplayMemberPath = "Name", SelectedItem = languageChoices.FirstOrDefault(x => x.Code == state.Settings.Language) ?? languageChoices[0], MinWidth = 170 };
        languages.SelectionChanged += async (_, _) =>
        {
            if (languages.SelectedItem is not LanguageOption selectedLanguage) return;
            state.Settings.Language = selectedLanguage.Code;
            SetLanguage(state.Settings.Language); SaveState(); LocalizeUi(); BuildSettings(); await RefreshAsync();
        };
        SettingsPanel.Children.Add(SettingsRow(T("语言", "Language"), T("标题、导航、托盘和提示统一切换", "Updates titles, navigation, tray menus and messages"), languages));
        Toggle(T("关闭窗口后留在托盘", "Keep running in the tray"), T("从托盘菜单可彻底退出", "Use the tray menu to exit completely"), state.Settings.CloseToTray, value => { state.Settings.CloseToTray = value; SaveState(); });
        bool startup;
        try { startup = !uiTest && StartupService.IsEnabled(); } catch { startup = false; }
        Toggle(T("开机自启动", "Launch at sign-in"), T("登录 Windows 后在托盘运行；移动程序文件夹后请重新启用", "Start in the tray after sign-in. Re-enable after moving the app folder."), startup, value =>
        {
            try { if (!uiTest) StartupService.SetEnabled(value); }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); BuildSettings(); }
        });
        Heading(T("全局快捷键", "Global shortcuts"));
        Toggle(T("启用快捷键", "Enable shortcuts"), T("若组合已被占用，会显示冲突而不抢占", "Conflicts are reported without taking over other shortcuts"), state.Settings.HotkeysEnabled, value =>
        {
            state.Settings.HotkeysEnabled = value; hotkeyProblems = shell?.ConfigureHotkeys(value && !uiTest) ?? new(); SaveState(); BuildSettings();
        });
        SettingsPanel.Children.Add(Empty("Ctrl + Alt + Shift + Space   —   " + T("打开面板", "Open panel") + "\nCtrl + Alt + Shift + ← / →   —   " + T("上一个 / 下一个配置", "Previous / next profile") + "\nCtrl + Alt + Shift + ↓   —   " + T("隐藏主面板", "Hide panel")));
        if (hotkeyProblems.Count > 0) SettingsPanel.Children.Add(Empty(T("以下快捷键未注册：", "Unavailable shortcuts: ") + string.Join(", ", hotkeyProblems)));
        if (shell?.TrayAvailable != true) SettingsPanel.Children.Add(Empty(T("托盘不可用，关闭按钮将退出应用。", "Tray unavailable. Closing the window exits the app.")));
        Heading(T("桌面控制面板", "Desktop controls"));
        Toggle(T("显示桌面面板", "Show desktop panel"), T("半透明背景 · 双击解锁 · 拖动右上角移动，右下角缩放", "Translucent · Double-click to unlock · Drag top-right to move, bottom-right to resize"), state.Settings.DesktopPanelEnabled, value => { state.Settings.DesktopPanelEnabled = value; SaveState(); RefreshDesktopPanel(); });
        Toggle(T("显示器一起控制", "Link display controls"), T("关闭后可选择每台显示器的独立控制项", "Turn off to choose controls for individual displays"), state.Settings.GroupDesktopMonitors, value =>
        {
            var old = state.Settings.DesktopRows.ToArray(); state.Settings.GroupDesktopMonitors = value;
            state.Settings.DesktopRows = DesktopCandidates().Where(x => old.Contains(x.Key) || (x.Key.StartsWith("monitor/") && old.Any(k => k.StartsWith("monitor/") && k.Split('/').Last() == x.Key.Split('/').Last()))).Select(x => x.Key).ToList();
            SaveState(); BuildSettings(); RefreshDesktopPanel();
        });
        Toggle(T("使用浅色文字", "Light text"), T("按桌面壁纸明暗选择文字颜色", "Choose text contrast for your wallpaper"), state.Settings.DesktopLightText, value => { state.Settings.DesktopLightText = value; SaveState(); RefreshDesktopPanel(); });
        var opacity = new Slider { Minimum = 10, Maximum = 85, StepFrequency = 5, Value = state.Settings.DesktopOpacity, Width = 170 };
        opacity.ValueChanged += (_, e) => { state.Settings.DesktopOpacity = e.NewValue; SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("背景不透明度", "Background opacity"), T("数值越低越通透，文字保持清晰", "Lower values reveal more of the desktop; text stays opaque"), opacity));
        var resetSize = new Button { Content = T("恢复自动尺寸", "Reset to automatic size") };
        resetSize.Click += (_, _) => { state.Settings.DesktopWidth = 340; state.Settings.DesktopHeight = null; SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("面板尺寸", "Panel size"), T("拖动后记住宽高；空间不足时控制行可滚动", "Remembers resized dimensions; control rows scroll when space is limited"), resetSize));
        var maximum = new NumberBox { Minimum = 1, Maximum = 16, Value = state.Settings.DesktopMaxRows, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 100 };
        maximum.ValueChanged += (_, e) => { if (double.IsNaN(e.NewValue)) return; state.Settings.DesktopMaxRows = (int)Math.Clamp(e.NewValue, 1, 16); SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("最多显示行数", "Maximum rows"), T("按下方顺序显示已勾选的控制项", "Selected rows appear in the order below"), maximum));
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
        SettingsPanel.Children.Add(Card(choices));
        Heading(T("软件准星", "Software crosshair"));
        Toggle(T("显示准星", "Show crosshair"), T("用于窗口 / 无边框模式，不控制显示器自带准星", "For windowed / borderless apps; independent of the monitor's own crosshair"), state.Settings.CrosshairEnabled, value => { state.Settings.CrosshairEnabled = value; SaveState(); RefreshCrosshair(); });
        var target = new ComboBox { ItemsSource = displayDevices, DisplayMemberPath = "DisplayName", SelectedItem = displayDevices.FirstOrDefault(x => x.Id == state.Settings.CrosshairMonitorId) ?? displayDevices.FirstOrDefault(x => x.IsPrimary) ?? displayDevices.FirstOrDefault() };
        target.SelectionChanged += (_, _) => { if (target.SelectedItem is MonitorDevice d) { state.Settings.CrosshairMonitorId = d.Id; SaveState(); RefreshCrosshair(); } };
        SettingsPanel.Children.Add(SettingsRow(T("显示屏幕", "Target display"), "", target));
        var exit = new Button { Content = T("退出聚合控制", "Exit Fluent Control") }; exit.Click += (_, _) => ShellCommand(6); SettingsPanel.Children.Add(exit);
    }
    private static Border SettingsRow(string title, string description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (description.Length > 0) text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(control); return Card(grid);
    }
}

public sealed partial class MainWindow
{
    private FrameworkElement CreateChoiceRow(ControlChannel channel, int version, IReadOnlyList<ControlChannel>? group)
    {
        var targets = group ?? new[] { channel };
        var options = channel.Options!;
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", MinWidth = 150, PlaceholderText = T("不同", "Mixed") };
        var syncing = false;
        void Update()
        {
            syncing = true;
            combo.SelectedItem = targets.All(x => x.Value == targets[0].Value) ? options.FirstOrDefault(x => x.Value == targets[0].Value) : null;
            syncing = false;
        }
        refreshRows.Add(Update); Update();
        combo.SelectionChanged += async (_, _) =>
        {
            if (syncing || combo.SelectedItem is not ControlOption choice) return;
            if (targets.Any(x => x.RequiresConfirmation) && !await ConfirmMonitorChangeAsync(channel, targets)) { Update(); return; }
            pendingWrites++; var page = notificationContext;
            await gate.WaitAsync();
            try
            {
                if (version != generation || closed) return;
                var errors = await Task.Run(() => ControlOperations.Apply(targets, choice.Value));
                if (closed) return;
                MarkProfileModified(); SynchronizeValues();
                ShowStatus(errors.Count == 0 ? F("已更新{0}", "{0} updated", Channel(channel)) : string.Join("; ", errors), errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error, page);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
            finally { pendingWrites--; gate.Release(); }
        };
        return SettingsRow((group is null ? "" : T("统一", "Linked ")) + Channel(channel), channel.Detail, combo);
    }
    private async Task RunFeatureChecksAsync()
    {
        if (!uiTest) return;
        var oldProfiles = state.Profiles.ToList(); var oldSelected = state.SelectedProfileId;
        var oldSettings = state.Settings;
        try
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "FluentControl.ico"))) throw new InvalidOperationException("Icon not published.");
            ShowStatus("transient", InfoBarSeverity.Success);
            await Task.Delay(4300);
            if (Status.IsOpen) throw new InvalidOperationException("Status did not expire.");
            ShowStatus("old page", InfoBarSeverity.Success);
            Navigation.SelectedItem = Navigation.SettingsItem;
            if (Status.IsOpen || SettingsPanel.Visibility != Visibility.Visible) throw new InvalidOperationException("Navigation failed to clear status or show settings.");
            SetLanguage("en-US"); LocalizeUi(); BuildSettings();
            if (PageTitle.Text != "Settings") throw new InvalidOperationException("English language not applied.");
            SetLanguage("zh-CN"); LocalizeUi();
            if (PageTitle.Text != "设置") throw new InvalidOperationException("Chinese language not applied.");
            ValidateCatalog();
            foreach (var language in SupportedLanguages)
            {
                SetLanguage(language); LocalizeUi(); BuildSettings();
                if (!Title.Contains(AppName) || Navigation.PaneTitle != AppName || PageTitle.Text != T("设置", "Settings")) throw new InvalidOperationException("Title or menu language failed: " + language);
            }
            SetLanguage("zh-CN"); LocalizeUi();
            var profile = new ControlProfile { Name = "UI test profile", Values = CaptureProfile() };
            state.Profiles.Add(profile);
            var brightness = displayDevices[0].Channels.First(x => x.PropertyKey == "brightness");
            var original = brightness.Value; brightness.Value = original + 7;
            await ApplyProfileAsync(profile);
            if (brightness.Value != original) throw new InvalidOperationException("Profile failed to restore controls.");
            var persisted = new UserStateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (!persisted.State.Profiles.Any(x => x.Id == profile.Id)) throw new InvalidOperationException("Profile not persisted.");
            state.Settings = new AppSettings { DesktopPanelEnabled = true, CrosshairEnabled = true };
            RefreshDesktopPanel(); RefreshCrosshair();
            await Task.Delay(250);
            if (desktopPanel is null || desktopPanel.RowCount != 2 || desktopPanel.IsUnlocked) throw new InvalidOperationException("Desktop panel did not start locked with two rows.");
            desktopPanel.SetUnlocked(true);
            if (!desktopPanel.IsUnlocked) throw new InvalidOperationException("Desktop panel cannot unlock.");
            desktopPanel.SetUnlocked(false);
            if (desktopPanel.IsUnlocked) throw new InvalidOperationException("Desktop panel cannot lock.");
            AppWindow.Hide();
            try { await PanelDiagnostics.VerifyAsync(desktopPanel); }
            finally { AppWindow.Show(); Activate(); }
            var savedWidth = state.Settings.DesktopWidth;
            var savedHeight = state.Settings.DesktopHeight;
            SaveState();
            desktopPanel.Close(); desktopPanel = null;
            RefreshDesktopPanel();
            if (desktopPanel is null || savedWidth != state.Settings.DesktopWidth || savedHeight != state.Settings.DesktopHeight) throw new InvalidOperationException("Panel size not restored.");
            if (crosshair is null) throw new InvalidOperationException("Crosshair failed to initialize.");
            StartupLog.Write("Tray available: " + (shell?.TrayAvailable == true));
            if (shell is not null)
            {
                var errors = shell.ConfigureHotkeys(true); shell.ConfigureHotkeys(false);
                if (errors.Count > 0) throw new InvalidOperationException("Hotkey registration failed: " + string.Join(",", errors));
            }
            AppWindow.Hide(); AppWindow.Show(); Activate();
            StartupLog.Write("Feature checks passed: transient status, languages, profiles, desktop alpha pixels, movement, resizing, unclipped footer, lock, crosshair, tray lifecycle and hotkeys");
        }
        finally
        {
            state.Profiles = oldProfiles; state.SelectedProfileId = oldSelected; state.Settings = oldSettings;
            desktopPanel?.Close(); desktopPanel = null; crosshair?.Close(); crosshair = null;
            SetLanguage(state.Settings.Language); SaveState(); LocalizeUi(); BuildSettings(); RefreshProfiles();
            Navigation.SelectedItem = Navigation.MenuItems[0]; DisplayMode.SelectedIndex = 1;
        }
    }
}
