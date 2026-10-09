using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private sealed record ModelGroupOption(string Id, string Name);
    private string libraryModel = "", librarySearch = "";
    private List<ModelGroupOption> LibraryGroups() => new[] { new ModelGroupOption("", T("全部型号", "All models")) }
        .Concat(state.MonitorPresets.GroupBy(p => MonitorPresetLibrary.GroupKey(p.Monitor)).Select(g => new ModelGroupOption(g.Key, MonitorPresetLibrary.GroupName(g.First().Monitor)))).ToList();
    private static bool PresetMatches(MonitorPreset preset, string group, string query) =>
        (group.Length == 0 || MonitorPresetLibrary.GroupKey(preset.Monitor) == group) &&
        string.Join(" ", new[] { preset.Name, preset.Scenario, preset.Summary, preset.Monitor.ModelId, preset.Monitor.ModelName, preset.Monitor.Brand }.Concat(preset.Applications)).Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private FrameworkElement CreateMonitorPresetRow(MonitorDevice device)
    {
        var active = state.MonitorPresets.FirstOrDefault(p => p.Id == state.ActiveMonitorPresets.GetValueOrDefault(device.Id));
        var panel = new StackPanel { Spacing = 6 };
        var title = Empty(""); panel.Children.Add(title);
        void Sync()
        {
            var current = state.MonitorPresets.FirstOrDefault(p => p.Id == state.ActiveMonitorPresets.GetValueOrDefault(device.Id));
            title.Text = T("显示器配置", "Monitor preset") + " · " + (current is null ? T("自定义", "Custom") : current.Name + (MonitorPresetLibrary.MatchesCurrent(current, device) ? "" : " *"));
        }
        refreshRows.Add(Sync); Sync();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var choose = new Button { Content = T("选用配置", "Choose preset") };
        choose.Click += async (_, _) => await ChooseMonitorPresetAsync(device);
        var save = new Button { Content = T("保存当前", "Save current") };
        save.Click += async (_, _) => await SaveMonitorPresetAsync(device, active);
        actions.Children.Add(choose); actions.Children.Add(save); panel.Children.Add(actions);
        return panel;
    }
    private void BuildPresetLibrary()
    {
        PresetLibraryPanel.Children.Clear();
        PresetLibraryPanel.Children.Add(Empty(T("按型号保存可复用的显示器参数；总配置保存整套桌面状态。", "Reusable monitor settings by model; global profiles save the whole desktop state.")));
        var groups = LibraryGroups();
        var model = new ComboBox { Header = T("显示器型号", "Monitor model"), ItemsSource = groups, DisplayMemberPath = "Name", SelectedItem = groups.FirstOrDefault(g => g.Id == libraryModel) ?? groups[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var search = new TextBox { PlaceholderText = T("搜索名称、场景或应用", "Search name, scenario or app"), Text = librarySearch };
        PresetLibraryPanel.Children.Add(model); PresetLibraryPanel.Children.Add(search);
        var exchange = new Button { Content = T("分享 / 导入", "Share / import") };
        exchange.Click += ProfileExchange_Click; PresetLibraryPanel.Children.Add(exchange);
        var rows = new StackPanel { Spacing = 12 }; PresetLibraryPanel.Children.Add(rows);
        void Render()
        {
            libraryModel = (model.SelectedItem as ModelGroupOption)?.Id ?? ""; librarySearch = search.Text;
            rows.Children.Clear();
            foreach (var group in state.MonitorPresets.Where(p => PresetMatches(p, libraryModel, librarySearch)).GroupBy(p => MonitorPresetLibrary.GroupKey(p.Monitor)))
            {
                var content = new StackPanel { Spacing = 8 };
                var deleteGroup = new Button { Content = T("删除分组", "Delete group") };
                deleteGroup.Click += async (_, _) => await DeleteMonitorPresetsAsync(state.MonitorPresets.Where(p => MonitorPresetLibrary.GroupKey(p.Monitor) == group.Key).ToArray());
                content.Children.Add(deleteGroup);
                foreach (var preset in group)
                {
                    var body = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
                    body.Children.Add(new TextBlock { Text = preset.Name, FontSize = 16, TextWrapping = TextWrapping.Wrap });
                    body.Children.Add(Empty(string.Join(" · ", new[] { preset.Scenario, preset.Summary, string.Join(", ", preset.Applications) }.Where(x => x.Length > 0))));
                    body.Children.Add(Empty(F("{0} 项参数", "{0} parameters", preset.Values.Count)));
                    var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    var use = new Button { Content = T("应用到屏幕…", "Apply to display…"), IsEnabled = displayDevices.Count > 0 };
                    use.Click += async (_, _) =>
                    {
                        try
                        {
                            var picker = new ComboBox { ItemsSource = displayDevices, DisplayMemberPath = "DisplayName", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                            if (await new ContentDialog { Title = T("选择目标显示器", "Choose target display"), Content = picker, PrimaryButtonText = T("继续", "Continue"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync() == ContentDialogResult.Primary && picker.SelectedItem is MonitorDevice target)
                                await ApplyMonitorPresetAsync(target, preset);
                        }
                        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
                    };
                    var edit = new Button { Content = T("信息 / 标签", "Info / tags") };
                    edit.Click += async (_, _) => await EditMonitorPresetAsync(preset);
                    var remove = new Button { Content = T("删除", "Delete") };
                    remove.Click += async (_, _) => await DeleteMonitorPresetsAsync(new[] { preset });
                    actions.Children.Add(use); actions.Children.Add(edit); actions.Children.Add(remove); body.Children.Add(actions); content.Children.Add(Card(body));
                }
                rows.Children.Add(new Expander { Header = MonitorPresetLibrary.GroupName(group.First().Monitor), IsExpanded = true, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            if (rows.Children.Count == 0) rows.Children.Add(Empty(T("暂无显示器配置。可从屏幕保存当前参数，或导入分享文件。", "No monitor presets. Save current display settings or import a shared file.")));
        }
        model.SelectionChanged += (_, _) => Render(); search.TextChanged += (_, _) => Render(); Render();
    }
    private async Task ChooseMonitorPresetAsync(MonitorDevice device)
    {
        try
        {
            if (refreshing || closed || !displayDevices.Contains(device)) return;
            var groups = LibraryGroups();
            var model = new ComboBox { Header = T("显示器型号", "Monitor model"), ItemsSource = groups, DisplayMemberPath = "Name", SelectedItem = groups.FirstOrDefault(g => g.Id == device.ModelId) ?? groups[0], HorizontalAlignment = HorizontalAlignment.Stretch };
            var search = new TextBox { PlaceholderText = T("搜索名称、场景或应用", "Search name, scenario or app") };
            var list = new ListView { DisplayMemberPath = "Name", SelectionMode = ListViewSelectionMode.Single, MaxHeight = 220 };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var body = new StackPanel { Spacing = 10, MinWidth = 340 }; body.Children.Add(model); body.Children.Add(search); body.Children.Add(list); body.Children.Add(preview);
            var dialog = new ContentDialog { Title = MonitorTitle(device) + " · " + T("选用配置", "Choose preset"), Content = body, PrimaryButtonText = T("应用", "Apply"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
            void Preview()
            {
                dialog.IsPrimaryButtonEnabled = list.SelectedItem is MonitorPreset;
                if (list.SelectedItem is not MonitorPreset preset) { preview.Text = T("暂无显示器配置。可从屏幕保存当前参数，或导入分享文件。", "No monitor presets. Save current display settings or import a shared file."); return; }
                var plan = MonitorPresetLibrary.Plan(preset, device);
                preview.Text = F("可应用 {0} 项，跳过 {1} 项。", "{0} applicable; {1} skipped.", plan.Applicable.Count, plan.Skipped.Count) + "\n" + (MonitorPresetLibrary.SameModel(preset, device) ? "" : T("跨型号使用后，可另存到当前型号组。", "After cross-model use, save a copy in this model's group."));
                dialog.IsPrimaryButtonEnabled = plan.Applicable.Count > 0;
            }
            void Filter()
            {
                list.ItemsSource = state.MonitorPresets.Where(p => PresetMatches(p, (model.SelectedItem as ModelGroupOption)?.Id ?? "", search.Text)).ToArray();
                list.SelectedIndex = ((IEnumerable<MonitorPreset>)list.ItemsSource).Any() ? 0 : -1; Preview();
            }
            model.SelectionChanged += (_, _) => Filter(); search.TextChanged += (_, _) => Filter(); list.SelectionChanged += (_, _) => Preview(); Filter();
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is MonitorPreset selected) await ApplyMonitorPresetAsync(device, selected);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async Task ApplyMonitorPresetAsync(MonitorDevice device, MonitorPreset preset, bool offerCopy = true)
    {
        if (closed || refreshing || applyingProfile || !displayDevices.Contains(device)) return;
        var version = generation; var applied = 0; var message = ""; applyingProfile = true;
        try
        {
            await WaitForWritesAsync();
            var plan = MonitorPresetLibrary.Plan(preset, device);
            var inputs = plan.Applicable.Where(x => x.Channel.PropertyKey == "input" && x.Channel.Value != x.Value).Select(x => x.Channel).ToArray();
            if (inputs.Length > 0 && !await ConfirmMonitorChangeAsync(inputs[0], inputs)) return;
            await gate.WaitAsync();
            try
            {
                if (closed || version != generation) return;
                var errors = await Task.Run(() =>
                {
                    var result = new List<string>();
                    foreach (var item in plan.Applicable)
                    {
                        if (item.Channel.PropertyKey == "input" && item.Channel.Value == item.Value) { applied++; continue; }
                        var failures = ControlOperations.Apply(new[] { item.Channel }, item.Value); result.AddRange(failures); if (failures.Count == 0) applied++;
                    }
                    return result;
                });
                if (closed || version != generation) return;
                if (applied > 0)
                {
                    state.ActiveMonitorPresets[device.Id] = preset.Id;
                    if (MonitorPresetLibrary.SameModel(preset, device) && preset.Brightness is { } mapping)
                    { mapping.Validate(); device.Preference.Brightness = mapping.Copy(); BindBrightnessMapping(device); preferences?.Save(); }
                    MarkProfileModified(); SaveState(); SynchronizeValues(); RenderMonitorControls(generation);
                }
                message = F("可应用 {0} 项，跳过 {1} 项。", "{0} applicable; {1} skipped.", applied, plan.Skipped.Count);
                if (plan.Skipped.Count > 0) message += "\n" + string.Join(", ", plan.Skipped.Select(k => VcpCatalog.Find(k)?.Name ?? k));
                if (errors.Count > 0) message += "\n" + string.Join("; ", errors);
                ShowStatus(message, errors.Count > 0 || plan.Skipped.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { applyingProfile = false; }
        if (offerCopy && applied > 0 && !closed && version == generation && !MonitorPresetLibrary.SameModel(preset, device))
        {
            try
            {
                if (await new ContentDialog { Title = T("另存为当前型号配置", "Save as this model's preset"), Content = message, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("稍后", "Later"), XamlRoot = Root.XamlRoot }.ShowAsync() == ContentDialogResult.Primary)
                    await SaveMonitorPresetAsync(device, preset);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        }
    }
    private async Task SaveMonitorPresetAsync(MonitorDevice device, MonitorPreset? source = null)
    {
        try
        {
            if (closed || refreshing || !displayDevices.Contains(device)) return;
            var descriptor = CaptureMonitorMetadata()[device.Id];
            var scenario = new TextBox { Header = T("场景 / 应用", "Scenario / app"), Text = source?.Scenario ?? "", MaxLength = 80 };
            var summary = new TextBox { Header = T("短简介", "Short description"), Text = source?.Summary ?? T("当前参数", "Current settings"), MaxLength = 160 };
            var apps = new TextBox { Header = T("适用应用 / 游戏（逗号分隔）", "Apps / games (comma separated)"), Text = string.Join(", ", source?.Applications ?? new()), MaxLength = 1500 };
            var name = new TextBox { Header = T("配置名称", "Profile name"), Text = MonitorPresetLibrary.SuggestedName(descriptor, scenario.Text, summary.Text), MaxLength = 80 };
            var lastSuggestion = name.Text;
            void Suggest()
            {
                var suggestion = MonitorPresetLibrary.SuggestedName(descriptor, scenario.Text.Length > 0 ? scenario.Text : apps.Text.Split(',')[0], summary.Text);
                if (name.Text == lastSuggestion) name.Text = suggestion; lastSuggestion = suggestion;
            }
            scenario.TextChanged += (_, _) => Suggest(); summary.TextChanged += (_, _) => Suggest(); apps.TextChanged += (_, _) => Suggest();
            var body = new StackPanel { Spacing = 10 }; body.Children.Add(Empty(MonitorPresetLibrary.GroupName(descriptor))); body.Children.Add(scenario); body.Children.Add(summary); body.Children.Add(apps); body.Children.Add(name);
            var dialog = new ContentDialog { Title = T("保存显示器配置", "Save monitor preset"), Content = body, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed || refreshing || !displayDevices.Contains(device)) return;
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException(T("名称为空或在当前分组中重复。", "The name is empty or already exists in this group."));
            await WaitForWritesAsync(); await gate.WaitAsync();
            try
            {
                if (closed || refreshing || !displayDevices.Contains(device)) return;
                var preset = MonitorPresetLibrary.Capture(device, scenario.Text.Trim(), summary.Text.Trim(), ParseApplications(apps.Text));
                if (preset.Values.Count == 0) throw new InvalidOperationException(T("没有可保存的控制项。", "No controls are available to save."));
                preset.Name = ProfileGroups.UniqueName(name.Text.Trim(), state.MonitorPresets.Where(p => MonitorPresetLibrary.GroupKey(p.Monitor) == MonitorPresetLibrary.GroupKey(preset.Monitor)).Select(p => p.Name));
                state.MonitorPresets.Add(preset); var previous = state.ActiveMonitorPresets.GetValueOrDefault(device.Id); state.ActiveMonitorPresets[device.Id] = preset.Id;
                if (!SaveState()) { state.MonitorPresets.Remove(preset); if (previous is null) state.ActiveMonitorPresets.Remove(device.Id); else state.ActiveMonitorPresets[device.Id] = previous; return; }
                MarkProfileModified(); BuildPresetLibrary(); RenderMonitorControls(generation);
                ShowStatus(T("配置已保存。", "Profile saved."), InfoBarSeverity.Success);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async Task EditMonitorPresetAsync(MonitorPreset preset)
    {
        try
        {
            var name = new TextBox { Header = T("配置名称", "Profile name"), Text = preset.Name, MaxLength = 80 };
            var scene = new TextBox { Header = T("场景 / 应用", "Scenario / app"), Text = preset.Scenario, MaxLength = 80 };
            var summary = new TextBox { Header = T("短简介", "Short description"), Text = preset.Summary, MaxLength = 160 };
            var apps = new TextBox { Header = T("适用应用 / 游戏（逗号分隔）", "Apps / games (comma separated)"), Text = string.Join(", ", preset.Applications), MaxLength = 1500 };
            var body = new StackPanel { Spacing = 10 }; body.Children.Add(name); body.Children.Add(scene); body.Children.Add(summary); body.Children.Add(apps);
            if (await new ContentDialog { Title = T("配置信息", "Profile information"), Content = body, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            var applications = ParseApplications(apps.Text);
            var old = (preset.Name, preset.Scenario, preset.Summary, preset.Applications);
            preset.Name = ProfileGroups.UniqueName(name.Text.Trim(), state.MonitorPresets.Where(p => p.Id != preset.Id && MonitorPresetLibrary.GroupKey(p.Monitor) == MonitorPresetLibrary.GroupKey(preset.Monitor)).Select(p => p.Name));
            preset.Scenario = scene.Text.Trim(); preset.Summary = summary.Text.Trim(); preset.Applications = applications;
            if (!SaveState()) (preset.Name, preset.Scenario, preset.Summary, preset.Applications) = old;
            BuildPresetLibrary(); RenderMonitorControls(generation);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private ControlProfile CaptureGlobalProfile(string name, List<string> applications)
    {
        var previous = state.Profiles.FirstOrDefault(p => p.Id == state.SelectedProfileId);
        var profile = new ControlProfile { Name = name, Applications = applications };
        if (previous is not null)
        {
            profile.Values = previous.Values.ToDictionary(p => p.Key, p => new SavedValue { Value = p.Value.Value, Muted = p.Value.Muted });
            profile.Monitors = previous.Monitors.ToDictionary(p => p.Key, p => p.Value.Copy());
            profile.BrightnessMappings = previous.BrightnessMappings.ToDictionary(p => p.Key, p => p.Value.Copy());
            profile.MonitorPresetIds = new(previous.MonitorPresetIds);
        }
        ProfileUpdates.Merge(profile, CaptureProfile(), CaptureMonitorMetadata(), CaptureMappings());
        return profile;
    }
    private async Task DeleteMonitorPresetsAsync(IReadOnlyList<MonitorPreset> presets)
    {
        try
        {
            if (await new ContentDialog { Title = T("删除配置", "Delete profile"), Content = string.Join("\n", presets.Take(8).Select(p => p.Name)) + "\n\n" + T("总配置中的快照会保留。", "Snapshots in global profiles will be retained."), PrimaryButtonText = T("删除", "Delete"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot }.ShowAsync() != ContentDialogResult.Primary) return;
            var old = state.MonitorPresets; state.MonitorPresets = old.Where(p => !presets.Contains(p)).ToList();
            if (!SaveState()) state.MonitorPresets = old;
            BuildPresetLibrary(); RenderMonitorControls(generation);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
}
