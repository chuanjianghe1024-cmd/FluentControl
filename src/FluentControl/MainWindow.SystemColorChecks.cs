using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task RunSystemColorChecksAsync()
    {
        if (!uiTest || systemColor is null) throw new InvalidOperationException("Expected simulated color backend.");
        MonitorRows.Visibility = Visibility.Visible; CombinedRows.Visibility = Visibility.Visible;
        Root.UpdateLayout();
        foreach (var e in Descendants<Expander>(MonitorRows).Concat(Descendants<Expander>(CombinedRows)).ToArray()) e.IsExpanded = true;
        Root.UpdateLayout();
        var sliders = Descendants<Slider>(MonitorRows).Where(s => AutomationProperties.GetAutomationId(s).StartsWith("system-temperature-")).ToArray();
        if (sliders.Length != 2 || !sliders.All(s => s.IsEnabled)) throw new InvalidOperationException("Per-screen software color controls missing.");
        var d = displayDevices[0]; var rawTemperature = d.Channels.First(c => c.PropertyKey == "color-preset").Value;
        sliders[0].Value = 4500;
        if (systemColor.Read(d.Id).Kelvin != 0) throw new InvalidOperationException("Selecting a temperature must not write before Apply.");
        await ChangeSystemColorAsync(d, generation, 4500);
        if (systemColor.Read(d.Id).Kelvin != 4500 || systemColor.Read(displayDevices[1].Id).Kelvin != 0 ||
            d.Channels.First(c => c.PropertyKey == "color-preset").Value != rawTemperature)
            throw new InvalidOperationException("Software temperature changed the wrong display or hardware preset.");
        if (CaptureProfile().GetValueOrDefault(SystemColorKey(d.Id))?.Value != 4500 ||
            MonitorPresetLibrary.Capture(d, "test", "", Array.Empty<string>()).Values.ContainsKey("system-temperature"))
            throw new InvalidOperationException("Software temperature must save locally without becoming a model hardware preset.");
        await RefreshAsync();
        if (colorStates[d.Id].Kelvin != 4500) throw new InvalidOperationException("Refresh lost an active software color session.");
        await ChangeHdrAsync(displayDevices[0], generation, true);
        if (colorStates[d.Id].Available || systemColor.Read(d.Id).Recovery) throw new InvalidOperationException("HDR must restore and suspend software color first.");
        await ChangeHdrAsync(displayDevices[0], generation, false);
        if (colorStates[d.Id].Kelvin != 0) throw new InvalidOperationException("HDR off must not replay old software color.");
        await ChangeSystemColorAsync(displayDevices[0], generation, 7500);
        await ChangeSystemColorAsync(displayDevices[0], generation, 0);
        if (systemColor.Read(d.Id).Kelvin != 0) throw new InvalidOperationException("Software color restoration failed.");
        // Hold the lifecycle gate so refresh invalidates an already queued profile.
        // No physical display is touched by this regression.
        var queuedProfile = new ControlProfile { Name = "Stale color profile", Values = new()
        {
            [SystemColorKey(d.Id)] = new SavedValue { Value = 4500 }
        } };
        Task queued, refresh;
        await gate.WaitAsync();
        try
        {
            queued = ApplyProfileAsync(queuedProfile);
            refresh = RefreshAsync();
        }
        finally { gate.Release(); }
        await Task.WhenAll(queued, refresh);
        if (systemColor.Read(d.Id).Kelvin != 0 || state.SelectedProfileId == queuedProfile.Id)
            throw new InvalidOperationException("A queued profile survived display refresh.");
        DisplayMode_SelectionChanged(DisplayMode, null!);
        StartupLog.Write("System color UI checks passed (simulated curves; no native gamma writes)");
    }
}
