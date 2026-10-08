using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
namespace FluentControl;

public sealed partial class MainWindow
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private async Task CheckProfilesAndThemeAsync()
    {
        if (!initialLoadCompleted || InitializationPanel.Visibility != Visibility.Collapsed || InitializationRing.IsActive || Navigation.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Initialization screen did not transition to device controls.");
        var theme = Root.RequestedTheme;
        try
        {
            foreach (var value in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                Root.RequestedTheme = value; await Task.Delay(100); ApplyTitleTheme();
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar.BackgroundColor?.R != (value == ElementTheme.Dark ? 32 : 243))
                    throw new InvalidOperationException("Title bar theme did not follow content.");
            }
        }
        finally { Root.RequestedTheme = theme; ApplyTitleTheme(); }
        var secondPreset = displayDevices[1].Channels.First(c => c.PropertyKey == "color-preset");
        var key = ProfileGroups.MonitorKey(displayDevices[1].Id, secondPreset.PropertyKey);
        if (CaptureProfile().ContainsKey(key)) throw new InvalidOperationException("Unadvertised current preset must not be captured.");
        var invalid = new ControlProfile { Name = "Legacy preset", Values = new() { [key] = new() { Value = 5 } } };
        await ApplyProfileAsync(invalid);
        if (secondPreset.Value != 5 || Status.Message.Contains("Unsupported option")) throw new InvalidOperationException("Legacy invalid preset was sent to the device.");
        Navigation.SelectedItem = Navigation.MenuItems[0]; DisplayMode.SelectedIndex = 1;
        var sections = ((Border)CombinedRows.Children[1]).Child as StackPanel;
        foreach (var expander in sections!.Children.OfType<Expander>()) expander.IsExpanded = true;
        await Task.Delay(100);
        var union = Descendants<ComboBox>(CombinedRows).FirstOrDefault(c => c.ItemsSource is ControlOption[] options && options.Select(o => o.Value).Order().SequenceEqual(new double[] { 5, 8, 11 }));
        if (union is null) throw new InvalidOperationException("Union of same-model preset options missing.");
        union.SelectedItem = ((ControlOption[])union.ItemsSource).First(x => x.Value == 11);
        await WaitForWritesAsync();
        if (secondPreset.Value != 11 || displayDevices[0].Channels.First(c => c.PropertyKey == "color-preset").Value != 5)
            throw new InvalidOperationException("Union choice was not isolated to supporting displays.");
        var group = new ProfileGroup { Name = "Imported file" }; state.Groups.Add(group);
        var profile = new ControlProfile { Name = "Game", GroupId = group.Id, Applications = new() { "Test Game" }, Monitors = CaptureMonitorMetadata(), Values = CaptureProfile() };
        state.Profiles.Add(profile); state.SelectedGroupId = group.Id; RefreshProfiles();
        if (((IEnumerable<ControlProfile>)ProfilePicker.ItemsSource).Count() != 1) throw new InvalidOperationException("Group picker did not scope profiles.");
        applicationFilter = "test game"; modelFilter = "TST0001"; filterAllGroups = true; RefreshProfiles();
        if (VisibleProfiles().Count != 1) throw new InvalidOperationException("Combined profile filters failed.");
        applicationFilter = modelFilter = ""; state.Profiles.Remove(profile); state.Groups.Remove(group); state.SelectedGroupId = ProfileGroup.LocalId; RefreshProfiles();
        StartupLog.Write("PASS: initialization, dark/light title bar, legacy preset guard, union choices, profile groups and combined filters");
    }
    private async Task CheckPanelTextSwitchAsync()
    {
        Navigation.SelectedItem = Navigation.SettingsItem; BuildSettings();
        await Task.Delay(100); SettingsPanel.UpdateLayout();
        var toggle = Descendants<ToggleSwitch>(SettingsPanel).First(t => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "setting-light-text");
        var peer = new ToggleSwitchAutomationPeer(toggle);
        var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        var original = state.Settings.DesktopLightText;
        var position = desktopPanel!.AppWindow.Position;
        for (var i = 0; i < 4; i++)
        {
            provider.Toggle(); await Task.Delay(150);
            if (desktopPanel is null || desktopPanel.UsesLightText != state.Settings.DesktopLightText || desktopPanel.RowCount != 2 || desktopPanel.BackdropAlpha is 0 or 255)
                throw new InvalidOperationException("Text color toggle failed or lost transparency/rows.");
        }
        if (original != state.Settings.DesktopLightText || desktopPanel!.AppWindow.Position.X != position.X || desktopPanel.AppWindow.Position.Y != position.Y)
            throw new InvalidOperationException("Text switch changed saved color or panel position.");
        StartupLog.Write("PASS: light-text toggle repeated through UI automation without losing panel state");
    }
}
