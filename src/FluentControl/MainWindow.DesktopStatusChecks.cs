using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckDesktopStatusAsync()
    {
        if (!uiTest) throw new InvalidOperationException("Desktop status checks require simulated devices.");
        var previous = state.Settings;
        var device = displayDevices[0]; var oldHdr = device.Hdr;
        try
        {
            state.Settings = new AppSettings { DesktopPanelEnabled = true };
            var source = new LightSourceSubscription { Id = "desktop-fixture", Name = "Fixture light", Url = "fc-usb://COM999", DeviceId = "fixture" };
            state.Settings.Adaptive.LightSources.Add(source);
            state.Settings.Adaptive.ActiveSourceId = source.Id;
            state.Settings.Adaptive.RecordLightHistory = false;
            state.Settings.Adaptive.Enabled = false;
            device.Hdr = new(HdrAvailability.Available, true, true);
            foreach (var grouped in new[] { true, false })
            {
                state.Settings.GroupDesktopMonitors = grouped;
                RefreshDesktopPanel();
                var panel = desktopPanel ?? throw new InvalidOperationException("Missing desktop panel.");
                PublishDesktopLight(source.Id, new LightSample { SourceId = "fixture", Lux = 123, Timestamp = DateTimeOffset.UtcNow, Valid = true });
                if (!panel.HdrNoticeText.Contains(device.DisplayName) || !panel.HdrNoticeText.Contains("HDR") || !panel.LightReadingText.Contains("123 lux") || panel.AutomaticEnabled)
                    throw new InvalidOperationException("Desktop HDR/lux status missing in linked or individual mode.");
                var toggle = Descendants<ToggleSwitch>((FrameworkElement)panel.Content).Single();
                toggle.IsOn = true;
                if (state.Settings.Adaptive.Enabled || panel.AutomaticEnabled) throw new InvalidOperationException("Locked desktop changed automatic adjustment.");
                panel.SetUnlocked(true); toggle.IsOn = true;
                if (!state.Settings.Adaptive.Enabled) throw new InvalidOperationException("Desktop did not enable the shared rule.");
                toggle.IsOn = false;
                if (state.Settings.Adaptive.Enabled || state.Settings.Adaptive.RecordLightHistory) throw new InvalidOperationException("Desktop changed history preference or failed to disable.");
                PublishDesktopLight(source.Id, new LightSample { SourceId = "fixture", Lux = 456, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1), Valid = true });
                if (panel.LightReadingText.Contains("456 lux")) throw new InvalidOperationException("Desktop showed stale lux as current.");
                FailDesktopLight();
                if (panel.LightReadingText.Contains("123 lux")) throw new InvalidOperationException("Desktop retained a failed reading.");
                panel.SetUnlocked(false);
            }
            device.Hdr = new(HdrAvailability.Available, false, false);
            state.Settings.Adaptive.ActiveSourceId = "removed";
            UpdateDesktopStatus();
            if (desktopPanel!.HdrNoticeText.Length != 0 || desktopPanel.LightReadingText.Length != 0) throw new InvalidOperationException("Desktop retained HDR or removed source status.");
            StartupLog.Write("PASS: desktop HDR in both modes, shared automation lock guards, live/stale lux and source removal (simulated).");
        }
        finally
        {
            device.Hdr = oldHdr; state.Settings = previous; desktopLightSample = null; desktopLightFailed = false;
            SaveAdaptiveSettings(); RefreshDesktopPanel();
        }
        await Task.CompletedTask;
    }
}
