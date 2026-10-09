using System.Text.Json;
namespace FluentControl.Services;

public sealed class MonitorPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceDeviceId { get; set; } = "";
    public List<string> Applications { get; set; } = new();
    public MonitorDescriptor Monitor { get; set; } = new();
    public Dictionary<string, double> Values { get; set; } = new();
    public BrightnessMapping? Brightness { get; set; }
}
public sealed record MonitorPresetPlan(IReadOnlyList<(ControlChannel Channel, double Value)> Applicable, IReadOnlyList<string> Skipped);

public static class MonitorPresetLibrary
{
    public static string GroupKey(MonitorDescriptor monitor) => ModelIdentity.IsValid(monitor.ModelId) ? monitor.ModelId : "unknown:" + monitor.Brand + ":" + monitor.ModelName;
    public static string ModelName(MonitorDescriptor monitor) => string.IsNullOrWhiteSpace(monitor.ModelName) ? monitor.ModelId.Length > 0 ? monitor.ModelId : Strings.T("未知型号", "Unknown model") : monitor.ModelName;
    public static string GroupName(MonitorDescriptor monitor) => ModelName(monitor) + (monitor.ModelId.Length > 0 && monitor.ModelId != monitor.ModelName ? " · " + monitor.ModelId : "");
    public static string SuggestedName(MonitorDescriptor monitor, string scenario, string summary)
    {
        var name = string.Join(" - ", new[] { ModelName(monitor), scenario.Trim(), summary.Trim() }.Where(s => s.Length > 0));
        return name[..Math.Min(80, name.Length)];
    }
    public static Dictionary<string, double> ValuesFor(ControlProfile profile, string id) => profile.Values
        .Where(x => ProfileGroups.TryMonitorKey(x.Key, out var device, out var key) && device == id && ProfileExchange.IsShareable(key))
        .ToDictionary(x => { ProfileGroups.TryMonitorKey(x.Key, out _, out var key); return key; }, x => x.Value.Value);
    private static string Fingerprint(MonitorPreset preset) => JsonSerializer.Serialize(new
    {
        Group = GroupKey(preset.Monitor), Values = preset.Values.OrderBy(x => x.Key).ToArray(), Brightness = preset.Brightness
    });
    public static void Migrate(UserState state)
    {
        state.MonitorPresets ??= new(); state.ActiveMonitorPresets ??= new();
        foreach (var profile in state.Profiles) profile.MonitorPresetIds ??= new();
        if (state.ProfileOrganizationVersion >= 1) return;
        foreach (var profile in state.Profiles)
        {
            foreach (var id in profile.Values.Keys.Select(key => ProfileGroups.TryMonitorKey(key, out var id, out _) ? id : null).OfType<string>().Distinct())
                if (!profile.Monitors.ContainsKey(id)) profile.Monitors[id] = state.KnownMonitors.GetValueOrDefault(id)?.Copy() ?? new() { ModelId = ModelIdentity.FromDevicePath(id) };
            AttachSnapshots(state, profile);
        }
        if (state.Profiles.FirstOrDefault(p => p.Id == state.SelectedProfileId) is { } selected)
            state.ActiveMonitorPresets = new(selected.MonitorPresetIds);
        state.ProfileOrganizationVersion = 1;
    }
    // Global profiles always keep independent values. Editing/deleting a library preset
    // cannot silently change a saved desktop scene or discard a disconnected display.
    public static void AttachSnapshots(UserState state, ControlProfile profile)
    {
        foreach (var pair in profile.Monitors)
        {
            var values = ValuesFor(profile, pair.Key); if (values.Count == 0) continue;
            var candidate = new MonitorPreset { SourceDeviceId = pair.Key, Monitor = pair.Value.Copy(), Values = values, Applications = profile.Applications.ToList(), Scenario = profile.Name, Summary = Strings.T("当前参数", "Current settings"), Brightness = profile.BrightnessMappings.GetValueOrDefault(pair.Key)?.Copy() };
            var signature = Fingerprint(candidate);
            var preferred = state.ActiveMonitorPresets.GetValueOrDefault(pair.Key);
            var match = state.MonitorPresets.OrderByDescending(p => p.Id == preferred).FirstOrDefault(p => Fingerprint(p) == signature && (p.Id == preferred || p.Scenario == candidate.Scenario && p.Applications.SequenceEqual(candidate.Applications)));
            if (match is null)
            {
                candidate.Name = ProfileGroups.UniqueName(SuggestedName(candidate.Monitor, candidate.Scenario, candidate.Summary), state.MonitorPresets.Where(p => GroupKey(p.Monitor) == GroupKey(candidate.Monitor)).Select(p => p.Name));
                state.MonitorPresets.Add(candidate); match = candidate;
            }
            profile.MonitorPresetIds[pair.Key] = match.Id;
        }
    }
    public static MonitorPreset Capture(MonitorDevice device, string scenario, string summary, IEnumerable<string> applications) => new()
    {
        SourceDeviceId = device.Id,
        Monitor = new() { ModelId = device.ModelId, ModelName = device.Model, DisplayName = device.DisplayName, Brand = ModelIdentity.Brand(device.ModelId), Hardware = MonitorHardwareInfo.Capture(device) },
        Scenario = scenario, Summary = summary, Applications = applications.ToList(), Brightness = device.Preference.Brightness.Copy(),
        Values = device.Channels.Where(c => !c.CompatibilityOnly && ProfileExchange.IsShareable(c.PropertyKey) && ProfileExchange.CanApply(c, c.Value)).ToDictionary(c => c.PropertyKey, c => c.Value)
    };
    public static MonitorPresetPlan Plan(MonitorPreset preset, MonitorDevice device)
    {
        var applicable = new List<(ControlChannel Channel, double Value)>(); var skipped = new List<string>();
        foreach (var value in preset.Values)
        {
            var channel = device.Channels.FirstOrDefault(c => c.PropertyKey == value.Key);
            if (value.Key == "temperature" && preset.Values.TryGetValue("color-preset", out var canonical) && device.Channels.Any(c => c.PropertyKey == "color-preset" && ProfileExchange.CanApply(c, canonical))) continue;
            if (channel is null || !ProfileExchange.IsShareable(value.Key) || !ProfileExchange.CanApply(channel, value.Value)) skipped.Add(value.Key);
            else applicable.Add((channel, value.Value));
        }
        return new(applicable.OrderBy(x => x.Channel.ApplyOrder).ToArray(), skipped);
    }
    public static bool SameModel(MonitorPreset preset, MonitorDevice device) => ModelIdentity.IsValid(device.ModelId) && preset.Monitor.ModelId == device.ModelId;
    public static bool MatchesCurrent(MonitorPreset preset, MonitorDevice device)
    {
        var plan = Plan(preset, device);
        return plan.Skipped.Count == 0 && plan.Applicable.Count > 0 && plan.Applicable.All(x => Math.Abs(x.Channel.Value - x.Value) < .51);
    }
    public static SharedProfileBundle Export(IEnumerable<MonitorPreset> presets, string name)
    {
        var result = new SharedProfileBundle { Name = name }; var index = 0;
        foreach (var group in presets.GroupBy(p => GroupKey(p.Monitor)))
        {
            var shared = new SharedProfileGroup { Name = GroupName(group.First().Monitor)[..Math.Min(80, GroupName(group.First().Monitor).Length)] }; var slot = "display-" + ++index;
            foreach (var preset in group)
                shared.Profiles.Add(new() { Name = preset.Name, Scenario = preset.Scenario, Summary = preset.Summary, Applications = preset.Applications.ToList(), Monitors = new() { new()
                { Slot = slot, ModelId = preset.Monitor.ModelId, ModelName = preset.Monitor.ModelName, DisplayName = preset.Monitor.DisplayName, Brand = preset.Monitor.Brand, Hardware = preset.Monitor.Hardware?.Copy(), Values = new(preset.Values), Brightness = preset.Brightness?.Copy() } } });
            result.Groups.Add(shared);
        }
        ProfileExchange.ValidateBundle(result); return result;
    }
    public static List<MonitorPreset> Import(SharedProfileBundle bundle, IEnumerable<MonitorPreset> existing, string source)
    {
        ProfileExchange.ValidateBundle(bundle);
        var all = existing.ToList(); var result = new List<MonitorPreset>();
        foreach (var scene in bundle.Groups.SelectMany(g => g.Profiles)) foreach (var monitor in scene.Monitors)
        {
            var preset = new MonitorPreset { Name = scene.Name, Scenario = scene.Scenario.Length > 0 ? scene.Scenario : scene.Name, Summary = scene.Summary, Source = source, Applications = scene.Applications.ToList(), Monitor = new() { ModelId = monitor.ModelId, ModelName = monitor.ModelName, DisplayName = monitor.DisplayName, Brand = monitor.Brand, Hardware = monitor.Hardware?.Copy() }, Values = new(monitor.Values), Brightness = monitor.Brightness?.Copy() };
            if (all.Any(p => p.Name == preset.Name && Fingerprint(p) == Fingerprint(preset) && p.Applications.SequenceEqual(preset.Applications) && p.Scenario == preset.Scenario && p.Summary == preset.Summary)) continue;
            preset.Name = ProfileGroups.UniqueName(preset.Name, all.Where(p => GroupKey(p.Monitor) == GroupKey(preset.Monitor)).Select(p => p.Name));
            result.Add(preset); all.Add(preset);
        }
        return result;
    }
}
