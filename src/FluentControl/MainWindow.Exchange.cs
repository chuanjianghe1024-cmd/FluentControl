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
        var models = state.MonitorPresets.Select(p => p.Monitor).Where(m => ModelIdentity.IsValid(m.ModelId)).DistinctBy(m => m.ModelId).ToArray();
        var model = new ComboBox { Header = T("显示器型号", "Monitor model"), ItemsSource = new[] { T("全部型号", "All models") }.Concat(models.Select(m => m.ModelName + " · " + m.ModelId)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var exportOptions = new StackPanel { Spacing = 10 }; exportOptions.Children.Add(name); exportOptions.Children.Add(model);
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
                var profiles = state.MonitorPresets.Where(p => model.SelectedIndex == 0 || p.Monitor.ModelId == models[model.SelectedIndex - 1].ModelId).ToList();
                if (profiles.Count == 0) throw new InvalidOperationException(T("请先保存至少一个显示器配置。", "Save at least one monitor profile first."));
                var shared = MonitorPresetLibrary.Export(profiles, name.Text.Trim());
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
    private async Task ImportDisplaysAsync(SharedProfileBundle bundle, string suggestedGroup)
    {
        var imported = MonitorPresetLibrary.Import(bundle, state.MonitorPresets, suggestedGroup);
        var modelNames = imported.GroupBy(p => MonitorPresetLibrary.GroupKey(p.Monitor)).Select(g => MonitorPresetLibrary.GroupName(g.First().Monitor) + " · " + g.Count());
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Empty(T("按显示器型号合并到配置库；无需连接设备，导入不会改变硬件。相同配置去重，重名内容另存。", "Merge into the library by monitor model. No device connection is needed and importing does not change hardware. Identical presets are deduplicated; differing namesakes are kept.")));
        body.Children.Add(Empty(string.Join("\n", modelNames)));
        var dialog = new ContentDialog { Title = T("导入预览", "Import preview"), Content = new ScrollViewer { Content = body, MaxHeight = 400 }, PrimaryButtonText = T("导入全部配置", "Import all profiles"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
        state.MonitorPresets.AddRange(imported);
        if (!SaveState()) { state.MonitorPresets.RemoveAll(imported.Contains); return; }
        BuildPresetLibrary(); RenderMonitorControls(generation);
        Navigation.SelectedItem = Navigation.MenuItems[3];
        ShowStatus(F("已导入 {0} 个配置；选择配置后应用。", "Imported {0} profiles. Select a profile to apply it.", imported.Count), InfoBarSeverity.Success);
    }
}
