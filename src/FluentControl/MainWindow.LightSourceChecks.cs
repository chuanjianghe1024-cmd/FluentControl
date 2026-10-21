using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckLightSourceManagementAsync()
    {
        if (!uiTest) throw new InvalidOperationException();
        var before = state.Settings.Adaptive;
        var previousSection = selectedAdaptiveSection;
        try
        {
            var one = new LightSourceSubscription { Name = "Same name", DeviceId = "fixture-one", Url = "fc-usb://com7" };
            var two = new LightSourceSubscription { Name = "Same name", DeviceId = "fixture-two", Url = "fc-usb://com8" };
            state.Settings.Adaptive = new() { LightSources = new() { one, two }, ActiveSourceId = one.Id, Enabled = true };
            OpenAdaptivePage("devices"); await Task.Delay(100); SettingsPanel.UpdateLayout();
            if (SettingsPanel.Visibility != Visibility.Visible || MainScroll.Visibility != Visibility.Collapsed || settingsSections.Count != 3)
                throw new InvalidOperationException("Adaptive page is not a dedicated three-section destination.");
            if (!Descendants<Expander>(SettingsPanel).Any(x => AutomationProperties.GetAutomationId(x) == "sensor-buying-guide"))
                throw new InvalidOperationException("Hardware onboarding guide missing.");
            if (Descendants<StackPanel>(SettingsPanel).Count(x => AutomationProperties.GetAutomationId(x).StartsWith("light-source-row-")) != 2)
                throw new InvalidOperationException("Saved sources are not individually listed.");
            Button Find(string id) => Descendants<Button>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == id);
            void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            var rename = Find("light-source-rename-" + one.Id);
            var flyout = (Flyout)rename.Flyout;
            flyout.ShowAt(rename); await Task.Delay(100);
            var form = (StackPanel)flyout.Content;
            var name = form.Children.OfType<TextBox>().Single();
            name.Text = "   "; Invoke(form.Children.OfType<Button>().Single()); await Task.Delay(50);
            if (one.Name != "Same name") throw new InvalidOperationException("Blank device name accepted.");
            name.Text = "  桌面 C3  "; Invoke(form.Children.OfType<Button>().Single()); await Task.Delay(100); SettingsPanel.UpdateLayout();
            var persisted = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (one.Name != "桌面 C3" || one.DeviceId != "fixture-one" || one.Url != "fc-usb://com7" || !state.Settings.Adaptive.Enabled ||
                persisted.State.Settings.Adaptive.LightSources.Single(x => x.Id == one.Id).Name != "桌面 C3")
                throw new InvalidOperationException("Rename lost identity, URL, activation or persistence.");
            var remove = Find("light-source-delete-" + two.Id); flyout = (Flyout)remove.Flyout;
            flyout.ShowAt(remove); await Task.Delay(100); flyout.Hide();
            if (state.Settings.Adaptive.LightSources.Count != 2) throw new InvalidOperationException("Dismissed deletion removed a device.");
            flyout.ShowAt(remove); await Task.Delay(100);
            Invoke(((StackPanel)flyout.Content).Children.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "light-source-confirm-delete"));
            await Task.Delay(100); SettingsPanel.UpdateLayout();
            if (state.Settings.Adaptive.LightSources.Count != 1 || state.Settings.Adaptive.ActiveSourceId != one.Id || !state.Settings.Adaptive.Enabled)
                throw new InvalidOperationException("Deleting an inactive device disturbed the active device.");
            using var request = new CancellationTokenSource(); lightRequest = request;
            remove = Find("light-source-delete-" + one.Id); flyout = (Flyout)remove.Flyout;
            flyout.ShowAt(remove); await Task.Delay(100);
            Invoke(((StackPanel)flyout.Content).Children.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "light-source-confirm-delete"));
            await Task.Delay(100);
            persisted = new UserStateStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
            if (state.Settings.Adaptive.LightSources.Count != 0 || state.Settings.Adaptive.Enabled || !request.IsCancellationRequested || persisted.State.Settings.Adaptive.LightSources.Count != 0)
                throw new InvalidOperationException("Deleting the active device did not stop/cancel/persist.");
            SelectSettingsSection("history"); BuildSettings();
            if (selectedAdaptiveSection != "history" || settingsSections["history"].Visibility != Visibility.Visible)
                throw new InvalidOperationException("Adaptive page lost section on rebuild.");
            StartupLog.Write("PASS: dedicated adaptive navigation and onboarding; two same-name sources, rename persistence and identity, deletion dismissal, inactive/active removal and read cancellation.");
        }
        finally { lightRequest = null; state.Settings.Adaptive = before; selectedAdaptiveSection = previousSection; SaveState(); BuildSettings(); }
    }
}
