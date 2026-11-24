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
        OpenAdaptivePage("devices");
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
                var devices = await FcsGatewayClient.ListAsync(false, sensorLifetime.Token);
                var identities = string.Join(",", devices.Select(d => d.DeviceId).Order());
                if (identities == lastSensorPorts) return;
                lastSensorPorts = identities;
                if (!closed && devices.Any(d => !state.Settings.Adaptive.LightSources.Any(s => s.DeviceId == d.DeviceId)))
                {
                    shell?.NotifySensor(T("FCS 发现新设备，请到自动调节中勾选订阅。", "FCS found a new device. Select it in Automatic adjustment to subscribe."));
                    ShowStatus(T("FCS 发现新设备，请到自动调节中勾选订阅。", "FCS found a new device. Select it in Automatic adjustment to subscribe."), InfoBarSeverity.Informational);
                }
            }
            catch (OperationCanceledException) { }
            catch { /* Device removal is normal. No credentials or serial frames in logs. */ }
            finally { sensorArrivalBusy = false; }
        };
        sensorArrivalTimer.Start();
        // Keep the Gateway's LAN heartbeat running even when the FCS page is closed.
        if (state.Settings.Adaptive.LightSources.Any(s => !string.IsNullOrWhiteSpace(s.DeviceId)))
            FcsGatewayClient.EnsureStarted();
    }
    private void AddSensorSubscription(LightSourceSubscription incoming)
    {
        var s = state.Settings.Adaptive;
        var item = s.LightSources.FirstOrDefault(x => x.DeviceId == incoming.DeviceId);
        if (item is null) { if (s.LightSources.Count >= 32) throw new InvalidDataException(); item = incoming; s.LightSources.Add(item); }
        else { item.Url = incoming.Url; item.SignedDevice = incoming.SignedDevice; }
        s.ActiveSourceId = item.Id; s.RecordLightHistory = true; s.Enabled = false; SaveAdaptiveSettings();
    }
}
