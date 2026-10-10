using FluentControl.Services;
using System.ComponentModel;

internal static class OsdPairingTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("OSD pairing: " + message); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("OSD pairing: accepted " + message); }
    internal static void Run()
    {
        var device = new MonitorDevice { Id = "PRIVATE_INSTANCE", Model = "Fixture", ModelId = "TST1234", FirmwareVersion = "0.1", Connection = "PRIVATE_PATH", CapabilitiesText = "(serial(PRIVATE_SERIAL) vcp(CA E1) mccs_ver(2.2))" };
        var profile = OsdPairingProfile.Create(device, true);
        var session = new OsdSessionRecord { Baseline = new() { DeviceId = device.Id, ModelId = device.ModelId, RawCapabilities = device.CapabilitiesText } };
        profile.Sessions.Add(session);
        var writes = new List<(byte, uint)>(); bool unreadable = false;
        var engine = new OsdPairingSession(_ => unreadable ? new(0, 0, 0, unchecked((int)0xC0262589)) : new(1, 0x0201, 2),
            (c, v) => { writes.Add((c, v)); unreadable = true; });
        var enable = profile.Commands[0];
        var trial = engine.Send(enable, session.Id, "closed", false, default); profile.Trials.Add(trial);
        Check(trial.WriteSucceeded && trial.After is { Succeeded: false, Current: null, Maximum: null } && writes.Single() == ((byte)0xCA, 0x0202u), "failed readback must preserve successful transport and high byte without fabricating zero state");
        Check(profile.Bindings.Count == 0, "transport cannot bind automatically");
        profile.Confirm(trial, "success", new[] { "open", "enable" }, "Physical menu opens");
        Check(profile.BoundCommand("open") == enable, "explicit local observations bind actions");
        var blocked = engine.Send(profile.Commands[1], session.Id, "main-menu", false, default);
        Check(!blocked.WriteSucceeded && writes.Count == 1, "failed pre-read blocks default write");
        var recovery = engine.Send(profile.Commands[1], session.Id, "main-menu", true, default);
        Check(recovery.WriteSucceeded && recovery.UsedSessionButtonFields && writes.Last().Item2 == 0x0201, "explicit session recovery preserves captured button fields");
        var reopened = new OsdPairingSession(_ => new(0, 0, 0, 31), (c, v) => writes.Add((c, v)));
        Check(!reopened.Send(enable, session.Id, "unknown", true, default).WriteSucceeded && writes.Count == 2, "no cached fields survive reopening");
        var failed = new OsdPairingSession(_ => new(1, 0x0201, 2), (_, _) => throw new Win32Exception(31)).Send(enable, session.Id, "closed", false, default);
        profile.Trials.Add(failed); Check(!failed.WriteSucceeded && failed.WriteErrorCode == 31, "native write errors retained");
        Reject(() => profile.Confirm(failed, "success", new[] { "open" }, ""), "failed writes as bindings");
        profile.Confirm(failed, "failure", Array.Empty<string>(), "No response");
        Check(profile.BoundCommand("open") is null, "a user-confirmed failure revokes previous bindings for that command");
        profile.Confirm(trial, "success", new[] { "open" }, "Retested");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); try { engine.Send(enable, session.Id, "closed", true, cts.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
            Check(writes.Count == 2, "cancelled queued commands never write");
        }
        var eventReads = new List<byte>(); var captureEngine = new OsdPairingSession(c => { eventReads.Add(c); return new(0, 0, 0, 50); }, (_, _) => throw new Exception("Event capture wrote"));
        var capture = captureEngine.Observe(session.Id, "Joystick up", default); profile.EventCaptures.Add(capture);
        Check(eventReads.SequenceEqual(new byte[] { 2, 0x52, 3, 2, 0x52, 3 }), "bounded event reads stop on repeated unsupported replies without writes");
        var json = OsdPairingStore.Serialize(profile);
        Check(!json.Contains("PRIVATE_"), "default exports omit instance paths and raw capability identifiers");
        Check(OsdPairingStore.Serialize(profile, true).Contains("PRIVATE_SERIAL"), "raw capabilities require explicit export opt-in");
        var roundtrip = OsdPairingStore.Parse(json); Check(roundtrip.Bindings.Count == 1 && roundtrip.Trials.Count == 2, "round-trip preserves observed evidence");
        var imported = OsdPairingProfile.Create(device, true); imported.ImportCandidates(roundtrip);
        Check(imported.Bindings.Count == 0 && imported.EventCaptures.Count == 1 && imported.Trials.All(t => t.SourceProfileId == roundtrip.Id), "imports preserve evidence without granting remote buttons");
        Reject(() => imported.Confirm(imported.Trials[0], "success", new[] { "up" }, ""), "imported observations as local confirmations");
        var mismatch = OsdPairingProfile.Create(device, true); mismatch.Identity.Firmware = "0.2";
        Reject(() => mismatch.ImportCandidates(roundtrip), "wrong firmware");
        Reject(() => OsdPairingStore.Parse("{}"), "missing format");
        Reject(() => OsdPairingStore.Parse(json.Replace("\"commands\": [", "\"commands\": null, \"unknown\": [")), "unknown JSON fields");
        foreach (var code in new byte[] { 4, 5, 8, 0xC6, 0xD6, 0x52 }) Reject(() => new OsdCommand { Name = "Bad", Source = "Invalid candidate", Code = code, Value = 1 }.Validate(), "non-navigation/reset command");
        Check(OsdPairingStore.TryHex("0xCA", 255, out var parsed) && parsed == 202 && !OsdPairingStore.TryHex("10x1", 65535, out _), "strict hexadecimal parsing");
        var directory = Path.Combine(Path.GetTempPath(), "FC-pairing-" + Guid.NewGuid());
        try
        {
            var store = new OsdPairingStore(directory, device.Id); store.Save(profile);
            Check(store.Load(profile.Identity, true)?.Bindings.Count == 1, "offline persistence");
            Check(new OsdPairingStore(directory, "OTHER_DEVICE").Load(profile.Identity, true) is null, "same-model monitors require separate local verification");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine("PASS: local OSD pairing, explicit observation, failed readback, session recovery, event capture, offline storage and import isolation.");
    }
}
