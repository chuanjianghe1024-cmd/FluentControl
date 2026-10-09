using FluentControl.Services;

internal static class MonitorDiscoveryTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Monitor discovery: " + message);
    }

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluentControl-monitor-cache-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { CacheChecks(directory); BatchChecks(); }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: monitor metadata cache, live values, forced/expired/corrupt cache recovery, bounded parallel reads and failure isolation.");
    }

    private static void CacheChecks(string directory)
    {
        var path = Path.Combine(directory, "capabilities.json");
        var now = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        var first = MonitorCapabilityCache.Identity("physical-A", "MODEL", "DISPLAY1");
        var second = MonitorCapabilityCache.Identity("physical-B", "MODEL", "DISPLAY2");
        var moved = MonitorCapabilityCache.Identity("physical-A", "MODEL", "DISPLAY3");
        Check(first != second && first != moved, "same-model screens and different connections need separate cache entries");
        var loads = 0;
        MonitorCapabilityData Read() { loads++; return new("(vcp(10 12 60(0F 11)))", 4); }
        var cache = new MonitorCapabilityCache(path, clock: () => now);
        var data = cache.GetOrRead(first, Read, false, out var hit);
        Check(!hit && loads == 1, "cold scan reads metadata from hardware");
        cache.Save();
        cache = new MonitorCapabilityCache(path, clock: () => now);
        data = cache.GetOrRead(first, Read, false, out hit);
        Check(hit && loads == 1 && data.ColorTemperatureFlags == 4, "metadata is reusable after an application restart");

        var value = 20u; var writes = 0; var reads = 0;
        List<MonitorFeature> Discover() => VcpDiscovery.Discover(first, VcpCapabilities.Parse(data.VcpText), code =>
        { reads++; return code == 0x10 ? new VcpReply(value, 100) : null; }, (_, _) => writes++);
        Check(Discover().Single(f => f.Definition.Key == "brightness").Channel?.Value == 20, "cold scan reads live brightness");
        var firstReadCount = reads; reads = 0; value = 73;
        data = cache.GetOrRead(first, Read, false, out hit);
        Check(hit && Discover().Single(f => f.Definition.Key == "brightness").Channel?.Value == 73,
            "metadata cache must not restore stale brightness after OSD or another application changes it");
        Check(reads == firstReadCount && reads > 0 && writes == 0, "cache hits retain live reads and never write hardware");

        cache.GetOrRead(second, Read, false, out hit);
        Check(!hit && loads == 2, "another same-model screen cannot reuse the first screen's metadata");
        cache.GetOrRead(moved, Read, false, out hit);
        Check(!hit && loads == 3, "changing a connection invalidates metadata");
        var changed = cache.GetOrRead(first, () => new("(vcp(10 12 60(0F)))", 0), true, out hit);
        Check(!hit && VcpCapabilities.Parse(changed.VcpText).Features[0x60].SequenceEqual(new byte[] { 15 }),
            "forced scan replaces old input-source options");
        cache.GetOrRead(first, () => new("broken response", null), true, out _);
        cache.Save();
        cache = new MonitorCapabilityCache(path, clock: () => now);
        cache.GetOrRead(first, Read, false, out hit);
        Check(!hit && loads == 4, "failed forced scan removes stale writable metadata instead of keeping it");
        now += MonitorCapabilityCache.Lifetime;
        cache.GetOrRead(first, Read, false, out hit);
        Check(!hit && loads == 5, "cache has a bounded lifetime");
        cache.Save();
        now -= TimeSpan.FromHours(1);
        cache.GetOrRead(first, Read, false, out hit);
        Check(!hit && loads == 6, "future cache timestamps are not trusted after clock rollback");

        File.WriteAllText(path, "{invalid");
        cache = new MonitorCapabilityCache(path, clock: () => now);
        cache.GetOrRead(first, Read, false, out hit);
        Check(!hit && loads == 7, "corrupted cache falls back to hardware");
        File.WriteAllText(path, "{\"Version\":1,\"Entries\":{\"" + first + "\":null}}");
        cache = new MonitorCapabilityCache(path, clock: () => now);
        cache.GetOrRead(first, Read, false, out hit);
        Check(!hit && loads == 8, "null cache records are ignored");

        // A directory at the intended file path simulates an unwritable cache
        // destination on Windows and Linux without changing user permissions.
        cache = new MonitorCapabilityCache(directory, clock: () => now);
        cache.GetOrRead(first, Read, false, out hit);
        cache.Save();
        Check(!hit && loads == 9, "cache persistence failure cannot prevent a hardware scan");
    }

    private static void BatchChecks()
    {
        var sync = new object(); var active = 0; var maximum = 0;
        var perDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var finished = new List<string>(); var errors = new List<string>();
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        var devices = new[] { (Id: "A", Index: 1), (Id: "a", Index: 2), (Id: "B", Index: 1), (Id: "B", Index: 2), (Id: "broken", Index: 1), (Id: "D", Index: 1) };
        MonitorReadBatch.Run(devices, d => d.Id, d =>
        {
            lock (sync)
            {
                active++; maximum = Math.Max(maximum, active);
                perDevice.TryGetValue(d.Id, out var existing);
                Check(existing == 0, "requests for one display must never overlap");
                perDevice[d.Id] = 1;
            }
            try
            {
                if (d.Id == "A") { firstEntered.Set(); Check(secondEntered.Wait(TimeSpan.FromSeconds(10)), "a second display should progress while the first is reading"); }
                if (d.Id == "B" && d.Index == 1) { secondEntered.Set(); Check(firstEntered.Wait(TimeSpan.FromSeconds(10)), "first display must start without waiting for the second to finish"); }
                if (d.Id == "broken") throw new IOException("disconnected");
                lock (sync) finished.Add(d.Id + d.Index);
            }
            finally { lock (sync) { active--; perDevice[d.Id] = 0; } }
        }, (d, error) => { lock (sync) errors.Add(d.Id + ":" + error.Message); });
        Check(maximum == 2 && active == 0, "at most two displays run concurrently and all workers are joined before returning");
        Check(finished.Count == 5 && errors.SequenceEqual(new[] { "broken:disconnected" }), "one display failure must not abort other displays");
    }
}
