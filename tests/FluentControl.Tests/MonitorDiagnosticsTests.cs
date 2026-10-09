using FluentControl.Services;
using System.Text.Json;

internal static class MonitorDiagnosticsTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Monitor diagnostics: " + message); }

    internal static void Run()
    {
        var device = new MonitorDevice { Id = "PRIVATE_DEVICE_INSTANCE", Model = "Test monitor", ModelId = "TST1234", Connection = "PRIVATE_LOCAL_PATH" };
        var caps = new DiagnosticCapabilities("(model(Test) serial(PRIVATE_SERIAL) vcp(04 10 14(05 08) CA E1) mccs_ver(2.2))", "hardware");
        var calls = new List<byte>();
        uint preset = 0x0205;
        bool fail = false;
        DiagnosticReply Read(byte code)
        {
            calls.Add(code);
            if (code == 0x14) return fail ? new(0, 0, 0, 31) : new(0, preset, 0);
            if (code == 0x10) return new(0, 70, 200);
            if (code == 0xC9) return new(0, 0x0102, 0);
            return new(0, 0, 0, 50);
        }
        var baseline = MonitorDiagnostics.Capture(device, caps, Read);
        Check(!calls.Contains(0x04) && !calls.Contains(0xE1), "reset and private features remain unprobed");
        Check(calls.Distinct().Count() == calls.Count, "each readable control is queried only once per sample");
        Check(baseline.Readings.Single(r => r.Code == 0xE1).Status == "not-probed", "advertised private features retained as placeholders");
        Check(baseline.Readings.Single(r => r.Code == 0x10).Current == 70 && baseline.Readings.Single(r => r.Code == 0x10).Maximum == 200,
            "native values are not normalized into UI percentages");
        Check(baseline.Readings.Single(r => r.Code == 0x14).Current == 0x0205 && baseline.Firmware == "1.2" && baseline.MccsVersion == "2.2", "raw high bytes and versions survive");
        preset = 0x0208;
        var sample = MonitorDiagnostics.Capture(device, caps, Read);
        var diff = MonitorDiagnostics.Compare(baseline, sample);
        Check(diff.Count == 1 && diff[0].Code == 0x14 && diff[0].Kind == "value", "color preset change compares raw values");
        Check(MonitorDiagnostics.Compare(sample, MonitorDiagnostics.Capture(device, caps, Read)).Count == 0, "stable readings have no invented changes");
        fail = true;
        var failed = MonitorDiagnostics.Capture(device, caps, Read);
        Check(failed.Readings.Single(r => r.Code == 0x14).Current is null && MonitorDiagnostics.Compare(baseline, failed).Single().Kind == "read-status",
            "failed read is an error, never a numeric zero or a preset change");
        var other = new MonitorDevice { Id = "SAME_MODEL_OTHER_SCREEN", Model = device.Model, ModelId = device.ModelId, Connection = "test" };
        var rejected = false;
        try { MonitorDiagnostics.Compare(baseline, MonitorDiagnostics.Capture(other, caps, Read)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "same-model screens cannot be mixed in one experiment");
        MonitorDiagnosticReport Report(bool raw) => new()
        {
            Baseline = baseline, IncludesRawCapabilities = raw, ConnectionNotes = "DP direct", EnvironmentNotes = "SDR",
            Observations = new() { new() { Label = "色温 9300 K", Snapshot = sample } }
        };
        var json = MonitorDiagnostics.Serialize(Report(false));
        Check(!json.Contains("PRIVATE_") && !json.Contains("rawCapabilities") && json.Contains("色温 9300 K"), "default export excludes raw capabilities and local identity, keeps readable labels");
        using var parsed = JsonDocument.Parse(json);
        Check(parsed.RootElement.GetProperty("observations")[0].GetProperty("differences")[0].GetProperty("kind").GetString() == "value", "export includes recomputed differences");
        var rawJson = MonitorDiagnostics.Serialize(Report(true));
        Check(rawJson.Contains("PRIVATE_SERIAL") && !rawJson.Contains("PRIVATE_DEVICE_INSTANCE") && !rawJson.Contains("PRIVATE_LOCAL_PATH"), "raw opt-in is scoped to capability text");
        var fallback = MonitorDiagnostics.Capture(device, new("(vcp(broken", "unavailable", 13), Read);
        Check(!fallback.CapabilitiesParsed && fallback.Readings.Any(r => r.Code == 0x10 && r.Status == "ok"), "malformed metadata still permits known read-only diagnostics");
        using var cancellation = new CancellationTokenSource(); calls.Clear();
        rejected = false;
        try
        {
            MonitorDiagnostics.Capture(device, caps, code => { calls.Add(code); cancellation.Cancel(); return new(0, 1, 100); }, cancellation.Token);
        }
        catch (OperationCanceledException) { rejected = true; }
        Check(rejected && calls.Count == 1, "cancellation stops at the next native-call boundary");
        calls.Clear(); rejected = false;
        try { MonitorDiagnostics.Capture(device, caps, Read, cancellation.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && calls.Count == 0, "already cancelled captures never contact hardware");
        var isolated = MonitorDiagnostics.Capture(device, caps, code => code == 0x10 ? throw new IOException() : Read(code));
        Check(isolated.Readings.Single(r => r.Code == 0x10).Status == "failed" && isolated.Firmware == "1.2", "one failing control does not abort other reads");
        var tooMany = Report(false);
        for (var i = 0; i < MonitorDiagnostics.MaxObservations; i++) tooMany.Observations.Add(new() { Label = "test", Snapshot = sample });
        rejected = false;
        try { MonitorDiagnostics.Serialize(tooMany); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "observation count bounded");
        Console.WriteLine("PASS: monitor diagnostic raw capture, status-aware diff, identity isolation, privacy, malformed capabilities and cancellation.");
    }
}
