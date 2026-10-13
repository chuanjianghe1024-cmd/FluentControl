using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckAppThemeAsync()
    {
        var originalTheme = state.Settings.Theme;
        var originalSection = selectedSettingsSection;
        var originalNavigation = Navigation.SelectedItem;
        var desktopText = state.Settings.DesktopLightText;
        try
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
            SelectSettingsSection("general");
            SettingsPanel.UpdateLayout();
            var picker = Descendants<ComboBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "setting-app-theme");
            var cards = settingsSections["general"].Children.OfType<Border>().ToArray();
            // Keep the same controls alive: recreating them would hide stale brushes.
            ShowMonitorOsd(displayDevices[0]);
            foreach (var selection in new[] { 2, 1, 2 })
            {
                picker.SelectedIndex = selection;
                await Task.Delay(120);
                ShellRoot.UpdateLayout();
                var expected = selection == 2 ? ElementTheme.Dark : ElementTheme.Light;
                var setting = selection == 2 ? "dark" : "light";
                if (state.Settings.Theme != setting || ShellRoot.ActualTheme != expected || Navigation.ActualTheme != expected || Root.ActualTheme != expected)
                    throw new InvalidOperationException("Theme picker did not update the whole application.");
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar.BackgroundColor?.R != (selection == 2 ? 32 : 243))
                    throw new InvalidOperationException("Title bar theme did not follow content.");
                var surface = selection == 2 ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
                foreach (var card in cards) CheckCardContrast(card, surface);
                if (monitorOsd?.Content is not Grid osdRoot || osdRoot.ActualTheme != expected || osdRoot.Background is not SolidColorBrush osdSurface)
                    throw new InvalidOperationException("The open FC menu did not follow the app theme.");
                foreach (var card in Descendants<Border>(osdRoot).Where(x => x.Style == Application.Current.Resources["CardBorderStyle"]))
                    CheckCardContrast(card, osdSurface.Color);
                var persisted = new UserStateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "test-user-state.json"));
                if (persisted.State.Settings.Theme != setting || state.Settings.DesktopLightText != desktopText)
                    throw new InvalidOperationException("Theme preference was not saved, or changed the independent desktop text preference.");
            }
            picker.SelectedIndex = 0;
            if (ShellRoot.RequestedTheme != ElementTheme.Default || state.Settings.Theme != "system")
                throw new InvalidOperationException("System theme did not clear the explicit override.");
            BuildSettings(); SelectSettingsSection("general");
            if (Descendants<ComboBox>(SettingsPanel).Single(x => AutomationProperties.GetAutomationId(x) == "setting-app-theme").SelectedIndex != 0)
                throw new InvalidOperationException("Rebuilding settings lost the theme selection.");
            StartupLog.Write("PASS: live light/dark settings-card contrast, whole-window and open FC menu theme, saved preference and system-default reset.");
        }
        finally
        {
            CloseMonitorOsd();
            state.Settings.Theme = originalTheme; ApplyAppTheme(); SaveState();
            selectedSettingsSection = originalSection; BuildSettings();
            Navigation.SelectedItem = originalNavigation;
        }
    }

    private static void CheckCardContrast(Border card, Color surface)
    {
        if (card.Background is not SolidColorBrush background) throw new InvalidOperationException("Missing themed card background.");
        var label = Descendants<TextBlock>(card).FirstOrDefault();
        if (label?.Foreground is not SolidColorBrush foreground) throw new InvalidOperationException("Missing card label foreground.");
        static Color Over(Color top, Color bottom) => Color.FromArgb(255,
            (byte)((top.R * top.A + bottom.R * (255 - top.A)) / 255),
            (byte)((top.G * top.A + bottom.G * (255 - top.A)) / 255),
            (byte)((top.B * top.A + bottom.B * (255 - top.A)) / 255));
        static double Luminance(Color color)
        {
            static double Linear(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var backdrop = Over(background.Color, surface);
        var a = Luminance(Over(foreground.Color, backdrop));
        var b = Luminance(backdrop);
        if ((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 4.5)
            throw new InvalidOperationException("Settings/control card text contrast is below 4.5:1: " + label.Text);
    }
}
