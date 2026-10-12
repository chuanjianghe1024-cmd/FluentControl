using System.Diagnostics;
using System.Text.Json;
using FluentControl.Services;

internal static class DdcReliabilityTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    // A real child process with a fake backend; never calls monitor APIs.
    public static void Fixture(string[] args)
    {
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            var request = JsonSerializer.Deserialize<DdcRequest>(line)!;
            if (args[1] == "hang" && request.Operation == "write")
            {
                File.AppendAllText(args[2], "write\n");
                Thread.Sleep(Timeout.Infinite);
            }
            if (args[1] == "crash") Environment.Exit(7);
            Console.WriteLine(JsonSerializer.Serialize(new DdcResponse(Current: request.Value, Maximum: 100)));
        }
    }
    private static ProcessStartInfo Start(string mode, string path)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(DdcReliabilityTests).Assembly.Location);
        info.ArgumentList.Add("--ddc-fixture"); info.ArgumentList.Add(mode); info.ArgumentList.Add(path);
        return info;
    }
    public static void Run()
    {
        var attempts = 0;
        var result = DdcReadPolicy.Read(() => ++attempts < 3 ? new(unchecked((int)0xC026258B)) : new(Current: 73), 0x10, _ => { });
        Check(attempts == 3 && result.Current == 73 && result.Error == 0, "Transient reads recover using a real reply.");
        foreach (var error in new uint[] { 0xC0262584, 0xC026258C, 0xC026258D })
        {
            attempts = 0;
            DdcReadPolicy.Read(() => { attempts++; return new(unchecked((int)error)); }, 0x10, _ => { });
            Check(attempts == 1, "Unsupported/disconnected/invalid-handle errors must not retry.");
        }
        foreach (var code in new byte[] { 0x02, 0x03, 0x52 })
        {
            attempts = 0;
            DdcReadPolicy.Read(() => { attempts++; return new(unchecked((int)0xC026258B)); }, code, _ => { });
            Check(attempts == 1, "Event reads must never be retried.");
        }
        attempts = 0;
        result = DdcReadPolicy.Read(() => { attempts++; return new(unchecked((int)0xC0262582)); }, 0x10, _ => { });
        Check(attempts == 3 && result.Error != 0, "Read retries are bounded and do not create a cached success.");

        var root = Path.Combine(Path.GetTempPath(), "fc-ddc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var recovery = new DdcProbeRecovery(root); var key = new string('A', 64);
            recovery.Begin(key);
            Check(new DdcProbeRecovery(root).ShouldSkip(key, false) && !recovery.ShouldSkip(key, true), "Interrupted capability query survives restart; explicit retry bypasses quarantine.");
            recovery.Complete(key); Check(!recovery.ShouldSkip(key, false), "A completed capability query clears only its marker.");
            using var healthy = new DdcProcessClient(Start("healthy", root), TimeSpan.FromSeconds(10));
            Check(healthy.Request(new("read", Value: 42)).Current == 42, "The worker protocol carries raw values.");
            var log = Path.Combine(root, "writes.txt");
            using (var hung = new DdcProcessClient(Start("hang", log), TimeSpan.FromSeconds(2)))
            {
                var watch = Stopwatch.StartNew();
                try { hung.Request(new("write", Code: 0xFD, Value: 1)); throw new Exception("Expected timeout."); } catch (IOException) { }
                Check(watch.Elapsed < TimeSpan.FromSeconds(6) && hung.IsStopped, "Blocked native host is bounded and invalidated.");
                try { hung.Request(new("write", Code: 0xFD, Value: 1)); throw new Exception("Expected stopped host."); } catch (IOException) { }
                Check(File.ReadAllLines(log).Length == 1, "Ambiguous writes are never replayed.");
            }
            Check(healthy.Request(new("read", Value: 55)).Current == 55, "A blocked display does not invalidate another connection.");
            using var crashed = new DdcProcessClient(Start("crash", root), TimeSpan.FromSeconds(5));
            try { crashed.Request(new("read")); throw new Exception("Expected EOF."); } catch (IOException) { }
            using var fresh = new DdcProcessClient(Start("healthy", root), TimeSpan.FromSeconds(10));
            Check(fresh.Request(new("read", Value: 61)).Current == 61, "A new discovery can create a fresh host after a crash.");
        }
        finally { Directory.Delete(root, true); }
        var readCodes = new List<byte>();
        var basic = VcpDiscovery.Discover("test", VcpCapabilities.Parse(""), code => { readCodes.Add(code); return new(40, 100); }, (_, _) => throw new Exception("Discovery wrote hardware."),
            definitions: VcpCatalog.All.Where(d => d.Code is 0x10 or 0x12));
        Check(readCodes.SequenceEqual(new byte[] { 0x10, 0x12 }), "The first phase queries only brightness and contrast.");
        var device = new MonitorDevice { Id = "test", Model = "test", Connection = "test", CapabilitiesPending = true };
        device.Features.AddRange(basic); device.Channels.AddRange(basic.Select(f => f.Channel!));
        var channel = device.Channels[0];
        MonitorService.MergeEnrichment(device, new MonitorDevice { Id = "test", Model = "test", Connection = "test", DiscoveryWarning = "test failure" });
        Check(!device.CapabilitiesPending && ReferenceEquals(device.Channels[0], channel) && channel.Value == 40, "Enrichment failure retains live basic controls and their connection objects.");
        Console.WriteLine("PASS: staged discovery, bounded IPC, per-connection failure isolation, no write replay and capability quarantine.");
    }
}
