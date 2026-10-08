namespace FluentControl.Services;

public sealed class MonitorDescriptor
{
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Brand { get; set; } = "";
    public MonitorDescriptor Copy() => new() { ModelId = ModelId, ModelName = ModelName, DisplayName = DisplayName, Brand = Brand };
}
public sealed class ProfileGroup
{
    public const string LocalId = "local";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string DisplayName => Id == LocalId ? Strings.T("本地 / 默认分组", "Local / Default group") : Name;
}
public static class ProfileGroups
{
    public static void Normalize(UserState state)
    {
        state.Groups ??= new(); state.KnownMonitors ??= new();
        if (!state.Groups.Any(g => g.Id == ProfileGroup.LocalId)) state.Groups.Insert(0, new() { Id = ProfileGroup.LocalId });
        foreach (var profile in state.Profiles)
        {
            if (!state.Groups.Any(g => g.Id == profile.GroupId)) profile.GroupId = ProfileGroup.LocalId;
            profile.Monitors ??= new(); profile.Values ??= new(); profile.BrightnessMappings ??= new(); profile.Applications ??= new();
        }
        if (!state.Groups.Any(g => g.Id == state.SelectedGroupId)) state.SelectedGroupId = ProfileGroup.LocalId;
    }
    public static List<ControlProfile> Current(UserState state) => state.Profiles.Where(x => x.GroupId == state.SelectedGroupId).ToList();
    public static string UniqueName(string name, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(name)) return name;
        for (var suffix = 2; ; suffix++) { var ending = " (" + suffix + ")"; var candidate = name[..Math.Min(name.Length, 80 - ending.Length)] + ending; if (!names.Contains(candidate)) return candidate; }
    }
    public static string MonitorKey(string id, string property) => $"monitor/{Uri.EscapeDataString(id)}/{property}";
    public static bool Matches(ControlProfile profile, string application = "", string model = "", string brand = "") =>
        (string.IsNullOrWhiteSpace(application) || profile.Applications.Any(x => x.Contains(application.Trim(), StringComparison.OrdinalIgnoreCase))) &&
        ((string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(brand)) || profile.Monitors.Values.Any(x =>
            (string.IsNullOrWhiteSpace(model) || (x.ModelId + " " + x.ModelName).Contains(model.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(brand) || x.Brand.Contains(brand.Trim(), StringComparison.OrdinalIgnoreCase))));
    public static bool TryMonitorKey(string key, out string id, out string property)
    {
        id = property = ""; var parts = key.Split('/');
        if (parts.Length != 3 || parts[0] != "monitor") return false;
        id = Uri.UnescapeDataString(parts[1]); property = parts[2]; return true;
    }
}
