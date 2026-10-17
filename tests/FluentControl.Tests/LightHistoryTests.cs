using FluentControl.Services;
internal static class LightHistoryTests
{
    internal static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "fc-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LightHistory(path, capacity: 4096, segmentSize: 1024);
            var now = DateTimeOffset.UtcNow;
            LightSample Sample(int n) => new() { SourceId = "probe", Timestamp = now.AddSeconds(n), Lux = n, Valid = true };
            await store.AppendAsync("a", Sample(0)); await store.AppendAsync("a", Sample(0));
            if ((await store.ReadPageAsync("a", 0)).Count != 1) throw new Exception("Duplicate samples logged.");
            await store.AppendAsync("a", null); await store.AppendAsync("a", null);
            if ((await store.ReadPageAsync("a", 0)).Count != 2) throw new Exception("Repeated gap events logged.");
            for (int n = 1; n < 80; n++) await store.AppendAsync("a", Sample(n));
            if (new DirectoryInfo(path).GetFiles().Sum(x => x.Length) > 4096) throw new Exception("History exceeds capacity.");
            var reopened = new LightHistory(path, 4096, 1024);
            var rows = await reopened.ReadPageAsync("a", 0);
            if (rows.Count == 0 || rows[0].Lux != 79 || rows.Any(x => x.Lux == 0)) throw new Exception("History retention or restart failed.");
            if ((await reopened.ReadPageAsync("b", 0)).Count != 0) throw new Exception("History mixed subscriptions.");
            var file = Directory.GetFiles(path).Order().Last();
            await File.AppendAllTextAsync(file, "{truncated");
            await reopened.AppendAsync("a", Sample(80));
            if ((await reopened.ReadPageAsync("a", 0))[0].Lux != 80) throw new Exception("History did not recover from partial record.");
            Console.WriteLine("PASS: bounded history, deduplication, gaps, retention, restart and damaged tail.");
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
