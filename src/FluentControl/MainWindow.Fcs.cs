using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using System.Diagnostics;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    // Management belongs to the separately running Gateway, never to FC subscriptions.
    private void BuildFcsPage()
    {
        SettingsPanel.Children.Clear(); settingsSections.Clear();
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "FCS Gateway", FontSize = 24 });
        body.Children.Add(new TextBlock { Text = T("FCS 统一管理设备、配网、插件与 USB 重装；关闭 FC 后仍可独立使用。", "FCS manages devices, Wi-Fi, plugins and USB recovery independently of FC."), TextWrapping = TextWrapping.Wrap });
        AddFcsManagerButton(body);
        BuildGatewaySettings(body);
        SettingsPanel.Children.Add(new ScrollViewer { Content = body });
    }
    private void AddFcsManagerButton(StackPanel body)
    {
        var button = new Button { Content = T("打开 FCS 设备管理", "Open FCS device manager"), IsEnabled = !uiTest };
        AutomationProperties.SetAutomationId(button, "open-fcs-manager");
        button.Click += (_, _) =>
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "FluentControl.FcsGateway.exe");
                Process.Start(new ProcessStartInfo(path, "--manage") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { ShowStatus(T("无法打开 FCS，请检查安装是否完整。", "Could not open FCS. Check the installation."), InfoBarSeverity.Warning); }
        };
        body.Children.Add(button);
    }
    private void BuildFcsSubscriptions(StackPanel section)
    {
        AddFcsManagerButton(section);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var list = new StackPanel { Spacing = 8 };
        var refresh = new Button { Content = T("刷新可订阅设备", "Refresh available devices"), IsEnabled = !uiTest };
        section.Children.Add(refresh); section.Children.Add(status); section.Children.Add(list);
        async Task Load()
        {
            refresh.IsEnabled = false;
            try
            {
                var devices = await FcsGatewayClient.ListAsync(false, sensorLifetime.Token);
                if (closed || list.XamlRoot is null) return;
                list.Children.Clear();
                foreach (var d in devices)
                {
                    var check = new CheckBox { Content = d.DeviceId + " · " + d.Status + " · " +
                        string.Join(" / ", new[] { d.UsbPort is null ? null : "USB", string.IsNullOrEmpty(d.Ip) ? null : "Wi-Fi" }.Where(x => x is not null)),
                        IsChecked = state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == d.DeviceId) };
                    check.Click += async (_, _) =>
                    {
                        check.IsEnabled = false;
                        var revision = lightSourceListRevision;
                        try
                        {
                            var existing = state.Settings.Adaptive.LightSources.FirstOrDefault(s => s.DeviceId == d.DeviceId);
                            if (check.IsChecked == true && existing is null)
                            {
                                if (state.Settings.Adaptive.LightSources.Count >= 32) throw new InvalidDataException();
                                var sensors = await FcsGatewayClient.NodeAsync(d.DeviceId, "sensors.list", cancellation: sensorLifetime.Token);
                                var found = sensors.GetProperty("sensors").EnumerateArray().FirstOrDefault(s =>
                                    s.GetProperty("signals").EnumerateArray().Any(v => v.GetProperty("id").GetString() == "illuminance" &&
                                        v.GetProperty("unit").GetString() == "lx" && v.GetProperty("readable").GetBoolean()));
                                if (found.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidDataException();
                                var source = new LightSourceSubscription { Name = d.DeviceId, DeviceId = d.DeviceId,
                                    Url = "fc-usb://auto", SensorId = found.GetProperty("id").GetString()!, SignalId = "illuminance" };
                                await FcsGatewayClient.ReadAutoAsync(source, sensorLifetime.Token);
                                if (closed || revision != lightSourceListRevision || check.XamlRoot is null) return;
                                if (!state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == d.DeviceId)) AddSensorSubscription(source);
                            }
                            else if (check.IsChecked != true && existing is not null)
                            {
                                RemoveLightSource(existing);
                            }
                            if (!closed) BuildSettings();
                        }
                        catch { check.IsChecked = state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == d.DeviceId);
                            status.Text = T("无法订阅：请检查设备连接及可读取的光照信号。", "Cannot subscribe. Check the connection and readable illuminance signal."); }
                        finally { check.IsEnabled = true; }
                    };
                    list.Children.Add(check);
                }
                status.Text = T("勾选后订阅；取消勾选不删除设备。自动亮度需在预设页单独开启。", "Select to subscribe; deselecting does not remove the device. Enable automatic brightness separately on the presets page.");
            }
            catch { if (!closed) status.Text = T("FCS 暂不可用，请打开设备管理检查。", "FCS is unavailable. Open device manager to check."); }
            finally { if (!closed) refresh.IsEnabled = !uiTest; }
        }
        refresh.Click += async (_, _) => await Load();
        list.Loaded += async (_, _) => { if (!uiTest) await Load(); };
    }
}
