using System.Text.Json;
namespace FluentControl.Services;

public sealed class MonitorPreference
{
    public string Label { get; set; } = "";
    public string Alias { get; set; } = "";
    public BrightnessMapping Brightness { get; set; } = new();
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Label : Alias;
}

public sealed class MonitorPreferences
{
    private readonly string path;
    private readonly Dictionary<string, MonitorPreference> entries;
    public MonitorPreferences(string path)
    {
        this.path = path;
        if (File.Exists(path))
            entries = JsonSerializer.Deserialize<Dictionary<string, MonitorPreference>>(File.ReadAllText(path)) ?? new();
        else entries = new();
    }

    public MonitorPreference GetOrAdd(string id)
    {
        if (entries.TryGetValue(id, out var existing)) return existing;
        var number = 1;
        while (entries.Values.Any(x => x.Label == "M" + number)) number++;
        var entry = new MonitorPreference { Label = "M" + number };
        entries.Add(id, entry);
        try { Save(); }
        catch { entries.Remove(id); throw; }
        return entry;
    }

    public void Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 40) throw new ArgumentException("名称需要 1–40 个字符。");
        var entry = GetOrAdd(id);
        var previous = entry.Alias;
        entry.Alias = name == entry.Label ? "" : name;
        try { Save(); }
        catch { entry.Alias = previous; throw; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
