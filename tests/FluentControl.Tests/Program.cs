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
Directory.CreateDirectory(directory);
try
{
    var statePath = Path.Combine(directory, "state.json");
    var store = new UserStateStore(statePath);
    Check(store.State.Settings.CloseToTray && !store.State.Settings.DesktopPanelEnabled, "Safe settings defaults.");
    store.State.Profiles.Add(new ControlProfile { Id = "one", Name = "Reading", Values = new() { ["monitor/A/brightness"] = new SavedValue { Value = 42 } } });
    store.State.Profiles.Add(new ControlProfile { Id = "two", Name = "Gaming" });
    store.State.Settings.Language = "en-US"; store.State.Settings.DesktopMaxRows = 3;
    store.State.Settings.DesktopRows = new() { "monitor/all/brightness" }; store.Save();
    var restored = new UserStateStore(statePath);
    Check(restored.State.Settings.Language == "en-US" && restored.State.Settings.DesktopMaxRows == 3, "Settings round trip.");
    Check(restored.State.Profiles[0].Values["monitor/A/brightness"].Value == 42, "Profile values round trip.");
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
