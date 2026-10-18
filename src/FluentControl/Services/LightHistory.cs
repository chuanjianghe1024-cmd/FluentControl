using System.Text;
using System.Text.Json;

namespace FluentControl.Services;

public sealed record LightHistoryEntry(string SubscriptionId, string SourceId, DateTimeOffset Timestamp,
    DateTimeOffset ReceivedAt, double? Lux, bool Valid, bool Simulated);

// Bounded, local-only segmented log. No URLs, credentials or screen settings are stored.
public sealed class LightHistory
{
    public const long DefaultCapacity = 64L * 1024 * 1024;
    private readonly string directory;
    private readonly long capacity, segmentSize;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (string Identity, DateTimeOffset Timestamp, bool Valid)> last = new();
    private string? active;
    public LightHistory(string directory, long capacity = DefaultCapacity, long segmentSize = 1024 * 1024)
    {
        if (capacity < 1024 || segmentSize < 512 || segmentSize > capacity) throw new ArgumentOutOfRangeException();
        this.directory = directory; this.capacity = capacity; this.segmentSize = segmentSize;
    }
    private FileInfo[] Files() => Directory.Exists(directory)
        ? new DirectoryInfo(directory).GetFiles("segment-*.jsonl").OrderBy(x => x.Name, StringComparer.Ordinal).ToArray()
        : Array.Empty<FileInfo>();
    public async Task AppendAsync(string subscriptionId, LightSample? sample)
    {
        await gate.WaitAsync();
        try
        {
            bool valid = sample is { Valid: true } && sample.Lux is double lux && double.IsFinite(lux) && lux is >= 0 and <= 200000;
            var identity = sample?.SourceId ?? "";
            var timestamp = sample?.Timestamp ?? DateTimeOffset.UtcNow;
            if (last.TryGetValue(subscriptionId, out var prior) && prior.Valid == valid &&
                (!valid || prior.Identity == identity && prior.Timestamp == timestamp)) return;
            Directory.CreateDirectory(directory);
            var record = new LightHistoryEntry(subscriptionId, identity, timestamp, DateTimeOffset.UtcNow,
                valid ? sample!.Lux : null, valid, sample?.Simulated ?? false);
            var line = JsonSerializer.Serialize(record) + "\n";
            var bytes = Encoding.UTF8.GetByteCount(line) + 1; // reserve crash-recovery separator
            if (bytes > segmentSize) throw new InvalidDataException("History entry too large.");
            var files = Files();
            active ??= files.LastOrDefault()?.FullName;
            if (active is null || !File.Exists(active) || new FileInfo(active).Length + bytes > segmentSize)
            {
                long sequence = 0;
                foreach (var file in files)
                    if (long.TryParse(Path.GetFileNameWithoutExtension(file.Name).AsSpan(8), out var n)) sequence = Math.Max(sequence, n);
                active = Path.Combine(directory, $"segment-{sequence + 1:D16}.jsonl");
            }
            // Prune before append; even at capacity, no temporary duplicate of the full log is needed.
            long total = files.Sum(x => x.Length);
            foreach (var file in files)
            {
                if (total + bytes <= capacity) break;
                if (file.FullName == active) continue;
                File.Delete(file.FullName); total -= file.Length;
            }
            // A crash may leave an unterminated last record. Separate it from the next record;
            // readers skip that damaged line instead of losing the next valid sample as well.
            if (File.Exists(active) && new FileInfo(active).Length > 0)
            {
                using var tail = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                tail.Seek(-1, SeekOrigin.End);
                if (tail.ReadByte() != '\n') line = "\n" + line;
            }
            await File.AppendAllTextAsync(active, line, new UTF8Encoding(false));
            if (last.Count > 128) last.Clear();
            last[subscriptionId] = (identity, timestamp, valid);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<LightHistoryEntry>> ReadPageAsync(string subscriptionId, int page, int pageSize = 100)
    {
        if (page < 0 || pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException();
        await gate.WaitAsync();
        try
        {
            return await Task.Run<IReadOnlyList<LightHistoryEntry>>(() =>
            {
                int skip = checked(page * pageSize);
                var results = new List<LightHistoryEntry>();
                foreach (var file in Files().Reverse())
                {
                    // Segments are bounded; reading a page never loads the whole retained log.
                    foreach (var line in File.ReadLines(file.FullName).Reverse())
                    {
                        LightHistoryEntry? entry;
                        try { entry = JsonSerializer.Deserialize<LightHistoryEntry>(line); }
                        catch (JsonException) { continue; }
                        if (entry is null || entry.SubscriptionId != subscriptionId) continue;
                        if (skip > 0) { skip--; continue; }
                        results.Add(entry);
                        if (results.Count == pageSize) return results;
                    }
                }
                return results;
            });
        }
        finally { gate.Release(); }
    }
}
