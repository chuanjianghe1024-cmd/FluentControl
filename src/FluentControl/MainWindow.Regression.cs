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
        var selective = ((ControlOption[])union.ItemsSource).First(x => x.Value == 11);
        if (!selective.Label.Contains(displayDevices[1].Preference.Label) || selective.Label.Contains(displayDevices[0].Preference.Label))
            throw new InvalidOperationException("Partial preset option does not identify its supporting display.");
        var speakerRow = Descendants<FrameworkElement>(CombinedRows).First(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == "linked-speaker");
        var supportText = string.Join(" ", Descendants<TextBlock>(speakerRow).Select(x => x.Text));
        if (!supportText.Contains(displayDevices[0].Preference.Label) || !supportText.Contains(displayDevices[1].Preference.Label) || !supportText.Contains("1/2"))
            throw new InvalidOperationException("Partial monitor feature does not identify supported/unavailable displays.");
        if (!Equals(DisplayMode.Items[1], Strings.T("整体控制", "Overall control"))) throw new InvalidOperationException("Overall mode label not applied.");
        if (!Descendants<TextBlock>(CombinedRows).Any(t => t.Text == Strings.F("同型号 · {0} 台联动", "Same model · {0} linked displays", 2)))
            throw new InvalidOperationException("Same-model badge missing.");
        var otherModel = new MonitorDevice { Id = "different-model", ModelId = "DEL1234", Model = "Other model", Connection = "test" };
        displayDevices.Add(otherModel);
        try
        {
            RenderMonitorControls(generation); await Task.Delay(100);
            if (!Descendants<TextBlock>(CombinedRows).Any(t => t.Text == Strings.T("不同型号 · 独立调节", "Different model · independent controls")))
                throw new InvalidOperationException("Different-model badge missing.");
        }
        finally { displayDevices.Remove(otherModel); RenderMonitorControls(generation); }
        // Restore expanded controls after the different-model fixture was removed.
        foreach (var expander in (((Border)CombinedRows.Children[1]).Child as StackPanel)!.Children.OfType<Expander>()) expander.IsExpanded = true;
        await Task.Delay(100);
        union = Descendants<ComboBox>(CombinedRows).First(c => c.ItemsSource is ControlOption[] options && options.Select(o => o.Value).Order().SequenceEqual(new double[] { 5, 8, 11 }));
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
    private async Task CheckDesktopNavigationAsync()
    {
        var groups = state.Groups; var profiles = state.Profiles;
        var selectedGroup = state.SelectedGroupId; var selectedProfile = state.SelectedProfileId;
        var appFilter = applicationFilter; var model = modelFilter; var brand = brandFilter;
        var dirty = profileDirty;
        var brightness = displayDevices[0].Channels.First(c => c.PropertyKey == "brightness"); var original = brightness.Value;
        var a = new ProfileGroup { Id = ProfileGroup.LocalId }; var empty = new ProfileGroup { Name = "Empty group" }; var b = new ProfileGroup { Name = "Imported group" };
        ControlProfile Scene(string name, string groupId, double value) => new() { Name = name, GroupId = groupId, Values = new() { [ProfileGroups.MonitorKey(displayDevices[0].Id, "brightness")] = new() { Value = value } } };
        var first = Scene("First", a.Id, 21); var second = Scene("Second", b.Id, 42); var third = Scene("Third", b.Id, 63);
        try
        {
            state.Groups = new() { a, empty, b }; state.Profiles = new() { third, first, second };
            state.SelectedGroupId = a.Id; state.SelectedProfileId = first.Id;
            applicationFilter = "no matches"; modelFilter = brandFilter = ""; // desktop and global keys must ignore main-window search filters
            RefreshProfiles(); desktopPanel!.SetUnlocked(false);
            Button Button(string id) => Descendants<Button>(desktopPanel.Content).First(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == id);
            async Task Click(string id)
            {
                ((IInvokeProvider)new ButtonAutomationPeer(Button(id)).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(75); while (applyingProfile) await Task.Delay(25);
            }
            await Click("desktop-next-group");
            if (state.SelectedGroupId != a.Id) throw new InvalidOperationException("Locked desktop navigation accepted input.");
            desktopPanel.SetUnlocked(true);
            await Click("desktop-next-group");
            if (state.SelectedGroupId != empty.Id || Button("desktop-next-profile").IsEnabled || brightness.Value != original)
                throw new InvalidOperationException("Empty group navigation applied a profile or left arrows enabled.");
            await Click("desktop-next-group");
            if (state.SelectedGroupId != b.Id || state.SelectedProfileId != third.Id || brightness.Value != 63)
                throw new InvalidOperationException("Desktop group change did not select and apply its first profile.");
            await Click("desktop-next-profile"); await Click("desktop-next-profile");
            if (state.SelectedProfileId != third.Id) throw new InvalidOperationException("Desktop profile arrows must wrap inside their group.");
            await Click("desktop-next-group");
            if (state.SelectedGroupId != a.Id || state.SelectedProfileId != first.Id || brightness.Value != 21) throw new InvalidOperationException("Group arrow did not wrap and apply the first profile.");
            await Click("desktop-previous-group"); await Click("desktop-previous-profile");
            if (state.SelectedGroupId != b.Id || state.SelectedProfileId != second.Id || brightness.Value != 42)
                throw new InvalidOperationException("Previous group/profile arrows failed.");
            applicationFilter = "no matches"; RefreshProfiles();
            // Use the actual commands shared by tray entries and registered global hotkeys.
            ShellCommand(3); while (applyingProfile) await Task.Delay(25);
            if (state.SelectedProfileId != first.Id || state.SelectedGroupId != a.Id || brightness.Value != 21)
                throw new InvalidOperationException("Global next did not wrap across groups.");
            ShellCommand(2); while (applyingProfile) await Task.Delay(25);
            if (state.SelectedProfileId != second.Id || state.SelectedGroupId != b.Id || brightness.Value != 42)
                throw new InvalidOperationException("Global previous did not cross groups.");
            GroupPicker.SelectedItem = a;
            while (applyingProfile) await Task.Delay(25);
            if (state.SelectedGroupId != a.Id || state.SelectedProfileId != first.Id || brightness.Value != 21)
                throw new InvalidOperationException("Main group picker did not apply the first profile.");
            GroupPicker.SelectedItem = b;
            while (applyingProfile) await Task.Delay(25);
            if (state.SelectedProfileId != third.Id || brightness.Value != 63) throw new InvalidOperationException("Main group picker did not follow profile order.");
            if (!DeleteGroupButton.IsEnabled) throw new InvalidOperationException("Imported group cannot be deleted.");
            state.SelectedGroupId = a.Id; RefreshProfiles();
            if (DeleteGroupButton.IsEnabled) throw new InvalidOperationException("Default group must be retained.");
            StartupLog.Write("PASS: separate desktop group/profile arrows, empty groups, locked input, cross-group hotkey commands and default-group protection");
        }
        finally
        {
            state.Groups = groups; state.Profiles = profiles; state.SelectedGroupId = selectedGroup; state.SelectedProfileId = selectedProfile;
            applicationFilter = appFilter; modelFilter = model; brandFilter = brand; profileDirty = dirty;
            await Task.Run(() => ControlOperations.Apply(new[] { brightness }, original));
            RefreshProfiles(); SynchronizeValues(); SaveState(); desktopPanel?.SetUnlocked(false);
        }
    }

}
