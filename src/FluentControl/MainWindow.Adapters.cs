using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task ShowAdapterManagerAsync(MonitorDevice device)
    {
        if (closed || refreshing || applyingProfile || monitorAdaptationDialog is not null || !displayDevices.Contains(device)) return;
        var version = generation; var refreshAfter = false; var collectAfter = false;
        CloseMonitorOsd(); desktopPanel?.SetUnlocked(false); await WaitForWritesAsync();
        if (closed || refreshing || generation != version || monitorAdaptationDialog is not null) return;
        var firmware = MonitorHardwareInfo.Capture(device)?.Firmware ?? "";
        var store = new MonitorAdapterStore(); var installed = store.Find(device.ModelId, firmware);
        using var lifetime = new CancellationTokenSource(); var open = true; var busy = false;
        var body = new StackPanel { Spacing = 12, MinWidth = 400 };
        body.Children.Add(Empty(device.Model + " · " + device.ModelId + " · " + T("固件版本", "Firmware version") + " " + (firmware.Length > 0 ? firmware : "—")));
        body.Children.Add(Empty(T("按型号和已验证固件精确匹配。安装后离线生效；移除后恢复通用控制。", "Match the exact model and verified firmware. Installed adapters work offline; removing one restores generic controls.")));
        var current = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = installed is null ? T("未安装匹配的适配包", "No matching adapter installed") : installed.Id + " · r" + installed.Revision };
        body.Children.Add(current);
        var query = new Button { Content = T("在线查找适配包", "Find adapters online"), IsEnabled = firmware.Length > 0 && ModelIdentity.IsValid(device.ModelId) };
        var choices = new ComboBox { DisplayMemberPath = "Id", HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var install = new Button { Content = T("安装并使用", "Install and use"), IsEnabled = false };
        var remove = new Button { Content = T("移除适配包", "Remove adapter"), IsEnabled = installed is not null };
        var collect = new Button { Content = T("采集 / 提交适配报告", "Collect / submit an adaptation report") };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Text = store.LoadError ?? "" };
        foreach (var item in new FrameworkElement[] { query, choices, description, install, remove, collect, status }) body.Children.Add(item);
        var dialog = new ContentDialog { Title = T("型号适配", "Model adapters"), Content = new ScrollViewer { Content = body, MaxHeight = 460 }, XamlRoot = Root.XamlRoot, CloseButtonText = T("关闭", "Close") };
        monitorAdaptationDialog = dialog;
        bool Active() => open && !closed && version == generation && ReferenceEquals(monitorAdaptationDialog, dialog);
        choices.SelectionChanged += (_, _) =>
        {
            var selected = choices.SelectedItem as MonitorAdapter;
            install.IsEnabled = !busy && selected is not null;
            description.Text = selected is null ? "" : $"r{selected.Revision} · {string.Join(", ", selected.FirmwareVersions)}\n{selected.Notes}\n{selected.Evidence}";
        };
        query.Click += async (_, _) =>
        {
            if (!Active() || busy) return;
            busy = true; query.IsEnabled = install.IsEnabled = remove.IsEnabled = false;
            status.Text = T("正在查找已审核适配包…", "Looking for reviewed adapters…");
            try
            {
                var found = await MonitorAdapterClient.FindAsync(device.ModelId, firmware, lifetime.Token);
                if (!Active()) return;
                choices.ItemsSource = found; choices.SelectedItem = found.FirstOrDefault();
                status.Text = found.Count == 0 ? T("暂无匹配适配包。可以采集报告并提交到网站，供后续适配。", "No matching adapter. Collect a report and submit it to the website for future adaptation.") :
                    F("找到 {0} 个匹配适配包。", "Found {0} matching adapters.", found.Count);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Active()) status.Text = ex.Message; }
            finally { busy = false; if (Active()) { query.IsEnabled = true; remove.IsEnabled = installed is not null; install.IsEnabled = choices.SelectedItem is MonitorAdapter; } }
        };
        install.Click += (_, _) =>
        {
            if (!Active() || busy || choices.SelectedItem is not MonitorAdapter adapter) return;
            try { store.Install(adapter, device.ModelId, firmware); refreshAfter = true; dialog.Hide(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        remove.Click += (_, _) =>
        {
            if (!Active() || busy || installed is null) return;
            try { store.Remove(installed.Id); refreshAfter = true; dialog.Hide(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        collect.Click += (_, _) => { if (!Active()) return; collectAfter = true; dialog.Hide(); };
        dialog.Closing += (_, _) => { open = false; lifetime.Cancel(); };
        try { await dialog.ShowAsync(); }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { open = false; lifetime.Cancel(); if (ReferenceEquals(monitorAdaptationDialog, dialog)) monitorAdaptationDialog = null; }
        if (closed || version != generation) return;
        if (refreshAfter) await RefreshAsync();
        else if (collectAfter) await ShowMonitorAdaptationAsync(device);
    }
}
