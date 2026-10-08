namespace FluentControl.Services;

public static class ProfileBundles
{
    public static SharedProfileBundle Export(UserState state, IEnumerable<ControlProfile> profiles, string name, string model = "")
    {
        var bundle = new SharedProfileBundle { Name = name };
        var slots = new Dictionary<string, string>();
        foreach (var group in profiles.GroupBy(p => p.GroupId))
        {
            var exported = new SharedProfileGroup { Name = state.Groups.FirstOrDefault(g => g.Id == group.Key)?.DisplayName ?? group.Key };
            foreach (var profile in group)
            {
                var scene = new SharedScene { Name = profile.Name, Applications = profile.Applications.ToList() };
                var values = new Dictionary<string, Dictionary<string, double>>();
                foreach (var entry in profile.Values)
                    if (ProfileGroups.TryMonitorKey(entry.Key, out var id, out var property) && ProfileExchange.IsShareable(property))
                    { if (!values.ContainsKey(id)) values[id] = new(); values[id][property] = entry.Value.Value; }
                foreach (var pair in values)
                {
                    var metadata = profile.Monitors.GetValueOrDefault(pair.Key) ?? state.KnownMonitors.GetValueOrDefault(pair.Key) ?? new MonitorDescriptor { ModelId = ModelIdentity.FromDevicePath(pair.Key) };
                    if (model.Length > 0 && metadata.ModelId != model) continue;
                    if (!slots.TryGetValue(pair.Key, out var slot)) slots[pair.Key] = slot = "display-" + (slots.Count + 1);
                    scene.Monitors.Add(new() { Slot = slot, ModelId = metadata.ModelId, ModelName = metadata.ModelName, DisplayName = metadata.DisplayName, Brand = metadata.Brand, Values = pair.Value, Brightness = profile.BrightnessMappings.GetValueOrDefault(pair.Key)?.Copy() });
                }
                if (scene.Monitors.Count > 0) exported.Profiles.Add(scene);
            }
            if (exported.Profiles.Count > 0) bundle.Groups.Add(exported);
        }
        ProfileExchange.ValidateBundle(bundle); return bundle;
    }
    public static List<ControlProfile> Import(SharedProfileBundle bundle, IReadOnlyDictionary<string, string> bindings, string groupId, IEnumerable<string> existingNames)
    {
        ProfileExchange.ValidateBundle(bundle);
        var result = new List<ControlProfile>(); var names = existingNames.ToList(); var unbound = new Dictionary<string, string>();
        foreach (var scene in bundle.Groups.SelectMany(g => g.Profiles))
        {
            var targets = scene.Monitors.Where(x => bindings.ContainsKey(x.Slot)).Select(x => bindings[x.Slot]).ToArray();
            if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length) throw new InvalidDataException("Two displays in one scene cannot map to the same device.");
            var profile = new ControlProfile { Name = ProfileGroups.UniqueName(scene.Name, names), GroupId = groupId, Applications = scene.Applications.ToList() }; names.Add(profile.Name);
            foreach (var monitor in scene.Monitors)
            {
                if (!bindings.TryGetValue(monitor.Slot, out var id))
                {
                    if (!unbound.TryGetValue(monitor.Slot, out id)) unbound[monitor.Slot] = id = "unbound:" + Guid.NewGuid().ToString("N");
                }
                profile.Monitors[id] = new() { ModelId = monitor.ModelId, ModelName = monitor.ModelName, DisplayName = monitor.DisplayName, Brand = monitor.Brand };
                foreach (var value in monitor.Values) profile.Values[ProfileGroups.MonitorKey(id, value.Key)] = new() { Value = value.Value };
                if (monitor.Brightness is not null) profile.BrightnessMappings[id] = monitor.Brightness.Copy();
            }
            result.Add(profile);
        }
        return result;
    }
}
