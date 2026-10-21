using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private bool sensorSetupActive;

    private async Task ShowWifiWizardAsync(IReadOnlyList<LocalLightDevice> devices,
        Func<string, object?, CancellationToken, Task<JsonElement>>? fixtureManage = null,
        Func<LightSourceSubscription, CancellationToken, Task<LightSample>>? fixtureRead = null,
        Action<ContentDialog>? fixtureOpened = null)
    {
        if (!uiTest && (fixtureManage is not null || fixtureRead is not null || fixtureOpened is not null)) throw new InvalidOperationException("Wi-Fi fixtures require UI test mode.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token);
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        var heading = new TextBlock { FontSize = 20, TextWrapping = TextWrapping.Wrap };
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var picker = new ComboBox { ItemsSource = devices, DisplayMemberPath = "Identity", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var ssid = new ComboBox { Header = "Wi-Fi SSID", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var password = new PasswordBox { Header = T("Wi-Fi 密码", "Wi-Fi password"), MaxLength = 64 };
        var openNetwork = new CheckBox { Content = T("此网络没有密码", "This network has no password") };
        var rescan = new Button { Content = T("搜索 Wi-Fi", "Scan Wi-Fi") };
        var progress = new ProgressRing { IsActive = false, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        foreach (var control in new UIElement[] { heading, help, picker, ssid, rescan, password, openNetwork, progress, message }) panel.Children.Add(control);
        var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ShellRoot.RequestedTheme,
            Title = T("Wi-Fi 配网向导", "Wi-Fi setup wizard"), Content = panel,
            PrimaryButtonText = T("下一步", "Next"), SecondaryButtonText = T("上一步", "Back"), CloseButtonText = T("取消", "Cancel") };
        int step = 0; bool busy = false, pending = false;
        LocalLightDevice? selected = null;
        LightSourceSubscription? verifiedSource = null;
        string networkName = "";
        void Render()
        {
            heading.Text = (step + 1) + "/6 · " + (step switch {
                0 => T("选择设备", "Choose a device"),
                1 => T("选择 Wi-Fi", "Choose Wi-Fi"),
                2 => T("输入密码", "Enter password"),
                3 => T("正在连接 Wi-Fi…", "Connecting to Wi-Fi…"),
                4 => T("验证无线读取", "Verify wireless readings"),
                _ => T("配网完成", "Wi-Fi setup complete") });
            help.Text = step switch {
                0 => T("保持 USB 连接并关闭 Thonny。下一步会检查设备并自动保存本机无线绑定。", "Keep USB connected and close Thonny. Next checks the device and saves its wireless binding on this PC."),
                1 => T("选择 2.4 GHz Wi-Fi，也可以直接输入隐藏网络名称。电脑应连接同一路由器。", "Choose a 2.4 GHz Wi-Fi network, or type a hidden network name. Connect the PC to the same router."),
                2 => networkName,
                3 => T("请保持 USB 连接。正在等待设备取得 IP 地址，无需手动刷新。", "Keep USB connected. Waiting for an IP address; no manual refresh is needed."),
                4 => T("设备已联网，正在检查电脑能否读取真实光照。失败时可点击下一步重新验证，无需重新输入密码。", "The device is online. Checking whether this PC can read real light data. If this fails, click Next to retry without entering the password again."),
                _ => T("点击完成启用 Wi-Fi 光照订阅，自动调光保持关闭。之后可改用充电器供电，遮住传感器检查读数变化。", "Click Finish to select the Wi-Fi light subscription; automatic brightness stays off. Then power from a charger and cover the sensor to check readings.") };
            picker.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            ssid.Visibility = rescan.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            password.Visibility = openNetwork.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            progress.IsActive = busy; progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            picker.IsEnabled = ssid.IsEnabled = rescan.IsEnabled = password.IsEnabled = openNetwork.IsEnabled = !busy;
            dialog.IsPrimaryButtonEnabled = !busy;
            dialog.IsSecondaryButtonEnabled = !busy && step is 1 or 2 or 4;
            dialog.PrimaryButtonText = step == 5 ? T("完成", "Finish") : T("下一步", "Next");
        }
        async Task<JsonElement> Manage(string operation, object? args, CancellationToken token)
        {
            if (selected is null) throw new InvalidOperationException();
            var result = fixtureManage is null ? await LocalLightSensor.ManageAsync(selected, operation, args, token) : await fixtureManage(operation, args, token);
            token.ThrowIfCancellationRequested();
            return result;
        }
        async Task Scan()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result = await Manage("wifi.scan", null, timeout.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            ssid.ItemsSource = result.TryGetProperty("networks", out var networks)
                ? networks.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray() : Array.Empty<string>();
        }
        async Task Verify()
        {
            if (verifiedSource is null) throw new InvalidOperationException();
            step = 4; Render();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var sample = fixtureRead is null ? await NetworkSignalSensor.ReadAsync(lightClient, verifiedSource, timeout.Token) : await fixtureRead(verifiedSource, timeout.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            message.Text = $"{sample.Lux:0.##} lux";
            step = 5;
        }
        async Task CancelPending()
        {
            if (!pending || selected is null || closed) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await Manage("wifi.cancel", null, timeout.Token);
            pending = false;
        }
        rescan.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true; message.Text = ""; Render();
            try { await Scan(); }
            catch { if (!lifetime.IsCancellationRequested) message.Text = T("搜索失败，请检查 USB 连接；也可直接输入网络名称。", "Scan failed. Check USB; you can also type the network name."); }
            finally { busy = false; if (!lifetime.IsCancellationRequested) Render(); }
        };
        dialog.SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            if (busy) return;
            step = step == 4 ? 1 : Math.Max(0, step - 1); message.Text = ""; password.Password = ""; Render();
        };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true;
            if (busy) return;
            var deferral = e.GetDeferral();
            busy = true; message.Text = ""; Render();
            try
            {
                if (pending) await CancelPending(); // Do not stack configuration attempts after an uncertain response.
                if (step == 0)
                {
                    selected = picker.SelectedItem as LocalLightDevice ?? throw new InvalidOperationException();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var status = await Manage("status", null, timeout.Token);
                    if (!status.GetProperty("operations").EnumerateArray().Any(x => x.GetString() == "wifi.configure"))
                    { message.Text = T("这台设备或固件不支持 Wi-Fi 配网，请使用 USB 订阅。", "This device or firmware does not support Wi-Fi setup. Use a USB subscription."); return; }
                    DeviceKeyStore.Save(selected.Identity, status.GetProperty("bindingKey").GetString()!);
                    if (status.TryGetProperty("state", out var stateValue) && stateValue.GetString() == "disabled") await Manage("wireless.enable", null, timeout.Token);
                    step = 1; Render();
                    try { await Scan(); }
                    catch (Exception) when (!lifetime.IsCancellationRequested) { message.Text = T("搜索失败，请检查 USB 连接；也可直接输入网络名称。", "Scan failed. Check USB; you can also type the network name."); }
                }
                else if (step == 1)
                {
                    networkName = ssid.Text;
                    if (Encoding.UTF8.GetByteCount(networkName) is < 1 or > 32)
                    { message.Text = T("请选择或输入有效的 Wi-Fi 名称。", "Choose or enter a valid Wi-Fi name."); return; }
                    step = 2;
                }
                else if (step == 2)
                {
                    if (Encoding.UTF8.GetByteCount(password.Password) > 64 || (password.Password.Length == 0 && openNetwork.IsChecked != true))
                    { message.Text = T("请输入密码；无密码网络请勾选对应选项。", "Enter the password, or select the option for a network without a password."); return; }
                    step = 3; Render(); pending = true;
                    // Older firmware can report the restored old network as connected after its 20 s timeout.
                    // Stop before that deadline rather than misreporting the requested network as successful.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(18));
                    var watch = Stopwatch.StartNew();
                    var result = await Manage("wifi.configure", new { ssid = networkName, password = openNetwork.IsChecked == true ? "" : password.Password }, timeout.Token);
                    password.Password = "";
                    while (true)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        if (watch.Elapsed >= TimeSpan.FromSeconds(18)) throw new TimeoutException();
                        if (WifiSetupNetwork.TryUrl(result) is { } url)
                        {
                            pending = false;
                            verifiedSource = new() { Name = selected!.Identity, DeviceId = selected.Identity, Url = url, SignedDevice = true };
                            break;
                        }
                        if (result.TryGetProperty("state", out var current) && current.GetString() == "failed_restored") throw new TimeoutException();
                        await Task.Delay(750, timeout.Token);
                        result = await Manage("status", null, timeout.Token);
                    }
                    await Verify();
                }
                else if (step == 4) await Verify();
                else if (step == 5 && verifiedSource is not null) { AddSensorSubscription(verifiedSource); dialog.Hide(); }
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                if (step == 3)
                {
                    step = 2;
                    message.Text = T("未确认连接成功。请检查 2.4 GHz 网络、密码及信号，再重试。", "Connection was not confirmed. Check the 2.4 GHz network, password and signal, then retry.");
                    try { await CancelPending(); }
                    catch { message.Text += " " + T("取消结果未确认，请保持 USB 连接后重试。", "Cancellation is unconfirmed. Keep USB connected and retry."); }
                }
                else message.Text = step == 4
                    ? T("设备已联网，但无线读数未通过验证。请让电脑和设备处于同一局域网，检查访客隔离或应用防火墙许可，然后点击下一步重试。", "The device is online, but wireless readings could not be verified. Check that the PC and device share a LAN, guest isolation and the app firewall permission, then click Next to retry.")
                    : T("设备未响应，请保持 USB 连接、关闭 Thonny，然后点击下一步重试。", "The device did not respond. Keep USB connected, close Thonny, then click Next to retry.");
            }
            catch (Exception) when (lifetime.IsCancellationRequested) { }
            finally { busy = false; if (!lifetime.IsCancellationRequested) Render(); deferral.Complete(); }
        };
        dialog.Closing += (_, _) => lifetime.Cancel();
        if (fixtureOpened is not null) dialog.Opened += (_, _) => fixtureOpened(dialog);
        Render();
        try { await dialog.ShowAsync(); }
        finally
        {
            lifetime.Cancel(); password.Password = "";
            try { await CancelPending(); }
            catch { if (!closed) ShowStatus(T("取消结果未确认，请保持 USB 连接后重试。", "Cancellation is unconfirmed. Keep USB connected and retry."), InfoBarSeverity.Warning); }
        }
        if (!closed) BuildSettings();
    }
    private async Task CheckWifiWizardAsync()
    {
        if (!uiTest) throw new InvalidOperationException();
        var device = new LocalLightDevice("COM999", "fixture-wifi-" + Guid.NewGuid().ToString("N"));
        bool configured = false; int readings = 0; Task? drive = null;
        Task<JsonElement> Manage(string operation, object? args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (operation == "wifi.configure") configured = true;
            return Task.FromResult(JsonSerializer.SerializeToElement(new {
                state = operation == "wifi.configure" ? "configuring" : configured ? "connected" : "idle",
                ip = configured ? "192.0.2.25" : "", bindingKey = new string('1', 64),
                operations = new[] { "status", "wifi.configure", "wifi.scan", "wifi.cancel" }, networks = new[] { "Fixture Wi-Fi" }
            }));
        }
        Task<LightSample> Read(LightSourceSubscription source, CancellationToken token)
        {
            if (source.DeviceId != device.Identity || !source.SignedDevice) throw new InvalidOperationException("Wizard lost device identity.");
            if (++readings == 1) throw new IOException("Simulated LAN isolation");
            return Task.FromResult(new LightSample { SourceId = device.Identity, Lux = 42, Valid = true, Timestamp = DateTimeOffset.UtcNow });
        }
        async Task Drive(ContentDialog dialog)
        {
            try
            {
                async Task Wait(int step)
                {
                    for (int i = 0; i < 160; i++)
                    {
                        dialog.UpdateLayout();
                        if (dialog.IsPrimaryButtonEnabled && Descendants<TextBlock>(dialog).Any(t => t.Text.StartsWith(step + "/6 · "))) return;
                        await Task.Delay(25);
                    }
                    throw new InvalidOperationException("Wi-Fi wizard did not reach step " + step);
                }
                void Next()
                {
                    var button = Descendants<Button>(dialog).Single(x => x.Name == "PrimaryButton");
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                await Wait(1); Next(); await Wait(2);
                Descendants<ComboBox>(dialog).Single(x => Equals(x.Header, "Wi-Fi SSID")).Text = "Fixture Wi-Fi";
                Next(); await Wait(3); Descendants<PasswordBox>(dialog).Single().Password = "fixture-only";
                Next(); await Wait(5);
                if (readings != 1) throw new InvalidOperationException("Wizard skipped real-read verification stage.");
                Next(); await Wait(6); Next();
            }
            catch { dialog.Hide(); throw; }
        }
        try
        {
            await ShowWifiWizardAsync(new[] { device }, Manage, Read, dialog => drive = Drive(dialog));
            if (drive is null) throw new InvalidOperationException("Wi-Fi wizard did not open.");
            await drive;
            if (readings != 2 || !state.Settings.Adaptive.LightSources.Any(x => x.DeviceId == device.Identity && x.SignedDevice) || state.Settings.Adaptive.Enabled)
                throw new InvalidOperationException("Wi-Fi wizard did not finish with a verified subscription and automation off.");
            StartupLog.Write("PASS: Wi-Fi wizard navigation, password step, asynchronous connection, failed LAN verification and retry, subscription without brightness writes (simulated transport).");
        }
        finally { DeviceKeyStore.Remove(device.Identity); }
    }

}
