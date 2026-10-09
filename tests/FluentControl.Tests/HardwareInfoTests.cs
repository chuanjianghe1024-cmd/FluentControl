using FluentControl.Services;

internal static class HardwareInfoTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var device = new MonitorDevice { Id = "DISPLAY\\DEL1234\\PRIVATE-SERIAL", ModelId = "DEL1234", Model = "Example display", Connection = "PRIVATE-CONNECTION", CapabilitiesText = "(model(Example) serial(PRIVATE-SERIAL) mccs_ver(2.2) vcp(10 14(05 08) C9 E1))" };
        var caps = VcpCapabilities.Parse(device.CapabilitiesText);
        device.Features.AddRange(VcpDiscovery.Discover(device.Id, caps, code => code switch { 0x10 => new(30, 100), 0x14 => new(5, 0), 0xC9 => new(0x103, 0), _ => null }, (_, _) => throw new Exception("Discovery wrote hardware")));
        device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
        var info = MonitorHardwareInfo.Capture(device)!; info.Validate();
        Check(info.Firmware == "1.3" && info.MccsVersion == "2.2" && info.ManufacturerCode == "DEL", "Portable hardware versions and manufacturer.");
        Check(info.Controls.Single(c => c.Key == "color-preset").Options.SequenceEqual(new double[] { 5, 8 }), "Capabilities preserve exact options.");
        Check(info.Controls.Single(c => c.Key == "vcp-e1") is { Advertised: true, Writable: false, Readable: false }, "Private advertised controls are not assumed writable.");
        var profile = new ControlProfile { Name = "Office", Monitors = new() { [device.Id] = new() { ModelId = device.ModelId, Hardware = info } }, Values = new() { [ProfileGroups.MonitorKey(device.Id, "brightness")] = new() { Value = 30 } } };
        var state = new UserState { Profiles = new() { profile } }; ProfileGroups.Normalize(state);
        var json = ProfileExchange.SerializeBundle(ProfileBundles.Export(state, state.Profiles, "Modes"));
        Check(!json.Contains("PRIVATE") && !json.Contains("CapabilitiesText"), "Raw identity and capabilities text must not escape.");
        var bundle = ProfileExchange.ParseBundle(json);
        var imported = ProfileBundles.Import(bundle, new Dictionary<string, string>(), "offline", Array.Empty<string>())[0];
        Check(imported.Monitors.Values.Single().Hardware!.Controls.Count == info.Controls.Count, "Offline import preserves hardware observations.");
        ProfileUpdates.Merge(imported, new Dictionary<string, SavedValue>(), new Dictionary<string, MonitorDescriptor>(), new Dictionary<string, BrightnessMapping>());
        Check(imported.Monitors.Values.Single().Hardware!.Firmware == "1.3", "Offline update preserves hardware observations.");
        var copy = info.Copy(); copy.Controls[0].Options.Add(999);
        Check(!info.Controls[0].Options.Contains(999), "Metadata copies are deep.");
        foreach (var invalid in new[] { json.Replace("\"version\": 1", "\"version\": 2"), json.Replace("\"mccsVersion\": \"2.2\"", "\"mccsVersion\": \"C:\\\\secret\""), json.Replace("\"minimum\": 0", "\"minimum\": -1") })
        {
            var rejected = false; try { ProfileExchange.ParseBundle(invalid); } catch { rejected = true; }
            Check(rejected, "Invalid capabilities must be rejected.");
        }
        var legacy = new SharedProfileBundle { Name = "Legacy", Groups = new() { new() { Name = "Local", Profiles = new() { new() { Name = "Office", Monitors = new() { new() { Slot = "one", ModelId = "DEL1234", Values = new() { ["brightness"] = 30 } } } } } } } };
        Check(ProfileExchange.ParseBundle(ProfileExchange.SerializeBundle(legacy)).Groups[0].Profiles[0].Monitors[0].Hardware is null, "Old v2 bundles remain valid without hardware metadata.");
        Console.WriteLine("PASS: portable hardware capabilities, offline retention, privacy, validation and old bundles");
    }
}
