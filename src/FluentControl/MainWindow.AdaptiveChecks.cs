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
        var navigation = Navigation.SelectedItem;
        var snapshot = AllChannels().ToDictionary(x => x.Key, x => x.Value.Value);
        try
        {
            state.Settings.Adaptive = new() { PreviewEnabled = true };
            Navigation.SelectedItem = Navigation.SettingsItem;
            BuildSettings(); SelectSettingsSection("adaptive");
            await Task.Delay(100); SettingsPanel.UpdateLayout();
            var canvas = Descendants<Canvas>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "adaptive-curve");
            if (canvas.ActualWidth < 100 || !canvas.Children.OfType<Rectangle>().Any(x => Equals(x.Tag, "adaptive-band")) || canvas.Children.OfType<Polyline>().Any()) throw new InvalidOperationException("Adaptive graph failed to render.");
            var y = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "adaptive-y-1");
            y.Value = 37;
            if (state.Settings.Adaptive.Curve.Points[1].Brightness != 37) throw new InvalidOperationException("Adaptive numeric editing failed.");
            var stored = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (stored.State.Settings.Adaptive.Curve.Points[1].Brightness != 37) throw new InvalidOperationException("Adaptive edit not persisted.");
            var transitionGraph = Descendants<Canvas>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "brightness-transition-curve");
            var transitionShape = Descendants<ComboBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "brightness-transition-shape");
            transitionShape.SelectedIndex = 2;
            var duration = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "brightness-transition-input-0");
            var steps = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "brightness-transition-input-2");
            duration.Value = 1500; steps.Value = 5;
            var transitionStored = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (transitionGraph.Children.OfType<Ellipse>().Count() != 7 ||
                transitionStored.State.Settings.Adaptive.Transition.DurationMs != 1500 ||
                transitionStored.State.Settings.Adaptive.Transition.Steps != 5 ||
                transitionStored.State.Settings.Adaptive.Transition.Curve != "custom")
                throw new InvalidOperationException("Transition controls, sample points or persistence failed.");
            if (snapshot.Any(x => AllChannels()[x.Key].Value != x.Value))
                throw new InvalidOperationException("Transition preview wrote brightness.");
            var targets = Descendants<CheckBox>(SettingsPanel).Where(x => AutomationProperties.GetAutomationId(x).StartsWith("adaptive-target-")).Take(2).ToArray();
            if (targets.Length != 2) throw new InvalidOperationException("Multi-display fixture requires two choices.");
            foreach (var target in targets) target.IsChecked = true;
            if (AdaptivePresets.Targets(state.Settings.Adaptive).Count != 2) throw new InvalidOperationException("Multi-display selection failed.");
            var savedTargets = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (AdaptivePresets.Targets(savedTargets.State.Settings.Adaptive).Count != 2) throw new InvalidOperationException("Multi-display selection not persisted.");
            var local = new LightSourceSubscription { Id = "fixture-usb", Name = "Fixture probe", Url = "fc-usb://auto", DeviceId = "fixture-probe" };
            state.Settings.Adaptive.LightSources.Add(local); state.Settings.Adaptive.ActiveSourceId = local.Id;
            BuildSettings(); SelectSettingsSection("adaptive");
            SelectSettingsSection("devices");
            // Rebuilt ScrollViewer content enters the visual tree on the next layout pass.
            await Task.Delay(100); SettingsPanel.UpdateLayout();
            var interval = Descendants<NumberBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "light-sampling-seconds");
            interval.Value = 600;
            if (local.PollMilliseconds != 600000) throw new InvalidOperationException("Long light interval input failed.");
            var intervalSlider = Descendants<Slider>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "light-sampling-slider");
            intervalSlider.Value = 0;
            if (local.PollMilliseconds != 200 || interval.Value != .2) throw new InvalidOperationException("Light interval slider/input synchronization failed.");
            if (snapshot.Any(x => AllChannels()[x.Key].Value != x.Value)) throw new InvalidOperationException("Sensor settings changed hardware brightness.");
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
                var baseline = channel.Minimum + .4 * (channel.Maximum - channel.Minimum);
                ControlOperations.Apply(new[] { channel }, baseline, automatic: true);
                baseline = channel.Read!();
                state.Settings.Adaptive.Mode = "clock"; state.Settings.Adaptive.Enabled = true;
                state.Settings.Adaptive.TargetDisplayIds = new() { fixtureTarget.Id };
                foreach (var point in state.Settings.Adaptive.TimeCurve.Points) point.Brightness = 100;
                SaveAdaptiveSettings(); adaptiveLastWrite = default; adaptiveLastTick = default;
                Interlocked.Exchange(ref manualBrightnessHold, 0);
                await TickAdaptiveAsync(fixture: true);
                var deltaPercent = (channel.Read!() - baseline) / (channel.Maximum - channel.Minimum) * 100;
                if (deltaPercent < -.1 || deltaPercent > 12.1) throw new InvalidOperationException("Automatic schedule violated the easing step bound.");
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
            finally { ControlOperations.Apply(new[] { channel }, originalValue, automatic: true); Interlocked.Exchange(ref manualBrightnessHold, originalHold); adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); }
            var ambientChannels = displayDevices.Select(d => d.Channels.Single(c => c.PropertyKey == "brightness")).ToArray();
            var ambientOriginal = ambientChannels.Select(c => c.Read!()).ToArray();
            var ambientHold = Interlocked.Read(ref manualBrightnessHold);
            try
            {
                state.Settings.Adaptive.Mode = "ambient"; state.Settings.Adaptive.ActiveSourceId = "simulation";
                state.Settings.Adaptive.Curve = AdaptivePresets.Default("ambient");
                foreach (var point in state.Settings.Adaptive.Curve.Points) point.Brightness = 70;
                state.Settings.Adaptive.TargetDisplayIds = displayDevices.Select(d => d.Id).ToList();
                state.Settings.Adaptive.Enabled = true;
                for (int i = 0; i < ambientChannels.Length; i++)
                    ControlOperations.Apply(new[] { ambientChannels[i] }, ambientChannels[i].Minimum + .2 * (ambientChannels[i].Maximum - ambientChannels[i].Minimum), automatic: true);
                SaveAdaptiveSettings(); adaptiveLastWrite = default; adaptiveLastTick = default;
                Interlocked.Exchange(ref manualBrightnessHold, 0);
                for (int i = 0; i < 3; i++) { adaptiveDue = default; await Task.Delay(10); await TickAdaptiveAsync(fixture: true); }
                var hdrStates = hdr.ReadAll();
                for (int i = 0; i < ambientChannels.Length; i++)
                {
                    var state = hdrStates[displayDevices[i].Id];
                    if (state.Active == true || state.Enabled == true) continue;
                    var c = ambientChannels[i];
                    if (Math.Abs((c.Read!() - c.Minimum) / (c.Maximum - c.Minimum) * 100 - 20) > 1)
                        throw new InvalidOperationException("Ambient band must ease in, not jump immediately.");
                }
                var appliedCount = adaptiveAppliedTargets.Count;
                adaptiveDue = default; adaptiveLastWrite = default;
                await Task.Delay(10); await TickAdaptiveAsync(fixture: true);
                if (adaptiveAppliedTargets.Count != appliedCount) throw new InvalidOperationException("Stable band lost target state.");
            }
            finally
            {
                for (int i = 0; i < ambientChannels.Length; i++) ControlOperations.Apply(new[] { ambientChannels[i] }, ambientOriginal[i], automatic: true);
                Interlocked.Exchange(ref manualBrightnessHold, ambientHold);
                adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); adaptiveFailedTargets.Clear();
            }
            var historyCanvas = new Canvas { Width = 460, Height = 185 };
            var at = DateTimeOffset.UtcNow;
            DrawLightHistory(historyCanvas, new LightHistoryEntry[] {
                new("s", "s", at, at, 10, true, false),
                new("s", "s", at.AddSeconds(1), at.AddSeconds(1), 20, true, false),
                new("s", "s", at.AddSeconds(2), at.AddSeconds(2), null, false, false),
                new("s", "s", at.AddSeconds(3), at.AddSeconds(3), 30, true, false)
            }, 1000);
            if (historyCanvas.Children.OfType<Polyline>().Count() != 2 || historyCanvas.Children.OfType<Ellipse>().Count() != 3)
                throw new InvalidOperationException("History chart filled a data gap or lost samples.");
            await CheckWifiWizardAsync();
            await CheckLightSourceManagementAsync();
            StartupLog.Write("PASS: simulated automatic brightness easing, bounded steps, write interval, manual hold and disable gate.");
            StartupLog.Write("PASS: adaptive curve rendering, numeric edits, persistence, midnight endpoints and no channel mutation.");
        }
        finally { state.Settings.Adaptive = previous; selectedSettingsSection = section; SaveState(); BuildSettings(); Navigation.SelectedItem = navigation; }
    }
}
