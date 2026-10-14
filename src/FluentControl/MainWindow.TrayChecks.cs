using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckTrayMenuAsync()
    {
        var theme = state.Settings.Theme;
        var visible = AppWindow.IsVisible;
        try
        {
            AppWindow.Hide();
            foreach (var selection in new[] { "dark", "light" })
            {
                state.Settings.Theme = selection; ApplyAppTheme();
                ShowTrayMenu();
                for (int i = 0; i < 40 && trayMenu?.Menu?.IsOpen != true; i++) await Task.Delay(25);
                await Task.Delay(100);
                if (trayMenu?.Menu is not { IsOpen: true } menu || AppWindow.IsVisible || trayMenu.AppWindow.IsShownInSwitchers)
                    throw new InvalidOperationException("Tray popup did not open independently of the hidden main window.");
                if (menu.IsConstrainedToRootBounds) throw new InvalidOperationException("Tray popup was constrained to its tiny host.");
                var expected = selection == "dark" ? ElementTheme.Dark : ElementTheme.Light;
                var items = menu.Items.OfType<MenuFlyoutItem>().ToArray();
                if (items.Length != 6 || items.Any(x => x.ActualTheme != expected || x.Icon is null) || trayMenu.MenuTheme != expected)
                    throw new InvalidOperationException("Tray menu lost commands, icons or app theme.");
                menu.Hide(); // Same Closed path used by Escape/light dismiss.
                for (int i = 0; i < 40 && trayMenu.AppWindow.IsVisible; i++) await Task.Delay(25);
                if (trayMenu.AppWindow.IsVisible || AppWindow.IsVisible) throw new InvalidOperationException("Tray dismissal left an owner window visible.");
            }
            ShowTrayMenu();
            for (int i = 0; i < 40 && trayMenu?.Menu?.IsOpen != true; i++) await Task.Delay(25);
            var open = trayMenu!.Menu!.Items.OfType<MenuFlyoutItem>().Single(x => (int)x.Tag == 1);
            var peer = new MenuFlyoutItemAutomationPeer(open);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            for (int i = 0; i < 40 && !AppWindow.IsVisible; i++) await Task.Delay(25);
            if (!AppWindow.IsVisible || trayMenu.AppWindow.IsVisible) throw new InvalidOperationException("Tray Open panel command did not activate the main panel and dismiss the popup.");
            StartupLog.Write("PASS: Fluent tray menu, light/dark themes, unconstrained popup, hidden main panel, dismissal, reopen and Open command.");
        }
        finally
        {
            trayMenu?.Dismiss();
            state.Settings.Theme = theme; ApplyAppTheme();
            if (visible) { AppWindow.Show(); Activate(); } else AppWindow.Hide();
        }
    }
}
