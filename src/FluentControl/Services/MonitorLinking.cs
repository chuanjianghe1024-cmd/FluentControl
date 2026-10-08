namespace FluentControl.Services;

public static class MonitorLinking
{
    // Overall controls follow capability, not model identity. Keep a stable row
    // selection even when a monitor is disconnected or its model is unknown.
    public static List<string> NormalizeLinkedDesktopRows(IEnumerable<string> rows) => rows.Select(key =>
        key.StartsWith("monitor/model/", StringComparison.Ordinal) || key.StartsWith("monitor/device/", StringComparison.Ordinal)
            ? "monitor/all/" + key.Split('/').Last() : key).Distinct(StringComparer.Ordinal).ToList();
    public static ControlOption[]? Options(IEnumerable<ControlChannel> channels)
    {
        var choices = channels.Where(c => c.Options is not null).SelectMany(c => c.Options!).DistinctBy(x => x.Value).ToArray();
        return choices.Length == 0 ? null : choices;
    }
}
