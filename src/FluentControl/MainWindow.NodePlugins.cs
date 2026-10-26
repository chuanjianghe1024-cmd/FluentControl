using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.Storage.Pickers;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void BuildNodePluginEntry(StackPanel section)
    {
        var open = new Button { Content = T("C3 插件与规则", "C3 plugins and rules"), IsEnabled = !uiTest };
        open.Click += async (_, _) =>
        {
            if (sensorSetupActive) return;
            sensorSetupActive = true; open.IsEnabled = false;
            try
            {
                var devices = await FcsGatewayClient.DiscoverUsbAsync("AUTO", sensorLifetime.Token);
                if (closed) return;
                if (devices.Count == 0) { ShowStatus(T("未发现兼容传感器，请关闭 Thonny 并检查主板程序和端口。", "No compatible sensor found. Close Thonny and check firmware and port."), InfoBarSeverity.Warning); return; }
                await ShowNodePluginsAsync(devices);
            }
            catch { if (!closed) ShowStatus(T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."), InfoBarSeverity.Warning); }
            finally { sensorSetupActive = false; if (!closed) open.IsEnabled = true; }
        };
        section.Children.Add(open);
    }
    private async Task ShowNodePluginsAsync(IReadOnlyList<LocalLightDevice> devices)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token);
        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        var device = new ComboBox { ItemsSource = devices, DisplayMemberPath = "Identity", SelectedIndex = 0 };
        var identity = new TextBox { Header = T("插件 ID", "Plugin ID"), Text = "wol", MaxLength = 48 };
        var settings = new TextBox { Header = T("插件参数（JSON）", "Plugin settings (JSON)"), AcceptsReturn = true, MaxLength = 1024,
            Text = "{\"mac\":\"02:00:00:00:00:01\",\"broadcast\":\"192.168.1.255\",\"port\":9}" };
        var status = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200 };
        panel.Children.Add(new TextBlock { Text = T("仅安装可信代码。安装后默认禁用；修改 MAC 和广播地址后再启用、测试。发送成功不代表电脑已开机。", "Install trusted code only. Plugins start disabled; set the MAC and broadcast address before enabling and testing. Sent does not mean the PC is awake."), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(device); panel.Children.Add(identity); panel.Children.Add(settings);
        var actions = new StackPanel { Spacing = 6 }; panel.Children.Add(actions); panel.Children.Add(status);
        bool busy = false;
        async Task<string?> PickAsync()
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            return (await picker.PickSingleFileAsync())?.Path;
        }
        void Add(string title, Func<string, CancellationToken, Task<JsonElement?>> operation)
        {
            var button = new Button { Content = title };
            button.Click += async (_, _) =>
            {
                if (busy || device.SelectedItem is not LocalLightDevice selected) return;
                busy = true; actions.IsHitTestVisible = false; device.IsEnabled = false;
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.CancelAfter(TimeSpan.FromMinutes(2));
                try { var result = await operation(selected.Identity, cancel.Token); if (!closed && !lifetime.IsCancellationRequested && result is { } frame) status.Text = frame.GetRawText(); }
                catch { if (!closed && !lifetime.IsCancellationRequested) status.Text = T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."); }
                finally { busy = false; actions.IsHitTestVisible = true; device.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
        Add(T("读取存储与插件状态", "Read storage and plugin status"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "plugin.list", null, ct));
        Add(T("从文件安装插件（USB）", "Install plugin from file (USB)"), async (id, ct) =>
        { var path = await PickAsync(); return path is null ? null : await FcsPluginClient.InstallAsync(id, path, ct); });
        Add(T("保存参数并启用插件", "Save settings and enable plugin"), async (id, ct) =>
        { using var json = JsonDocument.Parse(settings.Text); return await FcsPluginClient.RequestAsync(id, "plugin.configure", new { id = identity.Text.Trim(), enabled = true, settings = json.RootElement }, ct); });
        Add(T("禁用插件", "Disable plugin"), async (id, ct) =>
        {
            var pluginId = identity.Text.Trim();
            var current = await FcsPluginClient.RequestAsync(id, "plugin.list", null, ct);
            var saved = current.GetProperty("plugins").EnumerateArray().Single(p => p.GetProperty("id").GetString() == pluginId).GetProperty("settings");
            return await FcsPluginClient.RequestAsync(id, "plugin.configure", new { id = pluginId, enabled = false, settings = saved }, ct);
        });
        Add(T("执行一次插件动作", "Run plugin action once"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "plugin.run", new { id = identity.Text.Trim(), arguments = new { } }, ct));
        Add(T("从文件应用规则（USB）", "Apply rules from file (USB)"), async (id, ct) =>
        {
            var path = await PickAsync(); if (path is null) return null;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
            return await FcsPluginClient.RequestAsync(id, "rules.set", json.RootElement, ct);
        });
        Add(T("读取规则", "Read rules"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "rules.list", null, ct));
        Add(T("回滚插件并禁用", "Roll back and disable plugin"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "plugin.rollback", new { id = identity.Text.Trim() }, ct));
        Add(T("移除插件", "Remove plugin"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "plugin.remove", new { id = identity.Text.Trim() }, ct));
        Add(T("退出安全模式（保持插件禁用）", "Exit safe mode (keep plugins disabled)"), async (id, ct) => await FcsPluginClient.RequestAsync(id, "plugin.recover", null, ct));
        var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ShellRoot.RequestedTheme,
            Title = T("C3 插件与规则", "C3 plugins and rules"), Content = new ScrollViewer { Content = panel, MaxHeight = 580 }, CloseButtonText = T("关闭", "Close") };
        await dialog.ShowAsync(); lifetime.Cancel();
    }
}
