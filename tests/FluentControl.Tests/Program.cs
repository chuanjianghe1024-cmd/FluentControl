using FluentControl.Services;
using Microsoft.Win32;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static ControlChannel Channel(string name, double initial, Action<double> write, Func<double>? read = null) => new()
{
    Name = name, Detail = "", Glyph = "", Value = initial, Write = write, Read = read
};

Strings.ValidateCatalog();
Check(Strings.SupportedLanguages.Count == 8, "Eight supported locales.");
Check(Strings.ResolveLanguage("zh-HK") == "zh-TW" && Strings.ResolveLanguage("es-MX") == "es-ES", "Regional language fallback.");
foreach (var locale in Strings.SupportedLanguages)
{
    Strings.SetLanguage(locale);
    Check(!Strings.F("已更新{0}", "{0} updated", "TEST").Contains("{0}"), "Translated format placeholders.");
    Check(!string.IsNullOrWhiteSpace(Strings.AppName), "Localized application title.");
}
var sent = new List<double>();
var first = Channel("M1", 20, x => sent.Add(x));
var failed = Channel("M2", 40, _ => throw new IOException("disconnected"));
var last = Channel("M3", 60, x => sent.Add(x));
var errors = ControlOperations.Apply(new[] { first, failed, last }, 75);
Check(sent.SequenceEqual(new[] { 75d, 75d }), "A failed display must not prevent later displays from updating.");
Check(errors.Count == 1 && errors[0].Contains("M2"), "Failure must identify the affected channel.");
Check(first.Value == 75 && failed.Value == 40 && last.Value == 75, "Failed writes must not overwrite the last known value.");
ControlOperations.Apply(new[] { first }, 200);
Check(first.Value == 100 && last.Value == 75, "Individual writes must be clamped and isolated.");
var readback = Channel("readback", 20, _ => { }, () => 42);
ControlOperations.Apply(new[] { readback }, 50);
Check(readback.Value == 42, "Readback must reflect the device value.");

var directory = Path.Combine(Path.GetTempPath(), "FluentControl-tests-" + Guid.NewGuid());
var capabilities = VcpCapabilities.Parse("(model(Test) vcp(04 10 12 60(0F 11) 72(05 78 FB 50 64 78 8C) 8D CA(01 02 03) C0 C9 D6(01 04) E1) mccs_ver(2.2))");
Check(capabilities.HasVcpSection && capabilities.Features[0x60].SequenceEqual(new byte[] { 15, 17 }), "Nested capability lists must preserve input values.");
Check(!VcpCapabilities.Parse("(vcp(60(0F").HasVcpSection && !VcpCapabilities.Parse("vcp(ZZ)").HasVcpSection, "Malformed capabilities must not authorize writes.");
var replies = new Dictionary<byte, VcpReply> { [0x10] = new(40, 200), [0x60] = new(15,0), [0x72] = new(0x7800,0), [0x8D] = new(0x0202,0x0202), [0xCA] = new(0x0202,0), [0xC0] = new(42,1), [0xC9] = new(0x0102,0), [0xD6] = new(1,0) };
var writes = new List<(byte Code, uint Value)>();
var discovered = VcpDiscovery.Discover("TEST", capabilities, c => replies.TryGetValue(c, out var r) ? r : null, (c,v) => { writes.Add((c,v)); if (replies.TryGetValue(c, out var r)) replies[c] = r with { Current = v }; });
Check(writes.Count == 0, "Capability discovery must never send write commands, including reset.");
var bright = discovered.Single(x => x.Definition.Key == "brightness").Channel!;
Check(bright.Value == 20, "Native brightness normalizes to percent.");
ControlOperations.Apply(new[] { bright }, 50);
Check(writes.Last() == ((byte)0x10, 100u) && bright.Value == 50, "Brightness write and readback use the native maximum.");
var inputChannel = discovered.Single(x => x.Definition.Key == "input").Channel!;
Check(ControlOperations.Apply(new[] { inputChannel }, 18).Count == 1 && !writes.Any(x => x.Code == 0x60), "Unadvertised input ports must never be sent.");
ControlOperations.Apply(new[] { discovered.Single(x => x.Definition.Key == "monitor-mute").Channel! }, 1);
Check(writes.Last() == ((byte)0x8D, 0x0201u), "Mute must preserve screen blanking field.");
ControlOperations.Apply(new[] { discovered.Single(x => x.Definition.Key == "osd").Channel! }, 1);
Check(writes.Last() == ((byte)0xCA, 0x0201u), "OSD must preserve the power-button field.");
var gamma = discovered.Single(x => x.Definition.Key == "gamma").Channel!;
Check(gamma.Options!.Any(x => x.Value == 0x7800 && x.Label == "2.20") && !gamma.Options.Any(x => x.Value == 5), "Gamma metadata must not be mistaken for writable presets.");
Check(discovered.Single(x => x.Definition.Key == "usage").Information == "65578 h", "Usage counter must combine high and low words.");
Check(discovered.Single(x => x.Definition.Key == "factory-reset").Channel is { CanSave: false, IsAction: true } && discovered.Single(x => x.Definition.Key == "power").Channel is { CanSave: false }, "Destructive and power commands excluded from scenes.");
Check(discovered.Any(x => x.Definition.Code == 0xE1 && x.Channel is null), "Private features remain visible as adapter placeholders.");
var mapping = new BrightnessMapping { Enabled = true, Minimum = 10, Maximum = 90, Offset = 5, Curve = 2 };
Check(mapping.ToDevice(50) == 35 && Math.Abs(mapping.ToLinked(35) - 50) < .001, "Per-monitor nonlinear mapping is invertible before clipping.");
bright.LinkedToDevice = mapping.ToDevice; bright.DeviceToLinked = mapping.ToLinked;
ControlOperations.Apply(new[] { bright }, 50, true); Check(bright.Value == 35, "Linked writes apply calibration.");
ControlOperations.Apply(new[] { bright }, 50); Check(bright.Value == 50, "Individual writes bypass calibration.");
var publicProfile = new SharedMonitorProfile { Name = "Office", Monitors = new() { new() { Slot = "left", ModelId = "DEL1234", Values = new() { ["brightness"] = 40, ["input"] = 15 }, Brightness = mapping } } };
var sharedJson = ProfileExchange.Serialize(publicProfile);
Check(ProfileExchange.Parse(sharedJson).Monitors[0].Brightness!.Curve == 2, "Portable model profile round trip.");
Check(ModelIdentity.FromDevicePath(@"\\?\DISPLAY#DEL1234#SERIAL_AND_MACHINE_DATA#{GUID}") == "DEL1234", "Shared model identity must exclude local instance/serial data.");
foreach (var bad in new[] { sharedJson.Replace("\"version\": 1", "\"version\": 99"), sharedJson.Replace("\"brightness\"", "\"factory-reset\""), sharedJson.Replace("\"brightness\": 40", "\"brightness\": 101"), sharedJson.Replace("DEL1234", "private-instance") })
{
    var rejectedProfile = false; try { ProfileExchange.Parse(bad); } catch { rejectedProfile = true; }
    Check(rejectedProfile, "Invalid or unsafe shared profiles must be rejected.");
}
Check(!ProfileExchange.CanApply(inputChannel, 18) && ProfileExchange.CanApply(inputChannel, 15), "Import must recheck target capabilities.");
Console.WriteLine("PASS: VCP discovery, enum/range guards, gamma byte packing, safe profiles, portable imports and brightness mapping.");
Directory.CreateDirectory(directory);
try
{
    var statePath = Path.Combine(directory, "state.json");
    var store = new UserStateStore(statePath);
    Check(store.State.Settings.CloseToTray && !store.State.Settings.DesktopPanelEnabled, "Safe settings defaults.");
    store.State.Profiles.Add(new ControlProfile { Id = "one", Name = "Reading", Values = new() { ["monitor/A/brightness"] = new SavedValue { Value = 42 } } });
    store.State.Profiles.Add(new ControlProfile { Id = "two", Name = "Gaming" });
    store.State.Settings.Language = "ja-JP"; store.State.Settings.DesktopMaxRows = 3;
    store.State.Settings.DesktopOpacity = 25; store.State.Settings.DesktopWidth = 480; store.State.Settings.DesktopHeight = 250;
    store.State.Settings.DesktopRows = new() { "monitor/all/brightness" }; store.Save();
    store.State.Settings.HideUnavailableMonitorControls = true;
    store.State.Profiles[0].BrightnessMappings["monitor-A"] = mapping.Copy(); store.Save();
    var restored = new UserStateStore(statePath);
    Check(restored.State.Settings.Language == "ja-JP" && restored.State.Settings.DesktopMaxRows == 3, "Settings round trip.");
    Check(restored.State.Settings.DesktopOpacity == 25 && restored.State.Settings.DesktopWidth == 480 && restored.State.Settings.DesktopHeight == 250, "Panel appearance and geometry round trip.");
    Check(restored.State.Profiles[0].Values["monitor/A/brightness"].Value == 42, "Profile values round trip.");
    Check(restored.State.Settings.HideUnavailableMonitorControls && restored.State.Profiles[0].BrightnessMappings["monitor-A"].Curve == 2, "Filter and scene calibration persistence.");
    Check(UserStateStore.NextIndex(restored.State.Profiles, "one", -1) == 1, "Previous profile wraps.");
    Check(UserStateStore.NextIndex(restored.State.Profiles, "two", 1) == 0, "Next profile wraps.");
    Check(UserStateStore.NextIndex(Array.Empty<ControlProfile>(), null, 1) == -1, "Empty profiles handled.");
    var path = Path.Combine(directory, "names.json");
    var names = new MonitorPreferences(path);
    Check(names.GetOrAdd("monitor-A").Label == "M1", "First monitor label.");
    Check(names.GetOrAdd("monitor-B").Label == "M2", "Second monitor label.");
    names.Rename("monitor-A", "  左屏  ");
    var loaded = new MonitorPreferences(path);
    Check(loaded.GetOrAdd("monitor-B").Label == "M2", "Enumeration reordering must retain monitor labels.");
    Check(loaded.GetOrAdd("monitor-A").DisplayName == "左屏", "Name must survive restart.");
    Check(loaded.GetOrAdd("monitor-C").Label == "M3", "New monitors must not reuse retained labels.");
    var rejected = false;
    try { loaded.Rename("monitor-A", " "); } catch (ArgumentException) { rejected = true; }
    Check(rejected && new MonitorPreferences(path).GetOrAdd("monitor-A").DisplayName == "左屏", "Invalid names must preserve saved settings.");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("PASS: linked/individual controls, partial failures, readback and persistent monitor names.");

if (args.Contains("--native"))
{
    var originalSpeed = MouseService.GetSpeed();
    try
    {
        var target = originalSpeed == 20 ? 19 : originalSpeed + 1;
        MouseService.SetSpeed(target);
        Check(MouseService.GetSpeed() == target, "Native mouse speed readback.");
    }
    finally { MouseService.SetSpeed(originalSpeed); }
    using var cursors = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors");
    using var accessibility = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility");
    var originalBase = cursors.GetValue("CursorBaseSize");
    var originalLevel = accessibility.GetValue("CursorSize");
    var originalBaseKind = originalBase is null ? RegistryValueKind.DWord : cursors.GetValueKind("CursorBaseSize");
    var originalLevelKind = originalLevel is null ? RegistryValueKind.DWord : accessibility.GetValueKind("CursorSize");
    var level = MouseService.GetPointerSize();
    try
    {
        var target = level < 15 ? Math.Floor(level) + 1 : 14;
        MouseService.SetPointerSize(target);
        Check(MouseService.GetPointerBaseSize() == 32 + (target - 1) * 16, "Pointer base size persisted.");
        Check(accessibility.GetValue("CursorSize") is int step && step == target, "Accessibility size synchronized.");
    }
    finally
    {
        MouseService.SetPointerSize(level);
        if (originalBase is null) cursors.DeleteValue("CursorBaseSize", false); else cursors.SetValue("CursorBaseSize", originalBase, originalBaseKind);
        if (originalLevel is null) accessibility.DeleteValue("CursorSize", false); else accessibility.SetValue("CursorSize", originalLevel, originalLevelKind);
    }
    Console.WriteLine("PASS: native Windows mouse speed and pointer size; original settings restored.");
}
