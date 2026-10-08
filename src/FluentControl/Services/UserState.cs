using System.Text.Json;
namespace FluentControl.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "auto";
    public bool CloseToTray { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool DesktopPanelEnabled { get; set; }
    public bool GroupDesktopMonitors { get; set; } = true;
    public int DesktopMaxRows { get; set; } = 6;
    public bool DesktopLightText { get; set; } = true;
    public int? DesktopX { get; set; }
    public int? DesktopY { get; set; }
    public List<string> DesktopRows { get; set; } = new() { "monitor/all/brightness", "monitor/all/contrast" };
    public bool CrosshairEnabled { get; set; }
    public string CrosshairMonitorId { get; set; } = "";
}
public sealed class SavedValue
{
    public double Value { get; set; }
    public bool? Muted { get; set; }
}
public sealed class ControlProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Dictionary<string, SavedValue> Values { get; set; } = new();
}
public sealed class UserState
{
    public AppSettings Settings { get; set; } = new();
    public List<ControlProfile> Profiles { get; set; } = new();
    public string? SelectedProfileId { get; set; }
}
public sealed class UserStateStore
{
    private readonly string path;
    public UserState State { get; }
    public UserStateStore(string path)
    {
        this.path = path;
        State = File.Exists(path) ? JsonSerializer.Deserialize<UserState>(File.ReadAllText(path)) ?? new() : new();
        State.Settings ??= new(); State.Profiles ??= new();
        State.Settings.DesktopRows ??= new();
        State.Settings.DesktopMaxRows = Math.Clamp(State.Settings.DesktopMaxRows, 1, 16);
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
    public static int NextIndex(IReadOnlyList<ControlProfile> profiles, string? currentId, int delta)
    {
        if (profiles.Count == 0) return -1;
        var index = -1;
        for (var i = 0; i < profiles.Count; i++) if (profiles[i].Id == currentId) index = i;
        if (index < 0) return delta < 0 ? profiles.Count - 1 : 0;
        return (index + delta % profiles.Count + profiles.Count) % profiles.Count;
    }
}
public sealed class PanelRow
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required IReadOnlyList<ControlChannel> Targets { get; init; }
}
