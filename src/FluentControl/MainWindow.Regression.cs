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
        RenderMonitorControls(generation); // mirrors the device refresh after a language change
        Navigation.SelectedItem = Navigation.MenuItems[0]; DisplayMode.SelectedIndex = 1;
        var sections = ((Border)CombinedRows.Children[1]).Child as StackPanel;
        foreach (var expander in sections!.Children.OfType<Expander>()) expander.IsExpanded = true;
        await Task.Delay(100);
        var union = Descendants<ComboBox>(CombinedRows).FirstOrDefault(c => c.ItemsSource is ControlOption[] options && options.Select(o => o.Value).Order().SequenceEqual(new double[] { 5, 8, 11 }));
        if (union is null) throw new InvalidOperationException("Union of cross-model preset options missing.");
        var selective = ((ControlOption[])union.ItemsSource).First(x => x.Value == 11);
        if (!selective.Label.Contains(displayDevices[1].Preference.Label) || selective.Label.Contains(displayDevices[0].Preference.Label))
            throw new InvalidOperationException("Partial preset option does not identify its supporting display.");
        var speakerRow = Descendants<FrameworkElement>(CombinedRows).First(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == "linked-speaker");
        var supportText = string.Join(" ", Descendants<TextBlock>(speakerRow).Select(x => x.Text));
        if (!supportText.Contains(displayDevices[0].Preference.Label) || !supportText.Contains(displayDevices[1].Preference.Label) || !supportText.Contains("1/2"))
            throw new InvalidOperationException("Partial monitor feature does not identify supported/unavailable displays.");
        if (!Equals(DisplayMode.Items[1], Strings.T("整体控制", "Overall control"))) throw new InvalidOperationException("Overall mode label not applied.");
        if (!Descendants<TextBlock>(CombinedRows).Any(t => t.Text == Strings.F("跨型号 · {0} 台联动", "Across models · {0} linked displays", 2)))
            throw new InvalidOperationException("Cross-model linked badge missing.");
        await CheckCrossModelControlsAsync();
        await CheckMonitorOsdAsync();
        var otherModel = new MonitorDevice { Id = "unknown-model", Model = "Unknown model", Connection = "test" };
        otherModel.Channels.Add(new() { Name = "Unknown display brightness", Detail = "", Glyph = "", PropertyKey = "brightness", Value = 45, Write = _ => { } });
        displayDevices.Add(otherModel);
        try
        {
            RenderMonitorControls(generation); await Task.Delay(100);
            if (!Descendants<TextBlock>(CombinedRows).Any(t => t.Text == Strings.F("跨型号 · {0} 台联动", "Across models · {0} linked displays", 3)) ||
                Descendants<FrameworkElement>(CombinedRows).Count(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == "linked-brightness") != 1 ||
                DesktopCandidates().Single(x => x.Key == "monitor/all/brightness").Targets.Count != 3)
                throw new InvalidOperationException("An unknown model must join overall brightness when it supports the feature.");
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
    private async Task SetTestSliderAsync(DependencyObject root, string id, double value)
    {
        var row = Descendants<FrameworkElement>(root).Single(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == id);
        var slider = Descendants<Slider>(row).Single();
        ((IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(value);
        await WaitForWritesAsync();
    }
    private async Task CheckMonitorOsdAsync()
    {
        var first = displayDevices[0].Channels.First(c => c.PropertyKey == "brightness");
        var second = displayDevices[1].Channels.First(c => c.PropertyKey == "brightness");
        var before = first.Value; var other = second.Value;
        try
        {
            ShowMonitorOsd(displayDevices[0]); await Task.Delay(150);
            var content = (DependencyObject)monitorOsd!.Content;
            await SetTestSliderAsync(content, "osd-brightness", 61);
            if (first.Value != 61 || second.Value != other) throw new InvalidOperationException("OSD must target only its selected screen.");
            var row = Descendants<FrameworkElement>(content).Single(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == "osd-brightness");
            Descendants<Slider>(row).Single().Value = 63;
            CloseMonitorOsd(); await WaitForWritesAsync();
            if (first.Value != 61 || monitorOsd is not null) throw new InvalidOperationException("Closed OSD must cancel pending writes.");
            StartupLog.Write("PASS: monitor OSD controls only the selected screen; closing cancels queued writes");
        }
        finally { CloseMonitorOsd(); ControlOperations.Apply(new[] { first }, before); SynchronizeValues(); }
    }
    private async Task CheckCrossModelControlsAsync()
    {
        if (displayDevices.Select(d => d.ModelId).Distinct().Count() != 2) throw new InvalidOperationException("Cross-model test requires different models.");
        var channels = displayDevices.SelectMany(d => d.Channels).Where(c => c.PropertyKey is "brightness" or "contrast").ToArray();
        var saved = channels.ToDictionary(c => c, c => c.Value);
        var mapping = displayDevices[1].Preference.Brightness;
        try
        {
            foreach (var key in new[] { "brightness", "contrast" })
            {
                var value = key == "brightness" ? 37 : 61;
                await SetTestSliderAsync(CombinedRows, "linked-" + key, value);
                if (channels.Where(c => c.PropertyKey == key).Any(c => c.Value != value || c.Read?.Invoke() != value))
                    throw new InvalidOperationException("Overall slider failed to update both models using their native VCP ranges: " + key);
            }
            displayDevices[1].Preference.Brightness = new() { Enabled = true, Offset = 10 };
            BindBrightnessMapping(displayDevices[1]);
            await SetTestSliderAsync(CombinedRows, "linked-brightness", 55);
            var first = displayDevices[0].Channels.First(c => c.PropertyKey == "brightness");
            var second = displayDevices[1].Channels.First(c => c.PropertyKey == "brightness");
            if (first.Value != 55 || second.Value != 65) throw new InvalidOperationException("Cross-model brightness did not retain per-display calibration.");
            DisplayMode.SelectedIndex = 0; await Task.Delay(50);
            await SetTestSliderAsync(MonitorRows, "individual-" + displayDevices[0].Id + "-brightness", 17);
            if (first.Value != 17 || second.Value != 65) throw new InvalidOperationException("Individual mode changed another display.");
        }
        finally
        {
            displayDevices[1].Preference.Brightness = mapping; BindBrightnessMapping(displayDevices[1]);
            foreach (var entry in saved) ControlOperations.Apply(new[] { entry.Key }, entry.Value);
            SynchronizeValues(); DisplayMode.SelectedIndex = 1;
        }
        StartupLog.Write("PASS: one overall brightness/contrast slider updates different models and native ranges; calibration and individual isolation retained.");
    }
    private async Task CheckDesktopLinkedControlsAsync()
    {
        if (!state.Settings.DesktopRows.SequenceEqual(new[] { "monitor/all/brightness", "monitor/all/contrast" }))
            throw new InvalidOperationException("Legacy model-specific desktop selections were not migrated.");
        var candidates = DesktopCandidates();
        if (candidates.Single(x => x.Key == "monitor/all/brightness").Targets.Count != 2 ||
            candidates.Single(x => x.Key == "monitor/all/contrast").Targets.Count != 2 ||
            candidates.Single(x => x.Key == "monitor/all/speaker").Targets.Count != 1)
            throw new InvalidOperationException("Desktop controls must link different models and target partial capabilities correctly.");
        var channels = displayDevices.SelectMany(d => d.Channels).Where(c => c.PropertyKey is "brightness" or "contrast").ToArray();
        var saved = channels.ToDictionary(c => c, c => c.Value);
        try
        {
            desktopPanel!.SetUnlocked(true);
            foreach (var key in new[] { "brightness", "contrast" })
            {
                await SetTestSliderAsync(desktopPanel.Content, "desktop-monitor/all/" + key, 46);
                if (channels.Where(c => c.PropertyKey == key).Any(c => c.Value != 46 || c.Read?.Invoke() != 46))
                    throw new InvalidOperationException("Desktop overall slider failed to update both models: " + key);
            }
        }
        finally
        {
            foreach (var entry in saved) ControlOperations.Apply(new[] { entry.Key }, entry.Value);
            SynchronizeValues(); desktopPanel!.SetUnlocked(false);
        }
        StartupLog.Write("PASS: migrated desktop selections, two cross-model linked sliders and partial-feature targets.");
        await CheckDesktopModeSwitchAsync();
        await CheckDesktopExtendedControlsAsync();
    }
    private async Task CheckDesktopExtendedControlsAsync()
    {
        var originalRows = state.Settings.DesktopRows.ToList();
        var keys = new[] { "color-preset", "gain-red", "gain-green", "gain-blue", "display-mode" };
        var channels = displayDevices.SelectMany(d => d.Channels).Where(c => keys.Contains(c.PropertyKey)).ToArray();
        var saved = channels.ToDictionary(c => c, c => c.Value);
        try
        {
            var candidates = DesktopCandidates();
            foreach (var key in keys)
                if (candidates.Single(r => r.Key == "monitor/all/" + key).Targets.Count != 2)
                    throw new InvalidOperationException("Missing extended desktop aggregate: " + key);
            if (candidates.Any(r => r.Targets.Any(c => c.RequiresConfirmation || c.IsAction || c.CompatibilityOnly)))
                throw new InvalidOperationException("Unsafe or duplicate desktop aggregate.");
            state.Settings.DesktopRows = keys.Select(k => "monitor/all/" + k).ToList();
            RefreshDesktopPanel(); desktopPanel!.SetUnlocked(true);
            await Task.Delay(100);
            foreach (var key in keys.Where(k => k.StartsWith("gain-")))
            {
                await SetTestSliderAsync(desktopPanel.Content, "desktop-monitor/all/" + key, 43);
                if (channels.Where(c => c.PropertyKey == key).Any(c => c.Value != 43 || c.Read?.Invoke() != 43))
                    throw new InvalidOperationException("RGB desktop aggregate did not update both displays.");
            }
            async Task<ComboBox> Choose(string key, double value)
            {
                var row = Descendants<FrameworkElement>(desktopPanel.Content).First(e =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == "desktop-monitor/all/" + key);
                var combo = Descendants<ComboBox>(row).Single();
                combo.SelectedItem = ((IEnumerable<ControlOption>)combo.ItemsSource).Single(o => o.Value == value);
                await Task.Delay(100); await WaitForWritesAsync();
                return combo;
            }
            await Choose("color-preset", 8);
            if (channels.Where(c => c.PropertyKey == "color-preset").Any(c => c.Value != 8))
                throw new InvalidOperationException("Common desktop color preset did not update both displays.");
            var mixed = await Choose("color-preset", 11);
            if (displayDevices[0].Channels.Single(c => c.PropertyKey == "color-preset").Value != 8 ||
                displayDevices[1].Channels.Single(c => c.PropertyKey == "color-preset").Value != 11 || mixed.SelectedItem is not null)
                throw new InvalidOperationException("Partial preset must target only its supported display and show mixed values.");
            await Choose("display-mode", 5);
            if (channels.Where(c => c.PropertyKey == "display-mode").Any(c => c.Value != 5))
                throw new InvalidOperationException("Desktop scene mode did not update both displays.");
        }
        finally
        {
            foreach (var entry in saved) ControlOperations.Apply(new[] { entry.Key }, entry.Value);
            state.Settings.DesktopRows = originalRows;
            RefreshDesktopPanel(); SynchronizeValues(); desktopPanel!.SetUnlocked(false);
        }
        StartupLog.Write("PASS: desktop aggregated RGB, color presets, display modes, union options, partial targeting and mixed readback.");
    }
    private async Task CheckDesktopModeSwitchAsync()
    {
        var originalRows = state.Settings.DesktopRows.ToList();
        var originalLimit = state.Settings.DesktopMaxRows;
        var channels = displayDevices.SelectMany(d => d.Channels).Where(c => c.PropertyKey is "brightness" or "contrast").ToArray();
        var saved = channels.ToDictionary(c => c, c => c.Value);
        async Task ClickMode()
        {
            var button = Descendants<Button>(desktopPanel!.Content).Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "desktop-monitor-mode");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100); await WaitForWritesAsync();
        }
        try
        {
            state.Settings.DesktopMaxRows = 1;
            RefreshDesktopPanel(); desktopPanel!.SetUnlocked(true);
            await ClickMode();
            if (state.Settings.GroupDesktopMonitors || desktopPanel.RowCount != 4 || !desktopPanel.IsUnlocked)
                throw new InvalidOperationException("Individual desktop mode lost M2 or truncated selected controls at the visible row limit.");
            var first = displayDevices[0].Channels.First(c => c.PropertyKey == "brightness");
            var second = displayDevices[1].Channels.First(c => c.PropertyKey == "brightness");
            var firstValue = first.Value;
            await SetTestSliderAsync(desktopPanel.Content, "desktop-monitor/" + Uri.EscapeDataString(displayDevices[1].Id) + "/brightness", 28);
            if (second.Value != 28 || first.Value != firstValue) throw new InvalidOperationException("M2 individual desktop slider was not isolated.");
            state.Settings.DesktopRows.Add("monitor/offline-screen/brightness");
            await ClickMode();
            if (!state.Settings.GroupDesktopMonitors || desktopPanel.RowCount != 2) throw new InvalidOperationException("Overall desktop mode did not collapse to one row per feature.");
            await SetTestSliderAsync(desktopPanel.Content, "desktop-monitor/all/brightness", 41);
            if (first.Value != 41 || second.Value != 41) throw new InvalidOperationException("Desktop mode toggle did not restore cross-model control.");
            await ClickMode();
            if (!state.Settings.DesktopRows.Contains("monitor/offline-screen/brightness")) throw new InvalidOperationException("Offline desktop selection was lost on mode switch.");
            var mode = Descendants<ComboBox>(SettingsPanel).Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(c) == "setting-desktop-mode");
            mode.SelectedIndex = 1; await Task.Delay(100);
            if (!state.Settings.GroupDesktopMonitors || desktopPanel.RowCount != 2) throw new InvalidOperationException("Settings and desktop mode were not synchronized.");
        }
        finally
        {
            foreach (var entry in saved) ControlOperations.Apply(new[] { entry.Key }, entry.Value);
            state.Settings.GroupDesktopMonitors = true; state.Settings.DesktopRows = originalRows;
            state.Settings.DesktopIndividualRows = state.Settings.DesktopLinkedRows = null;
            state.Settings.DesktopMaxRows = originalLimit;
            RefreshDesktopPanel(); SynchronizeValues();
        }
        StartupLog.Write("PASS: desktop mode button and settings synchronized; M2 remains available beyond row limit; individual isolation and offline selections preserved.");
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
