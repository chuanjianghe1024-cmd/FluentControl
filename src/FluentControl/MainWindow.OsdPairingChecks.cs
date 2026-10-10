using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckOsdPairingAsync()
    {
        var device = displayDevices[0]; var directory = Path.Combine(Path.GetTempPath(), "FC-pairing-ui-" + Guid.NewGuid());
        var writes = new List<(byte, uint)>(); bool unreadable = false;
        var fixture = new OsdPairingSession(_ => unreadable ? new(0, 0, 0, unchecked((int)0xC0262589)) : new(1, 0x0201, 2),
            (code, value) => { writes.Add((code, value)); unreadable = true; });
        Task show = ShowOsdPairingAsync(device, fixture, directory);
        ScrollViewer Content() => monitorAdaptationDialog?.Content as ScrollViewer ?? throw new InvalidOperationException("Pairing dialog missing");
        T Find<T>(string id) where T : FrameworkElement => Descendants<T>(Content()).Single(x => AutomationProperties.GetAutomationId(x) == id);
        async Task Invoke(string id)
        {
            var button = Find<Button>(id); button.StartBringIntoView();
            var peer = new ButtonAutomationPeer(button);
            for (var i = 0; i < 30 && (!button.IsLoaded || !peer.IsEnabled()); i++) await Task.Delay(100);
            if (!button.IsLoaded || !peer.IsEnabled()) throw new InvalidOperationException("Pairing button unavailable: " + id);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(50); await osdPairingOperation;
        }
        try
        {
            await Task.Delay(200);
            await Invoke("pairing-osd-enable");
            if (writes.Count != 1 || writes[0] != ((byte)0xCA, 0x0202u)) throw new InvalidOperationException("Pairing enable did not preserve button fields");
            foreach (var expander in Descendants<Expander>(Content())) expander.IsExpanded = true;
            await Task.Delay(80);
            Find<CheckBox>("pairing-bind-open").IsChecked = true;
            await Invoke("pairing-result-success");
            var store = new OsdPairingStore(directory, device.Id);
            var saved = store.Load(OsdIdentity.Capture(device), true)!;
            if (saved.Bindings.Single().Action != "open" || saved.Trials[0].After?.Current is not null) throw new InvalidOperationException("Pairing UI did not persist actual observation with failed readback");
            Find<CheckBox>("pairing-reuse-fields").IsChecked = true;
            await Invoke("pairing-osd-disable");
            await Invoke("pairing-osd-disable");
            if (writes.Count != 3 || writes[^1].Item2 != 0x0201) throw new InvalidOperationException("Pairing disable cannot be repeated after read failure");
            // CA=3 requires a separate second user action.
            await Invoke("pairing-osd-disable-events");
            if (writes.Count != 3) throw new InvalidOperationException("Disable events bypassed second confirmation");
            await Invoke("pairing-confirm-send");
            if (writes.Count != 4 || writes[^1].Item2 != 0x0203) throw new InvalidOperationException("Confirmed disable events missing");
            Find<ComboBox>("pairing-navigation").SelectedIndex = 2; await Task.Delay(80);
            if (Find<Button>("pairing-remote-up").IsEnabled || Find<Button>("pairing-remote-enter").IsEnabled) throw new InvalidOperationException("Unbound navigation became available");
            await Invoke("pairing-remote-open");
            if (writes.Count != 5) throw new InvalidOperationException("Bound remote button failed");
            // Closing a queued request must not send it after the lifecycle gate is released.
            await gate.WaitAsync();
            try
            {
                var button = Find<Button>("pairing-remote-open");
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                CloseMonitorAdaptation(); await Task.Delay(50);
            }
            finally { gate.Release(); }
            await show;
            if (writes.Count != 5 || monitorAdaptationDialog is not null) throw new InvalidOperationException("Closed pairing retained queued hardware work");
            show = ShowOsdPairingAsync(device, new OsdPairingSession(_ => new(1, 0x0201, 2), (c, v) => writes.Add((c, v))), directory);
            await Task.Delay(200); await osdPairingOperation;
            Find<ComboBox>("pairing-navigation").SelectedIndex = 2; await Task.Delay(80);
            await Invoke("pairing-remote-open");
            if (writes.Count != 6) throw new InvalidOperationException("Local pairing did not survive reopening");
            StartupLog.Write("PASS: local OSD pairing UI, explicit success binding, failed readback, repeated recovery, second confirmation, remote, offline reload and close cancellation.");
        }
        finally
        {
            CloseMonitorAdaptation(); await show;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
