using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluentControl.Services;

// Only stable capability metadata is cached. VCP values and monitor handles
// must always come from the current connection / hardware scan.
public sealed record MonitorCapabilityData(string VcpText, uint? ColorTemperatureFlags);

public sealed class MonitorCapabilityCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private const int MaximumEntries = 32;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private readonly string path;
    private readonly Action<string>? diagnostic;
    private readonly Func<DateTimeOffset> clock;
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private bool dirty;

    public sealed record Entry(DateTimeOffset ReadAt, MonitorCapabilityData Data);
    public sealed record Document(int Version, Dictionary<string, Entry> Entries);

    public MonitorCapabilityCache(string path, Action<string>? diagnostic = null, Func<DateTimeOffset>? clock = null)
    {
        this.path = path; this.diagnostic = diagnostic; this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumFileBytes) return;
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1, Entries: not null }) return;
            foreach (var entry in document.Entries.OrderByDescending(x => x.Value?.ReadAt).Take(MaximumEntries))
                if (entry.Key.Length == 64 && entry.Key.All(Uri.IsHexDigit) && Valid(entry.Value)) entries[entry.Key] = entry.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { diagnostic?.Invoke("Monitor capability cache ignored: " + ex.GetType().Name); }
    }

    public static string Identity(string deviceId, string model, string connection) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { deviceId, model, connection }))));

    private bool Valid(Entry? entry)
    {
        if (entry?.Data?.VcpText is not { Length: > 0 and <= 65536 } text) return false;
        var age = clock() - entry.ReadAt;
        return age >= TimeSpan.Zero && age < Lifetime &&
            entry.Data.ColorTemperatureFlags is null or <= 255 && VcpCapabilities.Parse(text).HasVcpSection;
    }

    public MonitorCapabilityData GetOrRead(string key, Func<MonitorCapabilityData> read, bool force, out bool cacheHit)
    {
        lock (sync)
        {
            if (!force && entries.TryGetValue(key, out var entry) && Valid(entry))
            { cacheHit = true; return entry.Data; }
            // A forced or expired scan may not fall back to stale capabilities.
            if (entries.Remove(key)) dirty = true;
        }
        cacheHit = false;
        // Never hold the shared cache lock during a slow DDC/CI request.
        var result = read();
        var fresh = new Entry(clock(), result);
        if (Valid(fresh))
        {
            lock (sync)
            {
                entries[key] = fresh; dirty = true;
                while (entries.Count > MaximumEntries) entries.Remove(entries.MinBy(x => x.Value.ReadAt).Key);
            }
        }
        return result;
    }

    public void Save()
    {
        string? temporary = null;
        try
        {
            lock (sync)
            {
                if (!dirty) return;
                var content = JsonSerializer.Serialize(new Document(1, entries));
                if (Encoding.UTF8.GetByteCount(content) > MaximumFileBytes) return;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, content);
                File.Move(temporary, path, true);
                dirty = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { diagnostic?.Invoke("Monitor capability cache not saved: " + ex.GetType().Name); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
