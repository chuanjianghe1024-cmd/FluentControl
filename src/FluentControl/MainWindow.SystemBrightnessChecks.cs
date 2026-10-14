using FluentControl.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentControl;
public sealed partial class MainWindow
{
    private sealed class PanelBrightnessFixture : ISystemBrightnessBackend
    {
        internal const string Path = @"\\?\DISPLAY#BOE1234#4&123&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        internal byte Value = 40;
        public bool IsConnected(string path) => path == Path;
        public IReadOnlyList<SystemBrightnessReading> Read() => new[] { new SystemBrightnessReading(@"DISPLAY\BOE1234\4&123&0&UID1_0", Value, new byte[] { 10, 40, 70, 100 }) };
        public void Write(string instance, byte value) => Value = value;
    }
    private async Task RunSystemBrightnessChecksAsync()
    {
        var backend = new PanelBrightnessFixture();
        using var controller = new SystemBrightnessController(backend);
        var panel = new MonitorDevice { Id = PanelBrightnessFixture.Path, Model = "Notebook panel", ModelId = "BOE1234", Connection = "test", Preference = new() { Label = "M3" } };
        var channel = controller.Create(panel.Id, backend.Read())!;
        panel.Features.Add(new() { Definition = VcpCatalog.Find("brightness")!, Channel = channel }); panel.Channels.Add(channel);
        displayDevices.Add(panel);
        try
        {
            DisplayMode.SelectedIndex = 0;
            RenderMonitorControls(generation);
            await Task.Delay(80); Root.UpdateLayout();
            var row = Descendants<Microsoft.UI.Xaml.FrameworkElement>(MonitorRows)
                .Single(e => AutomationProperties.GetAutomationId(e) == "individual-" + panel.Id + "-brightness");
            var slider = Descendants<Slider>(row).Single();
            if (slider.Value != 40 || !slider.IsEnabled) throw new InvalidOperationException("Windows panel slider missing.");
            slider.Value = 62;
            for (var attempt = 0; attempt < 40 && pendingWrites > 0; attempt++) await Task.Delay(50);
            if (pendingWrites > 0 || backend.Value != 70 || channel.Value != 70 || slider.Value != 70)
                throw new InvalidOperationException("Windows panel slider did not show driver readback.");
            if (!CaptureProfile().ContainsKey(ProfileGroups.MonitorKey(panel.Id, "brightness")))
                throw new InvalidOperationException("Windows panel brightness missing from scene snapshot.");
            StartupLog.Write("PASS: Windows panel brightness slider, supported levels, live readback and scene capture (simulated).");
        }
        finally { displayDevices.Remove(panel); RenderMonitorControls(generation); }
    }
}
