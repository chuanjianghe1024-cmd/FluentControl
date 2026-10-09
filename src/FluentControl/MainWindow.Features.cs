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
        CloseMonitorOsd();
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
                case 2: await SwitchGlobalProfileAsync(-1); break;
                case 3: await SwitchGlobalProfileAsync(1); break;
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
        ((NavigationViewItem)Navigation.MenuItems[3]).Content = T("型号配置库", "Model preset library");
        GlobalProfileTitle.Text = T("总配置", "Global profile");
        if (Navigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = T("设置", "Settings");
        var index = DisplayMode.SelectedIndex;
        DisplayMode.Items[0] = T("单独控制", "Individual"); DisplayMode.Items[1] = T("整体控制", "Overall control"); DisplayMode.SelectedIndex = index;
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
        GroupPicker.PlaceholderText = T("配置分组", "Profile group");
        DeleteGroupButton.Text = T("删除分组", "Delete group");
        ToolTipService.SetToolTip(GroupActionsButton, T("管理分组", "Manage groups"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(GroupActionsButton, T("管理分组", "Manage groups"));
        NewGroupButton.Text = T("新建分组", "New group"); EditProfileButton.Content = T("信息 / 标签", "Info / tags"); FilterProfilesButton.Content = T("筛选配置", "Filter profiles");
        ProfilePicker.PlaceholderText = T("选择总配置", "Choose global profile");
        SaveProfileButton.Content = T("保存总配置", "Save global profile"); UpdateProfileButton.Content = T("更新", "Update"); DeleteProfileButton.Content = T("删除", "Delete");
        ToolTipService.SetToolTip(PreviousProfileButton, T("上一个配置", "Previous profile")); ToolTipService.SetToolTip(NextProfileButton, T("下一个配置", "Next profile"));
        UpdatePageTitle();
    }
    private void UpdatePageTitle()
    {
        var tag = Navigation.SelectedItem == Navigation.SettingsItem ? "settings" : (Navigation.SelectedItem as NavigationViewItem)?.Tag as string ?? "monitors";
        PageTitle.Text = tag switch { "audio" => T("声音与麦克风", "Audio"), "mouse" => T("鼠标与指针", "Mouse & pointer"), "library" => T("型号配置库", "Model preset library"), "settings" => T("设置", "Settings"), _ => T("显示器", "Displays") };
        Title = AppName + " · " + PageTitle.Text;
        PageDescription.Text = tag switch
        {
            "audio" => T("常用设备在前，其他设备按需展开。", "Your default devices first. Expand the rest when needed."),
            "mouse" => T("找到适合自己的移动速度与指针大小。", "Tune movement speed and pointer size."),
            "settings" => T("按自己的习惯使用聚合控制。", "Make Fluent Control work your way."),
            "library" => T("按型号保存可复用的显示器参数；总配置保存整套桌面状态。", "Reusable monitor settings by model; global profiles save the whole desktop state."),
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
        ProfileGroups.Normalize(state);
        rebuildingProfiles = true;
        GroupPicker.ItemsSource = null; GroupPicker.ItemsSource = state.Groups;
        GroupPicker.SelectedItem = state.Groups.First(x => x.Id == state.SelectedGroupId);
        DeleteGroupButton.IsEnabled = state.SelectedGroupId != ProfileGroup.LocalId;
        var profiles = VisibleProfiles();
        foreach (var profile in profiles) profile.DisplayLabel = profile.Name;
        ProfilePicker.ItemsSource = null; ProfilePicker.ItemsSource = profiles;
        ProfilePicker.SelectedItem = profiles.FirstOrDefault(x => x.Id == state.SelectedProfileId);
        EditProfileButton.IsEnabled = UpdateProfileButton.IsEnabled = DeleteProfileButton.IsEnabled = ProfilePicker.SelectedItem is not null;
        PreviousProfileButton.IsEnabled = NextProfileButton.IsEnabled = profiles.Count > 0;
        rebuildingProfiles = false;
        ProfileFilterSummary.Visibility = HasProfileFilters ? Visibility.Visible : Visibility.Collapsed;
        ProfileFilterSummary.Text = F("筛选结果：{0} 个配置", "Filter results: {0} profiles", profiles.Count);
        UpdateDesktopProfile();
    }
    private void UpdateDesktopProfile()
    {
        var name = state.Profiles.FirstOrDefault(x => x.Id == state.SelectedProfileId)?.Name;
        desktopPanel?.SetNavigation("", name is null ? T("未选择配置", "No profile selected") : name + (profileDirty ? " *" : ""), false, state.Profiles.Count > 0);
    }
    private async Task SwitchDesktopGroupAsync(int delta)
    {
        if (closed || refreshing || applyingProfile || state.Groups.Count == 0) return;
        var current = state.Groups.FindIndex(g => g.Id == state.SelectedGroupId);
        var group = state.Groups[(Math.Max(0, current) + delta % state.Groups.Count + state.Groups.Count) % state.Groups.Count];
        await SelectGroupAsync(group.Id);
    }
    private Task SwitchDesktopProfileAsync(int delta) => SwitchGlobalProfileAsync(delta);
    private Task SwitchGlobalProfileAsync(int delta) => SwitchFromProfilesAsync(ProfileGroups.AllOrdered(state), delta);
    private async Task SwitchFromProfilesAsync(IReadOnlyList<ControlProfile> profiles, int delta)
    {
        var index = UserStateStore.NextIndex(profiles, state.SelectedProfileId, delta);
        if (index < 0) return;
        await ApplyProfileAsync(profiles[index]);
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
        var profiles = VisibleProfiles();
        var index = UserStateStore.NextIndex(profiles, state.SelectedProfileId, delta);
        if (index < 0) { ShowStatus(T("请先保存一个配置。", "Save a profile first."), InfoBarSeverity.Informational); return; }
        await ApplyProfileAsync(profiles[index]);
    }
    private async Task WaitForWritesAsync()
    {
        while ((pendingWrites > 0 || desktopPanel?.HasPendingChanges == true) && !closed) await Task.Delay(25);
    }
    private async Task ApplyProfileAsync(ControlProfile profile)
    {
        if (applyingProfile || refreshing || closed || monitorAdaptationDialog is not null) return;
        applyingProfile = true; var page = notificationContext;
        try
        {
            await WaitForWritesAsync();
            foreach (var mapping in profile.BrightnessMappings.Values) mapping.Validate();
            if (!await ConfirmProfileInputAsync(profile)) { RefreshProfiles(); return; }
            await gate.WaitAsync();
            try
            {
                if (closed || monitorAdaptationDialog is not null) return;
                var available = AllChannels();
                var result = await Task.Run(() =>
                {
                    var errors = new List<string>(); var missing = 0; var applied = 0;
                    foreach (var entry in profile.Values.OrderBy(x => available.TryGetValue(x.Key, out var c) ? c.ApplyOrder : 50))
                    {
                        if (!available.TryGetValue(entry.Key, out var channel)) { missing++; continue; }
                        if (MonitorColorTemperature.IsSuperseded(channel, profile.Values, available)) continue;
                        if (entry.Key.StartsWith("monitor/") && !ProfileExchange.CanApply(channel, entry.Value.Value)) { missing++; continue; }
                        if (channel.PropertyKey == "input" && channel.Value == entry.Value.Value) continue;
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
                state.ActiveMonitorPresets = new(profile.MonitorPresetIds);
                state.SelectedGroupId = profile.GroupId; state.SelectedProfileId = profile.Id; profileDirty = result.errors.Count > 0 || result.missing > 0;
                SaveState(); RefreshProfiles(); SynchronizeValues(); RenderMonitorControls(generation);
                var message = F("配置「{0}」：已更新 {1} 项", "Profile '{0}': {1} controls applied", profile.Name, result.applied);
                if (result.missing > 0) message += F("，跳过 {0} 个未连接或不可设置项", "; {0} unavailable controls skipped", result.missing);
                if (result.errors.Count > 0) message += " · " + string.Join("; ", result.errors);
                ShowStatus(message, result.errors.Count > 0 || result.missing > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, page);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
        finally { applyingProfile = false; }
    }
    private Dictionary<string, SavedValue> CaptureProfile() => AllChannels()
        .Where(x => !x.Value.CompatibilityOnly)
        .Where(x => !x.Key.StartsWith("monitor/") || ProfileExchange.CanApply(x.Value, x.Value.Value))
        .ToDictionary(x => x.Key, x => new SavedValue { Value = x.Value.Value, Muted = x.Value.WriteMute is null ? null : x.Value.IsMuted });
    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Header = T("配置名称", "Profile name"), PlaceholderText = T("例如：阅读、游戏、夜间", "Reading, gaming, evening…"), MaxLength = 80 };
        var apps = new TextBox { Header = T("适用应用 / 游戏（逗号分隔）", "Apps / games (comma separated)"), MaxLength = 1500 };
        var contents = new StackPanel { Spacing = 12 }; contents.Children.Add(input); contents.Children.Add(apps);
        var dialog = new ContentDialog { Title = T("保存总配置", "Save global profile"), Content = contents, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            await WaitForWritesAsync(); await gate.WaitAsync();
            try
            {
                var name = input.Text.Trim();
                if (state.Profiles.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) { ShowStatus(T("配置名称已存在，可用“更新”覆盖。", "That name exists. Use Update to replace it."), InfoBarSeverity.Warning); return; }
                var profile = CaptureGlobalProfile(name, ParseApplications(apps.Text));
                if (profile.Values.Count == 0) { ShowStatus(T("没有可保存的控制项。", "No controls are available to save."), InfoBarSeverity.Warning); return; }
                var oldPresets = state.MonitorPresets.ToList(); var oldActive = state.ActiveMonitorPresets; var oldSelected = state.SelectedProfileId; var oldGroup = state.SelectedGroupId;
                MonitorPresetLibrary.AttachSnapshots(state, profile); state.ActiveMonitorPresets = new(profile.MonitorPresetIds);
                state.Profiles.Add(profile); state.SelectedProfileId = profile.Id; state.SelectedGroupId = profile.GroupId; profileDirty = false;
                if (SaveState()) { BuildPresetLibrary(); RenderMonitorControls(generation); ShowStatus(T("配置已保存。", "Profile saved."), InfoBarSeverity.Success); }
                else { state.Profiles.Remove(profile); state.MonitorPresets = oldPresets; state.ActiveMonitorPresets = oldActive; state.SelectedProfileId = oldSelected; state.SelectedGroupId = oldGroup; profileDirty = true; }
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
            var values = profile.Values; var mappings = profile.BrightnessMappings; var metadata = profile.Monitors;
            var oldRefs = new Dictionary<string, string>(profile.MonitorPresetIds); var oldPresets = state.MonitorPresets.ToList(); var oldActive = state.ActiveMonitorPresets;
            ProfileUpdates.Merge(profile, CaptureProfile(), CaptureMonitorMetadata(), CaptureMappings());
            MonitorPresetLibrary.AttachSnapshots(state, profile); state.ActiveMonitorPresets = new(profile.MonitorPresetIds);
            if (SaveState()) { profileDirty = false; BuildPresetLibrary(); RenderMonitorControls(generation); ShowStatus(T("配置已更新。", "Profile updated."), InfoBarSeverity.Success); }
            else { profile.Values = values; profile.BrightnessMappings = mappings; profile.Monitors = metadata; profile.MonitorPresetIds = oldRefs; state.MonitorPresets = oldPresets; state.ActiveMonitorPresets = oldActive; }
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
            var index = state.Profiles.IndexOf(profile); var selected = state.SelectedProfileId;
            state.Profiles.Remove(profile); state.SelectedProfileId = null;
            if (!SaveState()) { state.Profiles.Insert(index, profile); state.SelectedProfileId = selected; }
            RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private void SynchronizeValues()
    {
        foreach (var refresh in refreshRows) refresh(); desktopPanel?.RefreshValues();
    }
    private List<PanelRow> DesktopCandidates()
    {
        // Preserve a selected legacy temperature row when its canonical VCP row becomes available.
        string CanonicalKey(string key)
        {
            if (!key.EndsWith("/temperature", StringComparison.Ordinal)) return key;
            var legacyDevices = displayDevices.Where(d => d.Channels.Any(c => c.CompatibilityOnly)).ToArray();
            if (key == "monitor/all/temperature" && legacyDevices.Length == displayDevices.Count && legacyDevices.Length > 0 ||
                legacyDevices.Any(d => key == $"monitor/{Uri.EscapeDataString(d.Id)}/temperature"))
                return key[..^"temperature".Length] + "color-preset";
            return key;
        }
        var originalRows = state.Settings.DesktopRows;
        state.Settings.DesktopRows = originalRows.Select(CanonicalKey).Distinct().ToList();
        if (state.Settings.DesktopIndividualRows is not null) state.Settings.DesktopIndividualRows = state.Settings.DesktopIndividualRows.Select(CanonicalKey).Distinct().ToList();
        if (state.Settings.DesktopLinkedRows is not null) state.Settings.DesktopLinkedRows = state.Settings.DesktopLinkedRows.Select(CanonicalKey).Distinct().ToList();
        if (!originalRows.SequenceEqual(state.Settings.DesktopRows)) SaveState();
        var result = new List<PanelRow>();
        if (state.Settings.GroupDesktopMonitors)
        {
            var normalized = MonitorLinking.NormalizeLinkedDesktopRows(state.Settings.DesktopRows);
            if (!state.Settings.DesktopRows.SequenceEqual(normalized)) { state.Settings.DesktopRows = normalized; SaveState(); }
            var devices = displayDevices.ToArray();
            foreach (var controls in MonitorLinking.DesktopGroups(devices.SelectMany(d => d.Channels)))
            {
                var key = controls.Key;
                var targets = controls.ToArray();
                var supported = devices.Where(d => d.Channels.Any(c => c.PropertyKey == key)).ToArray();
                var names = string.Join(" + ", supported.Select(d => d.DisplayName));
                if (supported.Length < devices.Length) names = F("仅 {0}", "Only {0}", names);
                var label = supported.Length < devices.Length ? names : T("整体", "All");
                result.Add(new() { Key = "monitor/all/" + key, Name = label + " · " + Channel(targets[0]), Detail = SupportSummary(key, devices), Group = "monitor/all", Targets = targets, Linked = true,
                    Options = MonitorLinking.DesktopOptions(targets, c => devices.First(d => d.Id == c.DeviceId).DisplayName) });
            }
        }
        else foreach (var display in displayDevices) foreach (var c in display.Channels.Where(MonitorLinking.IsDesktopControl))
            result.Add(new() { Key = $"monitor/{Uri.EscapeDataString(display.Id)}/{c.PropertyKey}", Name = display.DisplayName + " · " + Channel(c), Group = display.Id, Targets = new[] { c } });
        foreach (var c in audioChannels.Where(x => x.IsDefaultAudio)) result.Add(new() { Key = $"audio/{Uri.EscapeDataString(c.DeviceId)}/{c.PropertyKey}", Name = c.Name, Group = "audio", Targets = new[] { c } });
        foreach (var c in mouseChannels) result.Add(new() { Key = "mouse/" + c.PropertyKey, Name = Channel(c), Group = "mouse", Targets = new[] { c } });
        var legacy = state.Settings.DesktopRows.Where(k => k.StartsWith("monitor/all/", StringComparison.Ordinal)).ToArray();
        if (!state.Settings.GroupDesktopMonitors && displayDevices.Count > 0 && legacy.Length > 0)
        {
            foreach (var item in result.Where(r => r.Key.StartsWith("monitor/") && legacy.Any(k => k.Split('/').Last() == r.Key.Split('/').Last())))
                if (!state.Settings.DesktopRows.Contains(item.Key)) state.Settings.DesktopRows.Add(item.Key);
            state.Settings.DesktopRows.RemoveAll(k => legacy.Contains(k)); SaveState();
        }
        return result;
    }
    private async Task SetDesktopMonitorModeAsync(bool linked)
    {
        await WaitForWritesAsync();
        if (closed || state.Settings.GroupDesktopMonitors == linked) return;
        var unlocked = desktopPanel?.IsUnlocked == true;
        var individualKeys = displayDevices.SelectMany(d => d.Channels.Where(MonitorLinking.IsDesktopControl)
            .Select(c => $"monitor/{Uri.EscapeDataString(d.Id)}/{c.PropertyKey}"));
        MonitorLinking.ChangeDesktopMode(state.Settings, linked, individualKeys);
        SaveState(); BuildSettings(); RefreshDesktopPanel();
        if (unlocked) desktopPanel?.SetUnlocked(true);
    }
    private void RefreshDesktopPanel()
    {
        try
        {
            if (!state.Settings.DesktopPanelEnabled || closed) { desktopPanel?.Close(); desktopPanel = null; return; }
            // A live theme change on this transparent secondary WinUI window can
            // invalidate theme resources during layout. Recreate with its final theme.
            if (desktopPanel is not null && desktopPanel.UsesLightText != state.Settings.DesktopLightText)
            { desktopPanel.Close(); desktopPanel = null; }
            desktopPanel ??= new DesktopPanelWindow(state.Settings, delta => _ = SwitchDesktopGroupAsync(delta), delta => _ = SwitchDesktopProfileAsync(delta), linked => _ = SetDesktopMonitorModeAsync(linked), () =>
            {
                pendingStateTimer.Stop(); pendingStateTimer.Start();
            });
            var version = generation;
            var selected = DesktopCandidates().Where(x => state.Settings.DesktopRows.Contains(x.Key)).ToList();
            desktopPanel.UpdateRows(selected, state.Settings, async (targets, value, linked) =>
            {
                var page = notificationContext;
                pendingWrites++;
                await gate.WaitAsync();
                try
                {
                    if (closed || version != generation || monitorAdaptationDialog is not null) return;
                    var errors = await Task.Run(() => ControlOperations.Apply(targets, value, linked));
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
        InitializationText.Text = T("正在初始化设备…", "Initializing devices…");
        InitializationDetail.Text = T("正在读取显示器信息与控制能力，请稍候。", "Reading display information and supported controls. Please wait.");
        void Heading(string text) => SettingsPanel.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        void Toggle(string title, string description, bool initial, Action<bool> changed)
        {
            var toggle = new ToggleSwitch { IsOn = initial, OnContent = T("开", "On"), OffContent = T("关", "Off") };
            if (title == T("使用浅色文字", "Light text")) Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "setting-light-text");
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
        Toggle(T("开机自启动", "Launch at sign-in"), T("使用当前用户的启动文件夹，登录后在托盘运行；移动程序后请重新启用", "Uses your Startup folder and starts in the tray. Re-enable after moving the app."), startup, value =>
        {
            try { if (!uiTest) StartupService.SetEnabled(value); }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); BuildSettings(); }
        });
        var startupSettings = new HyperlinkButton { Content = T("Windows 启动应用设置", "Windows Startup Apps"), Tag = "ms-settings:startupapps" };
        startupSettings.Click += OpenSettings_Click; SettingsPanel.Children.Add(startupSettings);
        SettingsPanel.Children.Add(new HyperlinkButton { Content = "fctrl.app", NavigateUri = new Uri("https://fctrl.app") });
        var rescanDisplays = new Button { Content = T("重新检测", "Detect again") };
        rescanDisplays.Click += async (_, _) =>
        {
            if (refreshing) return;
            rescanDisplays.IsEnabled = false;
            try { await RefreshAsync(forceMonitorCapabilities: true); }
            finally { if (!closed) rescanDisplays.IsEnabled = true; }
        };
        SettingsPanel.Children.Add(SettingsRow(T("显示器功能检测", "Display capability scan"), T("通常复用能力缓存；更换连接或显示器模式后，可重新完整检测", "Normally reuses cached capabilities. Detect again after changing connections or monitor modes."), rescanDisplays));
        Heading(T("全局快捷键", "Global shortcuts"));
        Toggle(T("启用快捷键", "Enable shortcuts"), T("若组合已被占用，会显示冲突而不抢占", "Conflicts are reported without taking over other shortcuts"), state.Settings.HotkeysEnabled, value =>
        {
            state.Settings.HotkeysEnabled = value; hotkeyProblems = shell?.ConfigureHotkeys(value && !uiTest) ?? new(); SaveState(); BuildSettings();
        });
        SettingsPanel.Children.Add(Empty("Ctrl + Alt + Shift + Space   —   " + T("打开面板", "Open panel") + "\nCtrl + Alt + Shift + ← / →   —   " + T("跨分组：上一个 / 下一个配置", "Across groups: previous / next profile") + "\nCtrl + Alt + Shift + ↓   —   " + T("隐藏主面板", "Hide panel")));
        if (hotkeyProblems.Count > 0) SettingsPanel.Children.Add(Empty(T("以下快捷键未注册：", "Unavailable shortcuts: ") + string.Join(", ", hotkeyProblems)));
        if (shell?.TrayAvailable != true) SettingsPanel.Children.Add(Empty(T("托盘不可用，关闭按钮将退出应用。", "Tray unavailable. Closing the window exits the app.")));
        Heading(T("桌面控制面板", "Desktop controls"));
        Toggle(T("显示桌面面板", "Show desktop panel"), T("半透明背景 · 双击解锁 · 拖动右上角移动，右下角缩放", "Translucent · Double-click to unlock · Drag top-right to move, bottom-right to resize"), state.Settings.DesktopPanelEnabled, value => { state.Settings.DesktopPanelEnabled = value; SaveState(); RefreshDesktopPanel(); });
        var desktopMode = new ComboBox { ItemsSource = new[] { T("单独控制", "Individual"), T("整体控制", "Overall control") }, SelectedIndex = state.Settings.GroupDesktopMonitors ? 1 : 0, MinWidth = 150 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(desktopMode, "setting-desktop-mode");
        desktopMode.SelectionChanged += async (_, _) => { if (desktopMode.SelectedIndex >= 0) await SetDesktopMonitorModeAsync(desktopMode.SelectedIndex == 1); };
        SettingsPanel.Children.Add(SettingsRow(T("显示器控制", "Monitor controls"), T("不同型号也一起调节；关闭后每台屏幕单独控制", "Link different models too. Turn off to control each display separately"), desktopMode));
        Toggle(T("使用浅色文字", "Light text"), T("按桌面壁纸明暗选择文字颜色", "Choose text contrast for your wallpaper"), state.Settings.DesktopLightText, value => { state.Settings.DesktopLightText = value; SaveState(); RefreshDesktopPanel(); });
        var opacity = new Slider { Minimum = 10, Maximum = 85, StepFrequency = 5, Value = state.Settings.DesktopOpacity, Width = 170 };
        opacity.ValueChanged += (_, e) => { state.Settings.DesktopOpacity = e.NewValue; SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("背景不透明度", "Background opacity"), T("数值越低越通透，文字保持清晰", "Lower values reveal more of the desktop; text stays opaque"), opacity));
        var resetSize = new Button { Content = T("恢复自动尺寸", "Reset to automatic size") };
        resetSize.Click += (_, _) => { state.Settings.DesktopWidth = 340; state.Settings.DesktopHeight = null; SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("面板尺寸", "Panel size"), T("拖动后记住宽高；空间不足时控制行可滚动", "Remembers resized dimensions; control rows scroll when space is limited"), resetSize));
        var maximum = new NumberBox { Minimum = 1, Maximum = 16, Value = state.Settings.DesktopMaxRows, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 100 };
        maximum.ValueChanged += (_, e) => { if (double.IsNaN(e.NewValue)) return; state.Settings.DesktopMaxRows = (int)Math.Clamp(e.NewValue, 1, 16); SaveState(); RefreshDesktopPanel(); };
        SettingsPanel.Children.Add(SettingsRow(T("最多显示行数", "Maximum rows"), T("限制自动高度，超出的控制项可滚动查看", "Limits automatic height; scroll to see additional controls"), maximum));
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
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", MinWidth = 150, MaxWidth = 340, PlaceholderText = T("不同", "Mixed") };
        var syncing = false;
        void Update()
        {
            syncing = true;
            var same = targets.All(x => x.Value == targets[0].Value);
            combo.SelectedItem = same ? options.FirstOrDefault(x => x.Value == targets[0].Value) : null;
            combo.PlaceholderText = same && combo.SelectedItem is null ? F("当前值 0x{0}（不可重放）", "Current 0x{0} (not replayable)", ((uint)targets[0].Value).ToString("X")) : T("不同", "Mixed");
            syncing = false;
        }
        refreshRows.Add(Update); Update();
        combo.SelectionChanged += async (_, _) =>
        {
            if (syncing || combo.SelectedItem is not ControlOption choice) return;
            if (targets.Any(x => x.RequiresConfirmation) && !await ConfirmMonitorChangeAsync(channel, targets)) { Update(); return; }
            pendingWrites++; combo.IsEnabled = false; var page = notificationContext;
            await gate.WaitAsync();
            try
            {
                if (version != generation || closed) return;
                var errors = await Task.Run(() => ControlOperations.Apply(targets.Where(c => c.Options?.Any(o => o.Value == choice.Value) == true), choice.Value));
                if (closed) return;
                MarkProfileModified(); SynchronizeValues();
                ShowStatus(errors.Count == 0 ? F("已更新{0}", "{0} updated", Channel(channel)) : string.Join("; ", errors), errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error, page);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error, page); }
            finally { pendingWrites--; gate.Release(); if (!closed && version == generation) { combo.IsEnabled = true; Update(); } }
        };
        return SettingsRow((group is null ? "" : T("统一", "Linked ")) + Channel(channel), channel.Detail, combo);
    }
    private async Task RunFeatureChecksAsync()
    {
        if (!uiTest) return;
        var oldProfiles = state.Profiles.ToList(); var oldSelected = state.SelectedProfileId;
        var oldSettings = state.Settings;
        var oldGroups = state.Groups.ToList(); var oldGroup = state.SelectedGroupId;
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
            await CheckProfilesAndThemeAsync();
            var profile = new ControlProfile { Name = "UI test profile", Values = CaptureProfile() };
            state.Profiles.Add(profile);
            var brightness = displayDevices[0].Channels.First(x => x.PropertyKey == "brightness");
            var original = brightness.Value; brightness.Value = original + 7;
            await ApplyProfileAsync(profile);
            if (brightness.Value != original) throw new InvalidOperationException("Profile failed to restore controls.");
            var persisted = new UserStateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (!persisted.State.Profiles.Any(x => x.Id == profile.Id)) throw new InvalidOperationException("Profile not persisted.");
            state.Settings = new AppSettings { DesktopPanelEnabled = true, CrosshairEnabled = true,
                DesktopRows = new() { "monitor/model/TST0001/brightness", "monitor/model/TST0002/brightness", "monitor/device/offline/contrast", "monitor/ui-test-monitor-0/contrast" } };
            RefreshDesktopPanel(); RefreshCrosshair();
            await Task.Delay(250);
            if (desktopPanel is null || desktopPanel.RowCount != 2 || desktopPanel.IsUnlocked) throw new InvalidOperationException("Desktop panel did not start locked with two rows.");
            await CheckDesktopLinkedControlsAsync();
            await CheckPanelTextSwitchAsync();
            await CheckDesktopNavigationAsync();
            desktopPanel!.SetUnlocked(true);
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
            state.Groups = oldGroups; state.SelectedGroupId = oldGroup; applicationFilter = modelFilter = brandFilter = "";
            desktopPanel?.Close(); desktopPanel = null; crosshair?.Close(); crosshair = null;
            SetLanguage(state.Settings.Language); SaveState(); LocalizeUi(); BuildSettings(); RefreshProfiles();
            Navigation.SelectedItem = Navigation.MenuItems[0]; DisplayMode.SelectedIndex = 1;
        }
    }
}
