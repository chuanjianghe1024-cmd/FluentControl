using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
namespace FluentControl;

public sealed partial class MainWindow
{
    private async Task CheckSystemAudioAsync()
    {
        var backend = (UiTestData.AudioBackend)audio!;
        var output = audioChannels.Single(c => c.DeviceId == SystemAudioControls.OutputId);
        var input = audioChannels.Single(c => c.DeviceId == SystemAudioControls.InputId);
        var beforeOutput = backend.Read(SystemAudioTarget.Output);
        var beforeInput = backend.Read(SystemAudioTarget.Input);
        async Task WaitForAudioAsync(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(25);
        }
        try
        {
            Navigation.SelectedItem = Navigation.MenuItems[1];
            if (AudioRows.Children.Count != 2 || !Descendants<TextBlock>(AudioRows).Any(x => x.Text == Strings.T("音量", "Volume")) ||
                !Descendants<TextBlock>(AudioRows).Any(x => x.Text == Strings.T("输入", "Input")) || Descendants<Expander>(AudioPanel).Any())
                throw new InvalidOperationException("Audio must expose only Volume and Input, without endpoint names or lists.");
            var profile = new ControlProfile { Name = "System audio fixture", Values = CaptureProfile() };
            if (!profile.Values.ContainsKey(SystemAudioControls.OutputKey) || !profile.Values.ContainsKey(SystemAudioControls.InputKey) || profile.Values.Keys.Any(SystemAudioControls.IsLegacyKey))
                throw new InvalidOperationException("Audio profile keys depend on endpoint identities.");
            backend.ChangeDefault(SystemAudioTarget.Output, 18, true);
            backend.ChangeDefault(SystemAudioTarget.Input, 24, true);
            await WaitForAudioAsync(() => output.Value == 18 && output.IsMuted && input.Value == 24 && input.IsMuted);
            if (output.Value != 18 || !output.IsMuted || input.Value != 24 || !input.IsMuted)
                throw new InvalidOperationException("System audio change notifications were not reflected in the controls.");
            // The old snapshot must target the new defaults through the same keys.
            await ApplyProfileAsync(profile);
            if (backend.Read(SystemAudioTarget.Output) != beforeOutput || backend.Read(SystemAudioTarget.Input) != beforeInput)
                throw new InvalidOperationException("Global profile failed to apply volume/mute to current defaults.");
            backend.InputAvailable = false; backend.ChangeDefault(SystemAudioTarget.Output, 22, false);
            await WaitForAudioAsync(() => !input.IsAvailable && output.Value == 22);
            if (!output.IsAvailable || input.IsAvailable || Descendants<Slider>(AudioRows).Count(s => s.IsEnabled) != 1 || CaptureProfile().ContainsKey(SystemAudioControls.InputKey))
                throw new InvalidOperationException("Missing input must be disabled without blocking output or saving a stale input value.");
            var legacy = new ControlProfile { Name = "Legacy audio", Values = new() { ["audio/old-device/volume"] = new() { Value = 99 } } };
            await ApplyProfileAsync(legacy);
            if (output.Value != 22 || !profileDirty || Status.Severity != Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning)
                throw new InvalidOperationException("Legacy endpoint values were rebound or skipped silently.");
            StartupLog.Write("PASS: two system audio controls, live default/volume/mute changes, logical profile keys, missing input and legacy profile guard");
        }
        finally
        {
            backend.InputAvailable = true;
            backend.ChangeDefault(SystemAudioTarget.Output, beforeOutput.Volume, beforeOutput.Muted);
            backend.ChangeDefault(SystemAudioTarget.Input, beforeInput.Volume, beforeInput.Muted);
            await RefreshSystemAudioAsync();
            Navigation.SelectedItem = Navigation.MenuItems[0];
        }
    }

    private async Task CheckWindowSizingAsync()
    {
        var area = WindowPlacement.Area(this);
        var scale = WindowPlacement.Scale(this);
        var expected = WindowGeometry.ClientSize(1120, 780, scale, area,
            AppWindow.Size.Width - AppWindow.ClientSize.Width, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        if (Math.Abs(AppWindow.ClientSize.Width - expected.Width) > 2 || Math.Abs(AppWindow.ClientSize.Height - expected.Height) > 2)
            throw new InvalidOperationException("Main window did not convert its logical client size to the current display DPI.");
        var monitor = new MonitorDevice
        {
            Id = "ui-identification-layout", Model = "Layout fixture", Connection = "test",
            Left = area.X, Top = area.Y, Width = area.Width, Height = area.Height,
            Preference = new() { Label = "M123", Alias = "四千分辨率显示器缩放布局验证名称，长文字应自动换行并且完整显示" }
        };
        var marker = new MonitorIdentificationWindow(monitor, "Identification layout fixture", Root.ActualTheme);
        try
        {
            marker.Activate();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while ((!marker.NameLabel.IsLoaded || marker.NameLabel.ActualHeight < 1) && DateTime.UtcNow < deadline) await Task.Delay(30);
            await Task.Delay(100);
            var content = (FrameworkElement)marker.Content;
            content.UpdateLayout();
            if (marker.NameLabel.ActualHeight < 40 || marker.NameLabel.IsTextTrimmed || marker.IndexLabel.IsTextTrimmed)
                throw new InvalidOperationException("Long identification text did not wrap without trimming.");
            foreach (var label in new[] { marker.IndexLabel, marker.NameLabel })
            {
                var origin = label.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point());
                if (origin.X < -1 || origin.Y < -1 || origin.X + label.ActualWidth > content.ActualWidth + 1 || origin.Y + label.ActualHeight > content.ActualHeight + 1)
                    throw new InvalidOperationException("Identification text was clipped by the window bounds.");
            }
            StartupLog.Write($"PASS: native DPI client sizing and wrapped identification text fit their window bounds; runner scale={scale}.");
        }
        finally { marker.Close(); Activate(); }
    }

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
        await CheckReadOnlyOsdAndLibraryAsync();
        await CheckRepeatableOsdCommandsAsync();
        await CheckSystemAudioAsync();
        await CheckAdapterCaptureAsync();
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
        if (!((IEnumerable<ControlProfile>)ProfilePicker.ItemsSource).Any(p => p.Id == profile.Id)) throw new InvalidOperationException("Global selector omitted a legacy-group scene.");
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
    private async Task CheckAdapterCaptureAsync()
    {
        async Task InvokeReadyAsync(Button button)
        {
            // ContentDialog opening and TextChanged can defer enablement until
            // the next XAML layout pass. Exercise the real enabled button.
            button.StartBringIntoView();
            var peer = new ButtonAutomationPeer(button);
            for (var attempt = 0; attempt < 30 && (!button.IsLoaded || !peer.IsEnabled()); attempt++) await Task.Delay(100);
            var id = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button);
            if (!button.IsLoaded || !peer.IsEnabled()) throw new InvalidOperationException("Diagnostic button did not become ready: " + id);
            StartupLog.Write("Invoking diagnostic button: " + id);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(50);
        }
        var device = displayDevices[0]; var channel = device.Channels.First(c => c.PropertyKey == "color-preset"); var original = channel.Value;
        var show = ShowMonitorAdaptationAsync(device);
        await Task.Delay(150);
        try
        {
            var content = monitorAdaptationDialog?.Content as ScrollViewer ?? throw new InvalidOperationException("Adaptation dialog did not open.");
            Button Button(string id) => Descendants<Button>(content).Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == id);
            if (Button("diagnostic-sample").IsEnabled || Button("diagnostic-export").IsEnabled) throw new InvalidOperationException("Diagnostics enabled without a baseline.");
            await InvokeReadyAsync(Button("diagnostic-baseline")); await monitorDiagnosticCapture;
            if (monitorDiagnosticBaseline is null || !Button("diagnostic-export").IsEnabled || monitorDiagnosticBaseline.Readings.Single(r => r.Code == 0x14).Current != original)
                throw new InvalidOperationException("Diagnostic baseline or export unavailable.");
            var label = Descendants<TextBox>(content).Single(t => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "diagnostic-label");
            label.Text = "实体菜单：9300 K";
            // Simulate a physical OSD change, bypassing the FC UI's cached value.
            var changed = original == 8 ? 5u : 8u; channel.Write(changed);
            await InvokeReadyAsync(Button("diagnostic-sample")); await monitorDiagnosticCapture;
            if (monitorDiagnosticObservations.Count != 1 || !monitorDiagnosticObservations[0].Differences.Any(d => d.Code == 0x14 && d.After?.Current == changed))
                throw new InvalidOperationException("Labeled raw OSD change was not recorded.");
            var saved = state.SelectedProfileId;
            await ApplyProfileAsync(new ControlProfile { Name = "Must stay paused", Values = CaptureProfile() });
            if (saved != state.SelectedProfileId) throw new InvalidOperationException("Profile switched during adaptation capture.");
        }
        finally { channel.Write(original); CloseMonitorAdaptation(); await show; }
        // Closing while waiting for the lifecycle gate must cancel before any capture.
        show = ShowMonitorAdaptationAsync(device); await Task.Delay(100);
        await gate.WaitAsync();
        try
        {
            var content = monitorAdaptationDialog!.Content as ScrollViewer;
            var button = Descendants<Button>(content!).Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "diagnostic-baseline");
            await InvokeReadyAsync(button); CloseMonitorAdaptation(); await Task.Delay(100);
        }
        finally { gate.Release(); }
        await show;
        if (monitorDiagnosticBaseline is not null || monitorAdaptationDialog is not null) throw new InvalidOperationException("Closed capture retained work or dialog state.");
        StartupLog.Write("PASS: adaptation UI baseline, physical-menu difference, export readiness, paused profiles and close/cancel lifecycle.");
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
    private async Task CheckReadOnlyOsdAndLibraryAsync()
    {
        var fixture = new MonitorDevice { Id = "ui-readonly-osd", ModelId = "TST0003", Model = "Unsaved OSD fixture", Connection = "test" };
        var writes = 0;
        fixture.Features.AddRange(VcpDiscovery.Discover(fixture.Id, VcpCapabilities.Parse("(vcp(10 12 FD))"),
            code => code == 0xCA ? new VcpReply(2, 2) : null, (_, _) => writes++));
        var hide = state.Settings.HideUnavailableMonitorControls;
        var presets = state.MonitorPresets.ToList(); var model = libraryModel; var search = librarySearch;
        var navigation = Navigation.SelectedItem; var mode = DisplayMode.SelectedIndex;
        displayDevices.Add(fixture);
        async Task ExpandAsync(DependencyObject root)
        {
            await Task.Delay(100);
            foreach (var expander in Descendants<Expander>(root).ToArray()) expander.IsExpanded = true;
            await Task.Delay(100);
        }
        bool Has(DependencyObject root, string id) => Descendants<FrameworkElement>(root)
            .Any(e => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == id);
        try
        {
            state.Settings.HideUnavailableMonitorControls = true;
            RenderMonitorControls(generation);
            foreach (var index in new[] { 0, 1 })
            {
                DisplayMode.SelectedIndex = index;
                var root = index == 0 ? MonitorRows : CombinedRows;
                await ExpandAsync(root);
                if (!Has(root, "native-osd-status-" + fixture.Id) || !Has(root, "readonly-" + fixture.Id + "-osd"))
                    throw new InvalidOperationException("Read-only OSD status disappeared when unavailable controls were hidden.");
            }
            ShowMonitorOsd(fixture);
            var osd = (DependencyObject)monitorOsd!.Content;
            await ExpandAsync(osd);
            if (!Has(osd, "native-osd-status-" + fixture.Id) || !Has(osd, "readonly-" + fixture.Id + "-osd"))
                throw new InvalidOperationException("FC menu omitted native adaptation status or the readable OSD state.");
            CloseMonitorOsd();
            state.MonitorPresets.Clear(); libraryModel = librarySearch = "";
            Navigation.SelectedItem = Navigation.MenuItems[3]; BuildPresetLibrary();
            await ExpandAsync(PresetLibraryPanel);
            if (displayDevices.Any(d => !Has(PresetLibraryPanel, "library-save-" + d.Id)) || state.MonitorPresets.Count != 0)
                throw new InvalidOperationException("Connected models without presets need visible save entries without creating empty presets.");
            if (writes != 0 || fixture.Channels.Count != 0 || fixture.Features.Any(f => f.Channel is not null))
                throw new InvalidOperationException("Read-only OSD discovery or rendering must not grant writes or send commands.");
            StartupLog.Write("PASS: unadvertised read-only OSD state in individual/overall/software menus and unsaved connected models in the library.");
        }
        finally
        {
            CloseMonitorOsd(); displayDevices.Remove(fixture);
            state.Settings.HideUnavailableMonitorControls = hide;
            state.MonitorPresets.Clear(); state.MonitorPresets.AddRange(presets);
            libraryModel = model; librarySearch = search; BuildPresetLibrary();
            Navigation.SelectedItem = navigation; DisplayMode.SelectedIndex = mode; RenderMonitorControls(generation);
        }
    }
    private async Task CheckRepeatableOsdCommandsAsync()
    {
        var fixture = new MonitorDevice { Id = "ui-osd-command", ModelId = "TST0004", Model = "OSD command fixture", Connection = "test" };
        var writes = new List<double>();
        var last = 1d; var rejectWrite = false;
        // This simulated channel omits the confirmation dialog so the regression
        // exercises selection events. Real discovery retains RequiresConfirmation.
        var channel = new ControlChannel
        {
            Name = "OSD", Detail = "VCP 0xCA", Glyph = "", PropertyKey = "osd", DeviceId = fixture.Id, VcpCode = 0xCA,
            Value = 1, Minimum = 1, Maximum = 3, CanSave = false,
            Options = new[] { new ControlOption(1, "Disable"), new ControlOption(2, "Enable"), new ControlOption(3, "Disable buttons") },
            Write = value => { writes.Add(value); if (rejectWrite) throw new IOException("simulated write failure"); last = value; },
            Read = () => last == 2 ? throw new IOException("simulated readback failure") : last
        };
        fixture.Channels.Add(channel);
        fixture.Features.Add(new() { Definition = VcpCatalog.Find("osd")!, Channel = channel });
        var mode = DisplayMode.SelectedIndex;
        displayDevices.Add(fixture);
        async Task ExerciseAsync(DependencyObject root, string rowId)
        {
            foreach (var expander in Descendants<Expander>(root).ToArray()) expander.IsExpanded = true;
            await Task.Delay(80);
            var row = Descendants<FrameworkElement>(root).Single(e => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == rowId);
            var combo = Descendants<ComboBox>(row).Single();
            var options = (IReadOnlyList<ControlOption>)combo.ItemsSource;
            if (combo.SelectedItem is not null || writes.Count != 0) throw new InvalidOperationException("OSD command picker must not select or send the initial disabled state.");
            foreach (var value in new[] { 2d, 1d, 2d, 2d, 1d })
            {
                var count = writes.Count;
                combo.SelectedItem = options.Single(o => o.Value == value);
                await WaitForWritesAsync();
                if (writes.Count != count + 1 || writes.Last() != value || combo.SelectedItem is not null || !combo.IsEnabled)
                    throw new InvalidOperationException("OSD command cannot be replayed after failed readback or resets caused another write.");
            }
            rejectWrite = true;
            combo.SelectedItem = options.Single(o => o.Value == 1);
            await WaitForWritesAsync();
            if (combo.SelectedItem is not null || !combo.IsEnabled) throw new InvalidOperationException("Failed OSD write left the picker stuck.");
            rejectWrite = false;
            var before = writes.Count;
            combo.SelectedItem = options.Single(o => o.Value == 1);
            await WaitForWritesAsync();
            if (writes.Count != before + 1 || combo.SelectedItem is not null) throw new InvalidOperationException("OSD command cannot be retried after write failure.");
            writes.Clear();
        }
        try
        {
            RenderMonitorControls(generation);
            foreach (var index in new[] { 0, 1 })
            {
                DisplayMode.SelectedIndex = index;
                await ExerciseAsync(index == 0 ? MonitorRows : CombinedRows, "individual-" + fixture.Id + "-osd");
            }
            ShowMonitorOsd(fixture);
            await ExerciseAsync((DependencyObject)monitorOsd!.Content, "osd-osd");
            StartupLog.Write("PASS: OSD command replay after failed readback and failed writes in individual, overall and FC menus; no state-driven extra writes.");
        }
        finally
        {
            CloseMonitorOsd(); displayDevices.Remove(fixture);
            DisplayMode.SelectedIndex = mode; RenderMonitorControls(generation);
        }
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
        var previousPage = Navigation.SelectedItem;
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
            // The settings content now lives in its own scroll host and is
            // realized only after its page/section is visible, as in normal use.
            Navigation.SelectedItem = Navigation.SettingsItem;
            SelectSettingsSection("desktop");
            await Task.Delay(100); SettingsPanel.UpdateLayout();
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
            Navigation.SelectedItem = previousPage;
        }
        StartupLog.Write("PASS: desktop mode button and settings synchronized; M2 remains available beyond row limit; individual isolation and offline selections preserved.");
    }
    private async Task CheckPanelTextSwitchAsync()
    {
        Navigation.SelectedItem = Navigation.SettingsItem; BuildSettings(); SelectSettingsSection("desktop");
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
        var groups = state.Groups; var profiles = state.Profiles; var selected = state.SelectedProfileId; var group = state.SelectedGroupId;
        var appFilter = applicationFilter; var oldPresets = state.MonitorPresets; var active = state.ActiveMonitorPresets;
        var channels = AllChannels(); var original = channels.ToDictionary(p => p.Key, p => p.Value.Value);
        var firstGroup = new ProfileGroup { Id = ProfileGroup.LocalId }; var otherGroup = new ProfileGroup { Name = "Legacy group" };
        var first = CaptureGlobalProfile("Global first", new()); var second = CaptureGlobalProfile("Global second", new()); second.GroupId = otherGroup.Id;
        var brightnessKey = ProfileGroups.MonitorKey(displayDevices[0].Id, "brightness");
        var microphone = channels.First(p => p.Key == SystemAudioControls.InputKey);
        var mouse = channels.First(p => p.Key == "mouse/mouse-speed");
        first.Values[brightnessKey].Value = 21; second.Values[brightnessKey].Value = 42;
        first.Values[microphone.Key].Value = 25; second.Values[microphone.Key].Value = 75;
        first.Values[mouse.Key].Value = 4; second.Values[mouse.Key].Value = 12;
        try
        {
            state.Groups = new() { firstGroup, otherGroup }; state.Profiles = new() { first, second }; state.MonitorPresets = new();
            MonitorPresetLibrary.AttachSnapshots(state, first); MonitorPresetLibrary.AttachSnapshots(state, second);
            await ApplyProfileAsync(first); applicationFilter = "no matches"; RefreshProfiles(); desktopPanel!.SetUnlocked(false);
            var next = Descendants<Button>(desktopPanel.Content).Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "desktop-next-profile");
            void Click() => ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
            Click(); await Task.Delay(75);
            if (state.SelectedProfileId != first.Id) throw new InvalidOperationException("Locked global navigation accepted input.");
            desktopPanel.SetUnlocked(true); Click(); await Task.Delay(75); while (applyingProfile) await Task.Delay(25);
            if (state.SelectedProfileId != second.Id || channels[brightnessKey].Value != 42 || microphone.Value.Value != 75 || mouse.Value.Value != 12)
                throw new InvalidOperationException("Desktop navigation did not apply the complete global scene across legacy groups.");
            ShellCommand(3); while (applyingProfile) await Task.Delay(25);
            if (state.SelectedProfileId != first.Id || microphone.Value.Value != 25 || mouse.Value.Value != 4) throw new InvalidOperationException("Global hotkey did not restore audio and mouse.");
            var source = state.MonitorPresets.First(p => p.Monitor.ModelId == displayDevices[0].ModelId);
            var snapshot = first.Values[brightnessKey].Value;
            source.Values["brightness"] = 67;
            var untouched = displayDevices[0].Channels.First(c => c.PropertyKey == "brightness").Value;
            await ApplyMonitorPresetAsync(displayDevices[1], source, false);
            if (displayDevices[1].Channels.First(c => c.PropertyKey == "brightness").Value != 67 || displayDevices[0].Channels.First(c => c.PropertyKey == "brightness").Value != untouched || first.Values[brightnessKey].Value != snapshot)
                throw new InvalidOperationException("Cross-model preset leaked to another screen or modified a saved global snapshot.");
            state.MonitorPresets.Remove(source); await ApplyProfileAsync(first);
            if (channels[brightnessKey].Value != snapshot) throw new InvalidOperationException("Deleting a library preset invalidated the global snapshot.");
            BuildPresetLibrary();
            StartupLog.Write("PASS: complete global scene navigation, audio/mouse restoration, reusable model presets and cross-model isolation");
        }
        finally
        {
            foreach (var item in original) ControlOperations.Apply(new[] { channels[item.Key] }, item.Value);
            state.Groups = groups; state.Profiles = profiles; state.SelectedProfileId = selected; state.SelectedGroupId = group;
            state.MonitorPresets = oldPresets; state.ActiveMonitorPresets = active; applicationFilter = appFilter;
            RefreshProfiles(); SynchronizeValues(); RenderMonitorControls(generation); SaveState(); desktopPanel?.SetUnlocked(false);
        }
    }
}
