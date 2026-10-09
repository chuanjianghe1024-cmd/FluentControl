using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private async void ProfileExchange_Click(object sender, RoutedEventArgs e)
    {
        var actions = new ComboBox { ItemsSource = new[] { T("批量导出配置", "Export profile bundle"), T("从文件导入", "Import from file"), T("从网站链接下载", "Download from web link") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("文件名称", "File name"), Text = T("我的显示器配置", "My display profiles"), MaxLength = 80 };
        var scope = new ComboBox { Header = T("导出范围", "Export scope"), ItemsSource = new[] { T("全部分组 / 全部配置", "All groups / all profiles"), T("当前分组 / 全部配置", "Current group / all profiles") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var models = state.Profiles.SelectMany(p => p.Monitors.Values).Concat(state.KnownMonitors.Values).Where(m => ModelIdentity.IsValid(m.ModelId)).DistinctBy(m => m.ModelId).ToArray();
        var model = new ComboBox { Header = T("显示器型号", "Monitor model"), ItemsSource = new[] { T("全部型号", "All models") }.Concat(models.Select(m => m.ModelName + " · " + m.ModelId)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var exportOptions = new StackPanel { Spacing = 10 }; exportOptions.Children.Add(name); exportOptions.Children.Add(scope); exportOptions.Children.Add(model);
        var url = new TextBox { Header = T("配置 JSON 的 HTTPS 链接", "HTTPS link to profile JSON"), PlaceholderText = "https://…", Visibility = Visibility.Collapsed };
        actions.SelectionChanged += (_, _) => { exportOptions.Visibility = actions.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; url.Visibility = actions.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 }; content.Children.Add(actions); content.Children.Add(exportOptions); content.Children.Add(url);
        content.Children.Add(Empty(T("导出已保存的显示器场景，包含屏幕别名、型号、品牌和应用标签；不含设备序列号、音频设备或本机路径。", "Exports saved monitor scenes with display aliases, models, brands and application tags; excludes serial numbers, audio devices and local paths.")));
        content.Children.Add(Empty(T("同时包含已读取的固件、MCCS 版本与控制能力，用于型号和参数匹配；能力信息不保证目标屏幕兼容。", "Includes observed firmware, MCCS version and control capabilities for model and parameter matching; capabilities do not guarantee target compatibility.")));
        content.Children.Add(Empty(T("网站上传：接口已预留，等待分享网站接入。", "Website upload: interface reserved until the sharing site is connected.")));
        try
        {
            var result = await new ContentDialog { Title = T("分享 / 导入", "Share / import"), Content = new ScrollViewer { Content = content, MaxHeight = 480 }, PrimaryButtonText = T("继续", "Continue"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync();
            if (result != ContentDialogResult.Primary || closed) return;
            if (actions.SelectedIndex == 0)
            {
                var profiles = scope.SelectedIndex == 0 ? state.Profiles : ProfileGroups.Current(state);
                if (profiles.Count == 0) throw new InvalidOperationException(T("请先保存至少一个显示器配置。", "Save at least one monitor profile first."));
                var shared = ProfileBundles.Export(state, profiles, name.Text.Trim(), model.SelectedIndex > 0 ? models[model.SelectedIndex - 1].ModelId : "");
                await ExportDisplaysAsync(shared); return;
            }
            SharedProfileBundle bundle; string groupName;
            if (actions.SelectedIndex == 1)
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync(); if (file is null) return;
                if ((await file.GetBasicPropertiesAsync()).Size > ProfileExchange.MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
                bundle = ProfileExchange.ParseBundle(await FileIO.ReadTextAsync(file));
                groupName = Path.GetFileNameWithoutExtension(file.Name);
                if (groupName.EndsWith(".fluentcontrol", StringComparison.OrdinalIgnoreCase)) groupName = groupName[..^14];
            }
            else
            {
                ProfileExchangeButton.IsEnabled = false;
                ShowStatus(T("正在下载配置…", "Downloading profile…"), InfoBarSeverity.Informational);
                bundle = await new HttpMonitorProfileExchange().DownloadAsync(new Uri(url.Text.Trim())); groupName = bundle.Name;
            }
            if (!closed) await ImportDisplaysAsync(bundle, groupName);
        }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { if (!closed) ProfileExchangeButton.IsEnabled = true; }
    }
    private async Task ExportDisplaysAsync(SharedProfileBundle bundle)
    {
        var json = ProfileExchange.SerializeBundle(bundle);
        var safeName = string.Concat(bundle.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var picker = new FileSavePicker { SuggestedFileName = safeName + ".fluentcontrol" };
        picker.FileTypeChoices.Add("Fluent Control JSON", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        await FileIO.WriteTextAsync(file, json);
        ShowStatus(F("已导出 {0} 个配置。", "Exported {0} profiles.", bundle.Groups.Sum(g => g.Profiles.Count)), InfoBarSeverity.Success);
    }
    private sealed record ImportTarget(string Id, string Name);
    private async Task ImportDisplaysAsync(SharedProfileBundle bundle, string suggestedGroup)
    {
        ProfileExchange.ValidateBundle(bundle);
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        var mode = new ComboBox { Header = T("导入方式", "Import mode"), ItemsSource = new[] { T("作为新分组", "Create a new group"), T("合并到当前分组", "Merge into current group") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("分组名称", "Group name"), Text = suggestedGroup.Length > 80 ? suggestedGroup[..80] : suggestedGroup, MaxLength = 80 }; content.Children.Add(mode); content.Children.Add(name);
        content.Children.Add(Empty(T("只映射相同型号。离线或暂不支持的参数仍会保留；重名配置自动编号，导入不会修改硬件。", "Map matching models only. Offline or unsupported values are retained. Duplicate names get a suffix; importing does not change hardware.")));
        var scenes = bundle.Groups.SelectMany(g => g.Profiles).ToArray();
        var slots = scenes.SelectMany(p => p.Monitors).DistinctBy(s => s.Slot).ToArray();
        var selections = new Dictionary<string, ComboBox>(); var used = new HashSet<string>();
        foreach (var slot in slots)
        {
            var targets = new List<ImportTarget> { new("", T("暂不绑定（保留配置）", "Leave unbound (keep values)")) };
            targets.AddRange(displayDevices.Where(d => ModelIdentity.IsValid(slot.ModelId) && d.ModelId == slot.ModelId).Select(d => new ImportTarget(d.Id, d.DisplayName + " · " + d.Model)));
            var suggested = targets.FirstOrDefault(d => d.Id.Length > 0 && !used.Contains(d.Id)) ?? targets[0]; used.Add(suggested.Id);
            var picker = new ComboBox { Header = string.Join(" · ", new[] { slot.DisplayName, slot.ModelName, slot.ModelId }.Where(s => s.Length > 0)), ItemsSource = targets, DisplayMemberPath = "Name", SelectedItem = suggested, HorizontalAlignment = HorizontalAlignment.Stretch };
            selections[slot.Slot] = picker; content.Children.Add(picker);
        }
        Dictionary<string, string> Bindings() => selections.Where(x => x.Value.SelectedItem is ImportTarget { Id.Length: > 0 }).ToDictionary(x => x.Key, x => ((ImportTarget)x.Value.SelectedItem).Id);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; content.Children.Add(summary);
        var dialog = new ContentDialog { Title = T("导入预览", "Import preview"), Content = new ScrollViewer { Content = content, MaxHeight = 480 }, PrimaryButtonText = T("导入全部配置", "Import all profiles"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
        void Preview()
        {
            name.IsEnabled = mode.SelectedIndex == 0;
            var bindings = Bindings(); var count = 0;
            foreach (var slot in scenes.SelectMany(p => p.Monitors))
                if (bindings.TryGetValue(slot.Slot, out var id) && displayDevices.FirstOrDefault(d => d.Id == id) is { } d)
                    count += slot.Values.Count(v => d.Channels.Any(c => c.PropertyKey == v.Key && ProfileExchange.CanApply(c, v.Value)));
            var total = scenes.Sum(p => p.Monitors.Sum(s => s.Values.Count));
            var duplicate = scenes.Any(p => { var ids = p.Monitors.Where(s => bindings.ContainsKey(s.Slot)).Select(s => bindings[s.Slot]).ToArray(); return ids.Distinct().Count() != ids.Length; });
            summary.Text = F("{0} 个配置；{1} 项当前可用，{2} 项保留待用。", "{0} profiles; {1} controls available now, {2} retained for later.", scenes.Length, count, total - count) + (duplicate ? "\n" + T("同一场景的多个屏幕不能绑定到同一设备。", "Multiple displays in one scene cannot use the same device.") : "");
            dialog.IsPrimaryButtonEnabled = !duplicate && (mode.SelectedIndex == 1 || !string.IsNullOrWhiteSpace(name.Text));
        }
        foreach (var picker in selections.Values) picker.SelectionChanged += (_, _) => Preview();
        mode.SelectionChanged += (_, _) => Preview(); name.TextChanged += (_, _) => Preview(); Preview();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
        var group = mode.SelectedIndex == 0 ? new ProfileGroup { Name = ProfileGroups.UniqueName(name.Text.Trim(), state.Groups.Select(g => g.DisplayName)) } : state.Groups.First(g => g.Id == state.SelectedGroupId);
        var profiles = ProfileBundles.Import(bundle, Bindings(), group.Id, state.Profiles.Where(p => p.GroupId == group.Id).Select(p => p.Name));
        var previousGroup = state.SelectedGroupId;
        if (mode.SelectedIndex == 0) state.Groups.Add(group);
        state.Profiles.AddRange(profiles); state.SelectedGroupId = group.Id;
        if (!SaveState()) { state.Profiles.RemoveAll(p => profiles.Contains(p)); if (mode.SelectedIndex == 0) state.Groups.Remove(group); state.SelectedGroupId = previousGroup; return; }
        applicationFilter = modelFilter = brandFilter = "";
        RefreshProfiles(); ShowStatus(F("已导入 {0} 个配置；选择配置后应用。", "Imported {0} profiles. Select a profile to apply it.", profiles.Count), InfoBarSeverity.Success);
    }
}
