namespace FluentControl.Services;

public static class MonitorLinking
{
    public static bool IsDesktopControl(ControlChannel channel) =>
        !channel.IsAction && !channel.RequiresConfirmation && !channel.CompatibilityOnly;

    public static IEnumerable<IGrouping<string, ControlChannel>> DesktopGroups(IEnumerable<ControlChannel> channels) =>
        channels.Where(IsDesktopControl).GroupBy(c => c.PropertyKey);

    public static ControlOption[]? DesktopOptions(IReadOnlyList<ControlChannel> targets, Func<ControlChannel, string> name) =>
        Options(targets)?.Select(option =>
        {
            var supported = targets.Where(c => c.Options?.Any(o => o.Value == option.Value) == true).ToArray();
            return supported.Length == targets.Count ? option : new ControlOption(option.Value,
                option.Label + " · " + Strings.F("仅 {0}", "Only {0}", string.Join(", ", supported.Select(name))));
        }).ToArray();

    // Overall controls follow capability, not model identity. Keep a stable row
    // selection even when a monitor is disconnected or its model is unknown.
    public static List<string> NormalizeLinkedDesktopRows(IEnumerable<string> rows) => rows.Select(key =>
        key.StartsWith("monitor/", StringComparison.Ordinal)
            ? "monitor/all/" + key.Split('/').Last() : key).Distinct(StringComparer.Ordinal).ToList();

    public static void ChangeDesktopMode(AppSettings settings, bool linked, IEnumerable<string> individualKeys)
    {
        if (settings.GroupDesktopMonitors == linked) return;
        var monitors = settings.DesktopRows.Where(k => k.StartsWith("monitor/", StringComparison.Ordinal)).ToList();
        if (settings.GroupDesktopMonitors) settings.DesktopLinkedRows = NormalizeLinkedDesktopRows(monitors);
        else settings.DesktopIndividualRows = monitors;
        var features = monitors.Select(k => k.Split('/').Last()).ToHashSet(StringComparer.Ordinal);
        var selected = linked
            ? settings.DesktopLinkedRows ?? NormalizeLinkedDesktopRows(monitors)
            : settings.DesktopIndividualRows ?? individualKeys.Where(k => features.Contains(k.Split('/').Last())).ToList();
        settings.DesktopRows = selected.Concat(settings.DesktopRows.Where(k => !k.StartsWith("monitor/", StringComparison.Ordinal))).Distinct(StringComparer.Ordinal).ToList();
        settings.GroupDesktopMonitors = linked;
    }
    public static ControlOption[]? Options(IEnumerable<ControlChannel> channels)
    {
        var choices = channels.Where(c => c.Options is not null).SelectMany(c => c.Options!).DistinctBy(x => x.Value).ToArray();
        return choices.Length == 0 ? null : choices;
    }
}
