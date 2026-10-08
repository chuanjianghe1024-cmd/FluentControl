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
        var actions = new ComboBox { ItemsSource = new[] { T("导出当前显示器配置", "Export current display controls"), T("从文件导入", "Import from file"), T("从网站链接下载", "Download from web link") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("配置名称", "Profile name"), Text = state.Profiles.FirstOrDefault(p => p.Id == state.SelectedProfileId)?.Name ?? T("我的显示器配置", "My display profile"), MaxLength = 80 };
        var url = new TextBox { Header = T("配置 JSON 的 HTTPS 链接", "HTTPS link to profile JSON"), PlaceholderText = "https://…", Visibility = Visibility.Collapsed };
        actions.SelectionChanged += (_, _) => { name.Visibility = actions.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; url.Visibility = actions.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 }; content.Children.Add(actions); content.Children.Add(name); content.Children.Add(url);
        content.Children.Add(Empty(T("分享文件只包含型号、显示器参数和亮度映射，不包含设备序列号、音频设备或本机路径。导入后先选择目标屏幕，保存后手动应用。", "Shared files contain model IDs, monitor values and brightness mappings; no serial numbers, audio devices or local paths. Choose target displays before saving, then apply manually.")));
        content.Children.Add(Empty(T("网站上传：接口已预留，等待分享网站接入。", "Website upload: interface reserved until the sharing site is connected.")));
        try
        {
            var result = await new ContentDialog { Title = T("分享 / 导入", "Share / import"), Content = content, PrimaryButtonText = T("继续", "Continue"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot }.ShowAsync();
            if (result != ContentDialogResult.Primary || closed) return;
            if (actions.SelectedIndex == 0) { await ExportDisplaysAsync(name.Text); return; }
            SharedMonitorProfile shared;
            if (actions.SelectedIndex == 1)
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync(); if (file is null) return;
                if ((await file.GetBasicPropertiesAsync()).Size > ProfileExchange.MaxBytes) throw new InvalidDataException("Profile exceeds 1 MB.");
                shared = ProfileExchange.Parse(await FileIO.ReadTextAsync(file));
            }
            else
            {
                ProfileExchangeButton.IsEnabled = false;
                ShowStatus(T("正在下载配置…", "Downloading profile…"), InfoBarSeverity.Informational);
                shared = await new HttpMonitorProfileExchange().DownloadAsync(new Uri(url.Text.Trim()));
            }
            if (!closed) await ImportDisplaysAsync(shared);
        }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { if (!closed) ProfileExchangeButton.IsEnabled = true; }
    }
    private async Task ExportDisplaysAsync(string name)
    {
        await WaitForWritesAsync(); await gate.WaitAsync();
        string json;
        try
        {
            var shared = new SharedMonitorProfile { Name = name.Trim() };
            foreach (var device in displayDevices.Where(d => ModelIdentity.IsValid(d.ModelId)))
            {
                var values = device.Channels.Where(c => c.CanSave && !c.IsAction && ProfileExchange.IsShareable(c.PropertyKey)).ToDictionary(c => c.PropertyKey, c => c.Value);
                if (values.Count == 0) continue;
                shared.Monitors.Add(new() { Slot = "display-" + (shared.Monitors.Count + 1), ModelId = device.ModelId, Values = values, Brightness = device.Preference.Brightness.Copy() });
            }
            if (shared.Monitors.Count == 0) throw new InvalidOperationException(T("没有可分享的已识别型号与控制项。", "No identified models with shareable controls."));
            json = ProfileExchange.Serialize(shared);
        }
        finally { gate.Release(); }
        var picker = new FileSavePicker { SuggestedFileName = "display-profile.fluentcontrol" };
        picker.FileTypeChoices.Add("Fluent Control JSON", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        await FileIO.WriteTextAsync(file, json);
        ShowStatus(T("配置已导出。", "Profile exported."), InfoBarSeverity.Success);
    }
    private async Task ImportDisplaysAsync(SharedMonitorProfile shared)
    {
        ProfileExchange.Validate(shared);
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        var name = new TextBox { Header = T("配置名称", "Profile name"), Text = shared.Name, MaxLength = 80 }; content.Children.Add(name);
        content.Children.Add(Empty(T("只允许映射到相同型号；不支持的控制项会跳过。此步骤不会修改显示器。", "Map only to matching models. Unsupported controls will be skipped. This step does not change your displays.")));
        var selections = new List<(SharedMonitorSlot Slot, ComboBox Picker)>(); var used = new HashSet<string>();
        foreach (var slot in shared.Monitors)
        {
            var compatible = displayDevices.Where(d => d.ModelId == slot.ModelId).ToArray();
            var suggested = compatible.FirstOrDefault(d => !used.Contains(d.Id)); if (suggested is not null) used.Add(suggested.Id);
            var picker = new ComboBox { Header = slot.Slot + " · " + slot.ModelId, ItemsSource = compatible, DisplayMemberPath = "DisplayName", SelectedItem = suggested, PlaceholderText = T("没有匹配的显示器", "No matching display"), HorizontalAlignment = HorizontalAlignment.Stretch };
            selections.Add((slot, picker)); content.Children.Add(picker);
        }
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; content.Children.Add(summary);
        int CountSupported() => selections.Sum(x => x.Picker.SelectedItem is MonitorDevice d ? x.Slot.Values.Count(v => d.Channels.Any(c => c.PropertyKey == v.Key && ProfileExchange.CanApply(c, v.Value))) : 0);
        var dialog = new ContentDialog { Title = T("导入预览", "Import preview"), Content = new ScrollViewer { Content = content, MaxHeight = 450 }, PrimaryButtonText = T("保存为配置", "Save as profile"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
        void Preview()
        {
            var count = CountSupported(); var selected = selections.Select(x => x.Picker.SelectedItem).OfType<MonitorDevice>().ToArray();
            summary.Text = F("可导入 {0} 项；跳过 {1} 项", "{0} controls can be imported; {1} skipped", count, shared.Monitors.Sum(x => x.Values.Count) - count);
            dialog.IsPrimaryButtonEnabled = count > 0 && selected.Select(x => x.Id).Distinct().Count() == selected.Length && !string.IsNullOrWhiteSpace(name.Text);
        }
        foreach (var selection in selections) selection.Picker.SelectionChanged += (_, _) => Preview();
        name.TextChanged += (_, _) => Preview(); Preview();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
        if (state.Profiles.Any(p => p.Name.Equals(name.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))) throw new InvalidOperationException(T("配置名称已存在，可用“更新”覆盖。", "That name exists. Use Update to replace it."));
        var profile = new ControlProfile { Name = name.Text.Trim() };
        foreach (var selection in selections)
        {
            if (selection.Picker.SelectedItem is not MonitorDevice device) continue;
            foreach (var value in selection.Slot.Values)
            {
                var channel = device.Channels.FirstOrDefault(c => c.PropertyKey == value.Key);
                if (channel is not null && ProfileExchange.CanApply(channel, value.Value)) profile.Values[$"monitor/{Uri.EscapeDataString(device.Id)}/{value.Key}"] = new() { Value = value.Value };
            }
            if (selection.Slot.Brightness is { } mapping) profile.BrightnessMappings[device.Id] = mapping.Copy();
        }
        state.Profiles.Add(profile);
        if (!SaveState()) { state.Profiles.Remove(profile); return; }
        RefreshProfiles(); ShowStatus(T("已导入；选择配置后应用。", "Imported. Select the profile to apply it."), InfoBarSeverity.Success);
    }
}
