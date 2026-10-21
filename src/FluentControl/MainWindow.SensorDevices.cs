using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer sensorArrivalTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource sensorLifetime = new();
    private string? requestedSensorId;
    private void OpenSensorLink(string link)
    {
        var id = DeviceSignals.LinkIdentity(link);
        if (id is null || closed) return;
        requestedSensorId = id;
        AppWindow.Show(); Activate();
        Navigation.SelectedItem = Navigation.SettingsItem;
        selectedSettingsSection = "adaptive"; BuildSettings(); SelectSettingsSection("adaptive");
        ShowStatus(T("请选择设备并确认添加订阅：", "Select the device and confirm subscription: ") + id, InfoBarSeverity.Informational);
    }
    private string lastSensorPorts = "";
    private bool sensorArrivalBusy;
    private void StartSensorArrival()
    {
        if (uiTest) return;
        _ = DeviceLinkActivation.ListenAsync(link => DispatcherQueue.TryEnqueue(() => OpenSensorLink(link)), sensorLifetime.Token);
        ShellRoot.Loaded += (_, _) => { if (DeviceLinkActivation.Argument() is { } link) OpenSensorLink(link); };
        sensorArrivalTimer.Tick += async (_, _) =>
        {
            if (closed || sensorArrivalBusy || sensorSetupActive) return;
            sensorArrivalBusy = true;
            try
            {
                var ports = string.Join(",", LocalLightSensor.CandidatePorts("AUTO"));
                if (ports == lastSensorPorts) return;
                lastSensorPorts = ports;
                var devices = await LocalLightSensor.DiscoverAsync("auto", sensorLifetime.Token);
                if (!closed && devices.Any(d => !state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == d.Identity)))
                {
                    shell?.NotifySensor(T("发现 USB 信号设备，请到设置 → 自动调节添加订阅或配置设备。", "USB signal device found. Open Settings → Automatic adjustment to subscribe or configure."));
                    ShowStatus(T("发现 USB 信号设备，请到设置 → 自动调节添加订阅或配置设备。", "USB signal device found. Open Settings → Automatic adjustment to subscribe or configure."), InfoBarSeverity.Informational);
                }
            }
            catch (OperationCanceledException) { }
            catch { /* Device removal is normal. No credentials or serial frames in logs. */ }
            finally { sensorArrivalBusy = false; }
        };
        sensorArrivalTimer.Start();
    }
    private void BuildSensorDeviceSettings(StackPanel section, TextBox port)
    {
        var button = new Button { Content = T("Wi-Fi 配网向导", "Wi-Fi setup wizard"), IsEnabled = !uiTest };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false; sensorSetupActive = true;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var devices = await LocalLightSensor.DiscoverAsync(port.Text.Trim(), cancel.Token);
                if (devices.Count == 0) { ShowStatus(T("未发现兼容传感器，请关闭 Thonny 并检查主板程序和端口。", "No compatible sensor found. Close Thonny and check firmware and port."), InfoBarSeverity.Warning); return; }
                if (!closed) await ShowWifiWizardAsync(devices);
            }
            catch (OperationCanceledException) { }
            catch { if (!closed) ShowStatus(T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."), InfoBarSeverity.Warning); }
            finally { sensorSetupActive = false; if (!closed) button.IsEnabled = true; }
        };
        section.Children.Add(button);
        var advanced = new Button { Content = T("USB 设备设置与配网", "USB device setup and Wi-Fi"), IsEnabled = !uiTest };
        advanced.Click += async (_, _) =>
        {
            if (sensorSetupActive) return;
            advanced.IsEnabled = false; sensorSetupActive = true;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var devices = await LocalLightSensor.DiscoverAsync(port.Text.Trim(), cancel.Token);
                if (!closed && devices.Count > 0) await ShowSensorDeviceAsync(devices, port.Text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase));
                else if (!closed) ShowStatus(T("未发现兼容传感器，请关闭 Thonny 并检查主板程序和端口。", "No compatible sensor found. Close Thonny and check firmware and port."), InfoBarSeverity.Warning);
            }
            catch (OperationCanceledException) { }
            catch { if (!closed) ShowStatus(T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."), InfoBarSeverity.Warning); }
            finally { sensorSetupActive = false; if (!closed) advanced.IsEnabled = true; }
        };
        section.Children.Add(advanced);
        var network = new Button { Content = T("发现已绑定的局域网设备", "Discover bound LAN devices"), IsEnabled = !uiTest };
        network.Click += async (_, _) =>
        {
            network.IsEnabled = false;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                bool added = false;
                foreach (var found in await NetworkSignalSensor.DiscoverAsync(cancel.Token))
                {
                    if (closed) return;
                    var source = new LightSourceSubscription { Name = found.Id, DeviceId = found.Id, Url = found.Url, SignedDevice = true };
                    try { await NetworkSignalSensor.ReadAsync(lightClient, source, cancel.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { continue; }
                    AddSensorSubscription(source); added = true;
                }
                if (!closed) { if (added) BuildSettings(); else ShowStatus(T("请先通过 USB 配网并保存绑定，再搜索同一局域网。", "Set up Wi-Fi and save binding over USB first, then search the same LAN."), InfoBarSeverity.Informational); }
            }
            catch (OperationCanceledException) { }
            catch { if (!closed) ShowStatus(T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."), InfoBarSeverity.Warning); }
            finally { if (!closed) network.IsEnabled = true; }
        };
        section.Children.Add(network);
    }
    private void AddSensorSubscription(LightSourceSubscription incoming)
    {
        var s = state.Settings.Adaptive;
        var item = s.LightSources.FirstOrDefault(x => x.DeviceId == incoming.DeviceId);
        if (item is null) { if (s.LightSources.Count >= 32) throw new InvalidDataException(); item = incoming; s.LightSources.Add(item); }
        else { item.Url = incoming.Url; item.SignedDevice = incoming.SignedDevice; }
        s.ActiveSourceId = item.Id; s.RecordLightHistory = true; s.Enabled = false; SaveAdaptiveSettings();
    }
    private async Task ShowSensorDeviceAsync(IReadOnlyList<LocalLightDevice> devices, bool automaticPort)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = T("USB 直连拥有管理权限，无需绑定。无线读取需先保存绑定；本版本管理操作仅走 USB。", "USB grants management access without binding. Wireless reads require saved binding; management uses USB in this version."), TextWrapping = TextWrapping.Wrap });
        var picker = new ComboBox { ItemsSource = devices, DisplayMemberPath = "Identity", SelectedIndex = Math.Max(0, devices.ToList().FindIndex(d => d.Identity == requestedSensorId)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var ssid = new ComboBox { Header = "Wi-Fi SSID", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var password = new PasswordBox { Header = T("Wi-Fi 密码", "Wi-Fi password"), MaxLength = 64 };
        var link = new TextBox { Header = T("订阅链接", "Subscription link"), IsReadOnly = true };
        panel.Children.Add(picker); panel.Children.Add(status); panel.Children.Add(link); panel.Children.Add(ssid); panel.Children.Add(password);
        var operationButtons = new Dictionary<Button, string>();
        var actions = new StackPanel { Spacing = 8 }; panel.Children.Add(actions);
        bool busy = false;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token);
        async Task Execute(string operation, object? arguments = null)
        {
            if (busy || picker.SelectedItem is not LocalLightDevice device) return;
            busy = true; actions.IsHitTestVisible = false; picker.IsEnabled = false;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var result = await LocalLightSensor.ManageAsync(device, operation, arguments, cancel.Token);
                if (lifetime.IsCancellationRequested || closed) return;
                if (result.TryGetProperty("bindingKey", out var key) && key.ValueKind == JsonValueKind.String) DeviceKeyStore.Save(device.Identity, key.GetString()!);
                if (operation == "factory.reset") DeviceKeyStore.Remove(device.Identity);
                if (result.TryGetProperty("operations", out var supported))
                {
                    var names = supported.EnumerateArray().Select(x => x.GetString()).ToHashSet();
                    foreach (var entry in operationButtons) entry.Key.IsEnabled = names.Contains(entry.Value);
                }
                if (result.TryGetProperty("networks", out var networks)) ssid.ItemsSource = networks.EnumerateArray().Select(n => n.GetString()).ToArray();
                status.Text = result.TryGetProperty("state", out var stateValue) ? stateValue.GetString() : T("指令已接受，请刷新状态确认。", "Command accepted. Refresh status to confirm.");
                if (result.TryGetProperty("ip", out var ip) && ip.GetString() is { Length: > 0 } address) status.Text += " · " + address;
                if (operation == "wifi.configure") password.Password = "";
            }
            catch (OperationCanceledException) { if (!closed && !lifetime.IsCancellationRequested) status.Text = T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."); }
            catch { if (!closed) status.Text = T("设备操作失败或结果未确认，请重新读取状态。", "Device operation failed or is unconfirmed. Read status again."); }
            finally { busy = false; actions.IsHitTestVisible = true; picker.IsEnabled = true; }
        }
        void Action(string title, string operation, Func<object?>? args = null, bool confirm = false)
        {
            var b = new Button { Content = title };
            if (confirm)
            {
                var flyout = new Flyout(); var body = new StackPanel { Spacing = 8 };
                body.Children.Add(new TextBlock { Text = T("确认执行此设备操作？", "Confirm this device operation?") });
                var yes = new Button { Content = T("确认", "Confirm") };
                yes.Click += async (_, _) => { flyout.Hide(); await Execute(operation, args?.Invoke()); }; body.Children.Add(yes); flyout.Content = body; b.Flyout = flyout;
            }
            else b.Click += async (_, _) => await Execute(operation, args?.Invoke());
            operationButtons[b] = operation; actions.Children.Add(b);
        }
        Action(T("读取状态并保存无线绑定", "Read status and save wireless binding"), "status");
        Action(T("搜索 Wi-Fi", "Scan Wi-Fi"), "wifi.scan");
        Action(T("连接 Wi-Fi", "Connect Wi-Fi"), "wifi.configure", () => new { ssid = ssid.Text, password = password.Password });
        Action(T("取消本次配网", "Cancel Wi-Fi setup"), "wifi.cancel");
        Action(T("忘记 Wi-Fi", "Forget Wi-Fi"), "wifi.forget", confirm: true);
        Action(T("关闭无线功能", "Disable wireless"), "wireless.disable", confirm: true);
        Action(T("开启无线功能", "Enable wireless"), "wireless.enable");
        Action(T("撤销旧无线绑定", "Revoke old wireless binding"), "binding.rotate", confirm: true);
        Action(T("重启设备", "Restart device"), "reboot", confirm: true);
        Action(T("恢复出厂设置", "Factory reset"), "factory.reset", confirm: true);
        var subscribe = new Button { Content = T("添加 USB 订阅", "Add USB subscription") };
        subscribe.Click += (_, _) => { if (picker.SelectedItem is LocalLightDevice d) { AddSensorSubscription(new() { Name = d.Identity, DeviceId = d.Identity, Url = "fc-usb://" + (automaticPort ? "auto" : d.Port.ToLowerInvariant()) }); status.Text = T("订阅已添加，自动调节保持关闭。", "Subscription added; automatic adjustment remains off."); } };
        actions.Children.Add(subscribe);
        void UpdateLink() { if (picker.SelectedItem is LocalLightDevice d) link.Text = DeviceSignals.SubscriptionLink(d.Identity); status.Text = ""; password.Password = ""; }
        picker.SelectionChanged += (_, _) => UpdateLink(); UpdateLink();
        var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ShellRoot.RequestedTheme,
            Title = T("USB 设备设置与配网", "USB device setup and Wi-Fi"), Content = new ScrollViewer { Content = panel, MaxHeight = 520 }, CloseButtonText = T("关闭", "Close") };
        dialog.Opened += async (_, _) => await Execute("status");
        await dialog.ShowAsync(); lifetime.Cancel(); password.Password = "";
        if (!closed) BuildSettings();
    }
}
