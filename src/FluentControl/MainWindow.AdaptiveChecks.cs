using FluentControl.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
namespace FluentControl;
public sealed partial class MainWindow
{
    private async Task CheckAdaptivePreviewAsync()
    {
        var previous = state.Settings.Adaptive;
        var section = selectedSettingsSection;
        var snapshot = AllChannels().ToDictionary(x => x.Key, x => x.Value.Value);
        try
        {
            state.Settings.Adaptive = new() { PreviewEnabled = true };
            BuildSettings(); SelectSettingsSection("adaptive");
            await Task.Delay(100); SettingsPanel.UpdateLayout();
            var canvas = Descendants<Canvas>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "adaptive-curve");
            if (canvas.ActualWidth < 100 || !canvas.Children.OfType<Polyline>().Any()) throw new InvalidOperationException("Adaptive graph failed to render.");
            var y = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "adaptive-y-1");
            y.Value = 37;
            if (state.Settings.Adaptive.Curve.Points[1].Brightness != 37) throw new InvalidOperationException("Adaptive numeric editing failed.");
            var stored = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (stored.State.Settings.Adaptive.Curve.Points[1].Brightness != 37) throw new InvalidOperationException("Adaptive edit not persisted.");
            state.Settings.Adaptive.Mode = "clock"; state.Settings.Adaptive.TimeCurve = AdaptivePresets.Default("clock"); BuildSettings(); SelectSettingsSection("adaptive");
            await Task.Delay(50); SettingsPanel.UpdateLayout();
            var first = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "adaptive-y-0");
            first.Value = 31;
            if (state.Settings.Adaptive.TimeCurve.Points[^1].Brightness != 31) throw new InvalidOperationException("Midnight endpoint edit is discontinuous.");
            if (snapshot.Any(x => AllChannels()[x.Key].Value != x.Value)) throw new InvalidOperationException("Preview changed a hardware channel.");
            var fixtureTarget = displayDevices.Single(x => x.Id == "ui-test-monitor-1");
            var channel = fixtureTarget.Channels.Single(x => x.PropertyKey == "brightness");
            var originalValue = channel.Read!();
            var originalHold = Interlocked.Read(ref manualBrightnessHold);
            try
            {
                state.Settings.Adaptive.Mode = "clock"; state.Settings.Adaptive.Enabled = true;
                state.Settings.Adaptive.TargetDisplayId = fixtureTarget.Id;
                foreach (var point in state.Settings.Adaptive.TimeCurve.Points) point.Brightness = 100;
                SaveAdaptiveSettings(); adaptiveLastWrite = default; adaptiveLastTick = default;
                Interlocked.Exchange(ref manualBrightnessHold, 0);
                await TickAdaptiveAsync(fixture: true);
                if (channel.Read!() <= originalValue || channel.Read!() - originalValue > 5.1) throw new InvalidOperationException("Automatic schedule did not apply a bounded brightness step.");
                var after = channel.Read!(); adaptiveDue = default;
                await TickAdaptiveAsync(fixture: true);
                if (channel.Read!() != after) throw new InvalidOperationException("Automatic rate limit failed.");
                ControlOperations.Apply(new[] { channel }, originalValue);
                adaptiveDue = default; adaptiveLastWrite = default;
                await TickAdaptiveAsync(fixture: true);
                if (channel.Read!() != originalValue) throw new InvalidOperationException("Automatic brightness overrode a manual hold.");
                state.Settings.Adaptive.Enabled = false; Interlocked.Exchange(ref manualBrightnessHold, 0); adaptiveDue = default;
                await TickAdaptiveAsync(fixture: true);
                if (channel.Read!() != originalValue) throw new InvalidOperationException("Disabled automation wrote brightness.");
            }
            finally { ControlOperations.Apply(new[] { channel }, originalValue, automatic: true); Interlocked.Exchange(ref manualBrightnessHold, originalHold); adaptiveReadback = null; }
            StartupLog.Write("PASS: simulated automatic schedule output, slew rate, write interval, manual hold and disable gate.");
            StartupLog.Write("PASS: adaptive curve rendering, numeric edits, persistence, midnight endpoints and no channel mutation.");
        }
        finally { state.Settings.Adaptive = previous; selectedSettingsSection = section; SaveState(); BuildSettings(); }
    }
}
