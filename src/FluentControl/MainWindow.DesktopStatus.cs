using FluentControl.Services;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private ToggleSwitch? adaptiveEnableToggle;
    private LightSample? desktopLightSample;
    private string desktopLightSourceId = "";
    private bool desktopLightFailed, desktopLightBusy;
    private DateTimeOffset desktopLightDue;
    private CancellationTokenSource? desktopLightRequest;

    private void SetDesktopAutomatic(bool enabled)
    {
        if (closed || desktopPanel?.IsUnlocked != true || state.Settings.Adaptive.Enabled == enabled) return;
        state.Settings.Adaptive.Enabled = enabled;
        Interlocked.Exchange(ref manualBrightnessHold, 0);
        SaveAdaptiveSettings();
        // Uses the configured rule/targets, never the desktop panel's grouping or row selection.
        if (adaptiveEnableToggle is not null) adaptiveEnableToggle.IsOn = enabled;
    }
    private void PublishDesktopLight(string sourceId, LightSample sample)
    {
        desktopLightSourceId = sourceId;
        desktopLightSample = sample;
        desktopLightFailed = false;
        UpdateDesktopStatus();
    }
    private void FailDesktopLight()
    {
        desktopLightSample = null;
        desktopLightFailed = true;
        UpdateDesktopStatus();
    }
    private void UpdateDesktopStatus()
    {
        if (desktopPanel is null || closed) return;
        var s = state.Settings.Adaptive;
        var ids = AdaptivePresets.Targets(s).ToHashSet();
        var names = displayDevices.Where(d => ids.Contains(d.Id)).Select(d => d.DisplayName).ToArray();
        var scope = F("沿用自动调节规则 · {0}", "Uses automatic adjustment rule · {0}",
            names.Length == 0 ? T("目标屏幕不可用", "Target display unavailable") : string.Join(" + ", names));
        var source = s.LightSources.FirstOrDefault(x => x.Id == s.ActiveSourceId);
        string lux = "";
        if (source is not null || s.Mode == "ambient" && s.ActiveSourceId == "simulation")
        {
            var name = source?.Name ?? T("模拟", "Simulated");
            var sample = desktopLightSourceId == s.ActiveSourceId ? desktopLightSample : null;
            var stale = sample is not null && (DateTimeOffset.UtcNow - sample.Timestamp > TimeSpan.FromMilliseconds(Math.Max(5000, 3d * (source?.PollMilliseconds ?? 1000))) || sample.Timestamp > DateTimeOffset.UtcNow.AddSeconds(5));
            lux = name + " · " + (sample is not null && !stale && sample.Valid && sample.Lux is double value && double.IsFinite(value)
                ? $"{sample.Lux:0.##} lux · {sample.Timestamp.ToLocalTime():HH:mm:ss}" + (sample.Simulated ? " · " + T("模拟", "Simulated") : "")
                : desktopLightFailed || stale ? T("光照不可用，等待重试", "Light unavailable; waiting to retry") : T("等待数据…", "Waiting for data…"));
        }
        var hdrNames = displayDevices.Where(d => d.Hdr.Enabled == true || d.Hdr.Active == true).Select(d => d.DisplayName).ToArray();
        var hdrText = hdrNames.Length == 0 ? "" : string.Join(" + ", hdrNames) + " · " + T("HDR 调节限制", "HDR control limitations") + "\n" +
            T("HDR 模式下，显示器可能锁定亮度、对比度、色温等调节。若调节无效，请关闭 HDR 后重新读取设备。", "In HDR mode, the display may lock brightness, contrast, color temperature and other controls. If adjustments have no effect, turn off HDR and refresh devices.");
        desktopPanel.UpdateStatus(s.Enabled, scope, lux, hdrText);
    }

    // Ambient mode publishes from its existing collector. In clock/solar mode this
    // read-only collector lets the panel show the selected sensor without enabling
    // ambient control, changing history preferences or starting a second ambient reader.
    private async Task RefreshDesktopLightAsync()
    {
        var s = state.Settings.Adaptive;
        if (desktopPanel is null || closed || uiTest || desktopLightBusy || sensorSetupActive || s.Mode == "ambient" || DateTimeOffset.UtcNow < desktopLightDue) return;
        var source = s.LightSources.FirstOrDefault(x => x.Id == s.ActiveSourceId);
        if (source is null) return;
        desktopLightBusy = true;
        desktopLightDue = DateTimeOffset.UtcNow.AddMilliseconds(Math.Clamp(source.PollMilliseconds, LightFeed.MinimumPollMilliseconds, LightFeed.MaximumPollMilliseconds));
        var revision = adaptiveRevision;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(LocalLightProtocol.IsLocal(source.Url) ? 25 : 3));
        desktopLightRequest = cancel;
        try
        {
            var sample = LocalLightProtocol.IsLocal(source.Url) ? await LocalLightSensor.ReadAsync(source, cancel.Token)
                : source.SignedDevice ? await NetworkSignalSensor.ReadAsync(lightClient, source, cancel.Token)
                : await LightFeed.ReadAsync(lightClient, source, cancel.Token);
            if (!closed && desktopPanel is not null && revision == adaptiveRevision) PublishDesktopLight(source.Id, sample);
        }
        catch (Exception)
        {
            if (!closed && revision == adaptiveRevision)
            {
                FailDesktopLight(); desktopLightDue = DateTimeOffset.UtcNow.AddSeconds(10);
            }
        }
        finally { if (ReferenceEquals(desktopLightRequest, cancel)) desktopLightRequest = null; desktopLightBusy = false; }
    }
}
