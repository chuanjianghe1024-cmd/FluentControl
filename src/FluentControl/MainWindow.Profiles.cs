using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private string applicationFilter = "", modelFilter = "", brandFilter = "";
    private bool filterAllGroups = true;
    private bool HasProfileFilters => applicationFilter.Length + modelFilter.Length + brandFilter.Length > 0;
    private List<ControlProfile> VisibleProfiles() => (HasProfileFilters && filterAllGroups ? state.Profiles : ProfileGroups.Current(state))
        .Where(p => ProfileGroups.Matches(p, applicationFilter, modelFilter, brandFilter)).ToList();
    private static List<string> ParseApplications(string text)
    {
        var result = text.Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (result.Count > 32 || result.Any(x => x.Length > 80)) throw new ArgumentException(T("最多 32 个应用，每个名称不超过 80 字符。", "Use up to 32 apps, with at most 80 characters per name."));
        return result;
    }
    private Dictionary<string, MonitorDescriptor> CaptureMonitorMetadata() => displayDevices.ToDictionary(d => d.Id, d => new MonitorDescriptor { ModelId = d.ModelId, ModelName = d.Model, DisplayName = d.DisplayName, Brand = ModelIdentity.Brand(d.ModelId) });
    private void RecordMonitorMetadata(bool renamed = false)
    {
        foreach (var pair in CaptureMonitorMetadata())
        {
            state.KnownMonitors[pair.Key] = pair.Value;
            foreach (var profile in state.Profiles.Where(p => p.Values.Keys.Any(k => ProfileGroups.TryMonitorKey(k, out var id, out _) && id == pair.Key)))
            {
                if (!profile.Monitors.ContainsKey(pair.Key)) profile.Monitors[pair.Key] = pair.Value.Copy();
                else if (renamed) profile.Monitors[pair.Key].DisplayName = pair.Value.DisplayName;
            }
        }
        SaveState(); RefreshProfiles();
    }
    private async void GroupPicker_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (rebuildingProfiles || GroupPicker.SelectedItem is not ProfileGroup group) return;
        await SelectGroupAsync(group.Id);
    }
    private async Task SelectGroupAsync(string groupId)
    {
        if (closed || state.SelectedGroupId == groupId) return;
        if (refreshing || applyingProfile) { RefreshProfiles(); return; }
        state.SelectedGroupId = groupId; state.SelectedProfileId = null; profileDirty = true;
        applicationFilter = modelFilter = brandFilter = "";
        var first = ProfileGroups.Current(state).FirstOrDefault();
        SaveState(); RefreshProfiles();
        if (first is not null) await ApplyProfileAsync(first);
    }
    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Header = T("分组名称", "Group name"), MaxLength = 80 };
        var dialog = new ContentDialog { Title = T("新建分组", "New group"), Content = input, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var group = new ProfileGroup { Name = ProfileGroups.UniqueName(input.Text.Trim(), state.Groups.Select(g => g.DisplayName)) };
            state.Groups.Add(group); state.SelectedGroupId = group.Id; state.SelectedProfileId = null;
            applicationFilter = modelFilter = brandFilter = ""; SaveState(); RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = state.Groups.FirstOrDefault(g => g.Id == state.SelectedGroupId);
        if (group is null || group.Id == ProfileGroup.LocalId) return;
        try
        {
            var count = state.Profiles.Count(p => p.GroupId == group.Id);
            var result = await new ContentDialog
            {
                Title = T("删除分组", "Delete group"),
                Content = F("删除分组「{0}」及其中的 {1} 个配置？此操作无法撤销。", "Delete group '{0}' and its {1} profiles? This cannot be undone.", group.DisplayName, count),
                PrimaryButtonText = T("删除", "Delete"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot
            }.ShowAsync();
            if (result != ContentDialogResult.Primary || closed) return;
            var groups = state.Groups.ToList(); var profiles = state.Profiles.ToList();
            var selectedGroup = state.SelectedGroupId; var selectedProfile = state.SelectedProfileId;
            if (!ProfileGroups.Remove(state, group.Id)) return;
            if (!SaveState()) { state.Groups = groups; state.Profiles = profiles; state.SelectedGroupId = selectedGroup; state.SelectedProfileId = selectedProfile; }
            else ShowStatus(T("分组已删除。", "Group deleted."), InfoBarSeverity.Success);
            RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async void FilterProfiles_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(Empty(T("可只填一项；多项条件同时满足。留空表示不限。", "Use any one field, or combine fields with AND. Empty fields match all.")));
        var apps = new TextBox { Header = T("应用 / 游戏", "App / game"), Text = applicationFilter };
        var model = new TextBox { Header = T("显示器型号或名称", "Monitor model or name"), Text = modelFilter };
        var brand = new TextBox { Header = T("品牌", "Brand"), Text = brandFilter };
        var all = new CheckBox { Content = T("搜索所有分组", "Search all groups"), IsChecked = filterAllGroups };
        panel.Children.Add(apps); panel.Children.Add(model); panel.Children.Add(brand); panel.Children.Add(all);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(preview);
        void Count()
        {
            var source = all.IsChecked == true ? state.Profiles : ProfileGroups.Current(state);
            preview.Text = F("筛选结果：{0} 个配置", "Filter results: {0} profiles", source.Count(p => ProfileGroups.Matches(p, apps.Text, model.Text, brand.Text)));
        }
        foreach (var box in new[] { apps, model, brand }) box.TextChanged += (_, _) => Count();
        all.Checked += (_, _) => Count(); all.Unchecked += (_, _) => Count(); Count();
        try
        {
            var result = await new ContentDialog { Title = T("筛选配置", "Filter profiles"), Content = panel, PrimaryButtonText = T("应用筛选", "Apply filters"), SecondaryButtonText = T("清除筛选", "Clear filters"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync();
            if (result == ContentDialogResult.None) return;
            applicationFilter = result == ContentDialogResult.Primary ? apps.Text.Trim() : "";
            modelFilter = result == ContentDialogResult.Primary ? model.Text.Trim() : "";
            brandFilter = result == ContentDialogResult.Primary ? brand.Text.Trim() : "";
            filterAllGroups = all.IsChecked == true; RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not ControlProfile profile) return;
        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        var name = new TextBox { Header = T("配置名称", "Profile name"), Text = profile.Name, MaxLength = 80 };
        var apps = new TextBox { Header = T("适用应用 / 游戏（逗号分隔）", "Apps / games (comma separated)"), Text = string.Join(", ", profile.Applications), MaxLength = 1500 };
        panel.Children.Add(name); panel.Children.Add(apps);
        var fields = new List<(MonitorDescriptor Descriptor, TextBox Model, TextBox Brand, TextBox Alias)>();
        foreach (var descriptor in profile.Monitors.Values)
        {
            panel.Children.Add(new TextBlock { Text = descriptor.ModelId, FontSize = 16, Margin = new Thickness(0, 8, 0, 0) });
            var model = new TextBox { Header = T("可读型号名称", "Readable model name"), Text = descriptor.ModelName, MaxLength = 128 };
            var brand = new TextBox { Header = T("品牌", "Brand"), Text = descriptor.Brand, MaxLength = 80 };
            var alias = new TextBox { Header = T("屏幕别名", "Display alias"), Text = descriptor.DisplayName, MaxLength = 80 };
            panel.Children.Add(model); panel.Children.Add(brand); panel.Children.Add(alias); fields.Add((descriptor, model, brand, alias));
        }
        try
        {
            if (await new ContentDialog { Title = T("配置信息", "Profile information"), Content = new ScrollViewer { Content = panel, MaxHeight = 420 }, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync() != ContentDialogResult.Primary) return;
            var updatedName = name.Text.Trim();
            if (updatedName.Length == 0 || state.Profiles.Any(p => p.Id != profile.Id && p.GroupId == profile.GroupId && p.Name.Equals(updatedName, StringComparison.CurrentCultureIgnoreCase))) throw new ArgumentException(T("名称为空或在当前分组中重复。", "The name is empty or already exists in this group."));
            var applications = ParseApplications(apps.Text);
            profile.Name = updatedName; profile.Applications = applications;
            foreach (var field in fields) { field.Descriptor.ModelName = field.Model.Text.Trim(); field.Descriptor.Brand = field.Brand.Text.Trim(); field.Descriptor.DisplayName = field.Alias.Text.Trim(); }
            SaveState(); RefreshProfiles();
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
    }
}
