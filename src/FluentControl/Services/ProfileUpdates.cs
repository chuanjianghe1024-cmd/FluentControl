namespace FluentControl.Services;

public static class ProfileUpdates
{
    // Device discovery is a live view, never a deletion instruction. A partial
    // snapshot must preserve offline displays and temporarily unavailable controls.
    public static void Merge(ControlProfile profile, IReadOnlyDictionary<string, SavedValue> snapshot,
        IReadOnlyDictionary<string, MonitorDescriptor> monitors, IReadOnlyDictionary<string, BrightnessMapping> mappings)
    {
        var values = profile.Values.Where(x => ProfileGroups.TryMonitorKey(x.Key, out _, out _))
            .ToDictionary(x => x.Key, x => new SavedValue { Value = x.Value.Value, Muted = x.Value.Muted });
        foreach (var item in snapshot) values[item.Key] = new() { Value = item.Value.Value, Muted = item.Value.Muted };
        var metadata = profile.Monitors.ToDictionary(x => x.Key, x => x.Value.Copy());
        foreach (var item in monitors)
        {
            if (!metadata.TryGetValue(item.Key, out var existing)) { metadata[item.Key] = item.Value.Copy(); continue; }
            // Keep the user's profile-specific labels; fill only missing metadata.
            if (existing.ModelId.Length == 0) existing.ModelId = item.Value.ModelId;
            if (existing.ModelName.Length == 0) existing.ModelName = item.Value.ModelName;
            if (existing.DisplayName.Length == 0) existing.DisplayName = item.Value.DisplayName;
            if (existing.Brand.Length == 0) existing.Brand = item.Value.Brand;
        }
        var brightness = profile.BrightnessMappings.ToDictionary(x => x.Key, x => x.Value.Copy());
        foreach (var item in mappings) brightness[item.Key] = item.Value.Copy();
        profile.Values = values; profile.Monitors = metadata; profile.BrightnessMappings = brightness;
    }
}
