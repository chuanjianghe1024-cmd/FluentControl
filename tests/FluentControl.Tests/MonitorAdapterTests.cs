using FluentControl.Services;
using System.Text.Json;

internal static class MonitorAdapterTests
{
    internal static MonitorAdapter Fixture() => new()
    {
        Id = "test-monitor", Revision = 1, ModelId = "TST1234", ModelName = "Synthetic test display", FirmwareVersions = new() { "1.2" },
        Evidence = "Synthetic unit-test fixture only; not a hardware adaptation.",
        Controls = new() { new() { Key = "color-preset", Options = new() { new() { Value = 5, Label = "Verified warm" }, new() { Value = 8, Label = "Verified cool" } } } },
        NativeMenu = new() { new() { Action = "open", Code = 3, Value = 1 }, new() { Action = "close", Code = 3, Value = 4 } }
    };
    private static void Check(bool value, string message) { if (!value) throw new Exception("Monitor adapters: " + message); }
    internal static void Run()
    {
        var adapter = Fixture(); adapter.Validate();
        Check(adapter.Matches("TST1234", "1.2") && !adapter.Matches("TST1234", "1.3") && !adapter.Matches("TST1234", "") && !adapter.Matches("TST1235", "1.2"), "model and firmware matching is exact");
        var json = JsonSerializer.Serialize(adapter, MonitorAdapterStore.JsonOptions);
        var parsed = MonitorAdapterStore.Parse(json); Check(parsed.NativeMenu.Count == 2, "multiple menu actions sharing one VCP survive");
        foreach (var bad in new[] { json.Replace("\"version\": 1", "\"version\": 2"), json.Replace("\"color-preset\"", "\"power\""), json.Replace("\"code\": 3", "\"code\": 4"), json.Replace("\"1.2\"", "\"*\""), json.Replace("\"format\":", "\"script\": \"run\", \"format\":") })
        {
            var rejected = false; try { MonitorAdapterStore.Parse(bad); } catch { rejected = true; }
            Check(rejected, "unknown schemas, unsafe targets, wildcards and executable fields rejected");
        }
        var sent = new List<(byte, uint)>();
        uint current = 5;
        var features = VcpDiscovery.Discover("TEST", VcpCapabilities.Parse("(vcp(14))"),
            code => code == 0x14 ? new(current, 0) : null, (code, value) => { sent.Add((code, value)); current = value; }, adapter);
        Check(sent.Count == 0, "loading an adapter never writes hardware");
        var channel = features.Single(f => f.Definition.Code == 0x14).Channel!;
        Check(channel.Options!.Single(o => o.Value == 8).Label == "Verified cool", "verified options work despite missing capability option list");
        Check(ControlOperations.Apply(new[] { channel }, 8).Count == 0 && current == 8 && sent.Single() == ((byte)0x14, 8u), "adapted values use normal readback and write safeguards");
        Check(ControlOperations.Apply(new[] { channel }, 11).Count == 1 && sent.Count == 1, "adapter cannot authorize undeclared option values");
        var directory = Path.Combine(Path.GetTempPath(), "fc-adapter-tests-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "adapters.json");
        try
        {
            var store = new MonitorAdapterStore(path); store.Install(adapter, "TST1234", "1.2");
            Check(new MonitorAdapterStore(path).Find("TST1234", "1.2")?.Id == adapter.Id, "installed packages work offline");
            var before = File.ReadAllText(path); var rejected = false;
            try { store.Install(adapter, "TST1234", "1.3"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(path) == before, "mismatch leaves local installation intact");
            store.Remove(adapter.Id); Check(new MonitorAdapterStore(path).Find("TST1234", "1.2") is null, "removal restores generic discovery");
            File.WriteAllText(path, "broken"); var broken = new MonitorAdapterStore(path);
            Check(broken.LoadError is not null && !broken.HasModel("TST1234"), "corrupt adapter store falls back to generic controls");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: adapter model/firmware isolation, strict data schema, option guards, offline installation, removal and corrupt-store fallback.");
    }
}
