using FluentControl.Services;

internal static class OsdCandidateTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Advertised OSD candidates: " + message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid advertised candidate was accepted"); }
    internal static void Run()
    {
        var device = new MonitorDevice { Id = "synthetic-pairing", Connection = "fixture", Model = "Synthetic", ModelId = "TST1234", FirmwareVersion = "0.1",
            CapabilitiesText = "(vcp(02 04 05 08 4D(02 01 02) 52 AA(01 02) AC AE B2 B6 C6 CA FD FF) mccs_ver(2.2))" };
        var reads = new List<byte>(); var writes = new List<(byte, uint)>();
        var baseline = MonitorDiagnostics.Capture(device, new(device.CapabilitiesText, "fixture"), code => { reads.Add(code); return new(1, 99, 65535); });
        var features = OsdCandidateDiscovery.Discover(baseline);
        Check(features.Select(f => f.Code).SequenceEqual(new byte[] { 0x4D, 0xFD, 0xFF }), "unknown codes below E0 and private codes appear; known standard items are excluded");
        Check(features[0].Values.SequenceEqual(new byte[] { 1, 2 }) && features[1].Values.Count == 0, "only declared options are enumerated, not current/maximum values");
        Check(!reads.Any(OsdCandidateDiscovery.IsUnknown), "discovery never probes unknown codes");
        Check(OsdCandidateDiscovery.Discover(new() { CapabilitiesParsed = false, Readings = baseline.Readings }).Count == 0 &&
            OsdCandidateDiscovery.Discover(new() { CapabilitiesParsed = true, CapabilitiesErrorCode = 31, Readings = baseline.Readings }).Count == 0, "missing or failed capabilities cannot generate candidates");
        Check(OsdCandidateDiscovery.Discover(new() { CapabilitiesParsed = true, Readings = new() { new() { Code = 0xE2, Advertised = false } } }).Count == 0, "unadvertised unknowns are not inferred");
        var profile = OsdPairingProfile.Create(device, true); var session = new OsdSessionRecord { Baseline = baseline }; profile.Sessions.Add(session);
        var command = OsdCandidateDiscovery.GetOrAdd(profile, features[0], 2);
        var probe = OsdCandidateDiscovery.GetOrAdd(profile, features[1], 1);
        Check(command.Discovery == "capability-option" && probe.Discovery == "capability-experiment" && probe.DeclaredValues.Count == 0, "advertised and experimental values have distinct provenance");
        Check(ReferenceEquals(command, OsdCandidateDiscovery.GetOrAdd(profile, features[0], 2)) && profile.Commands.Count == 5, "repeated tests reuse the candidate without discarding observations");
        Reject(() => features[0].CreateCommand(3, device.ModelId));
        Reject(() => features[1].CreateCommand(65536, device.ModelId));
        Reject(() => new OsdAdvertisedFeature { Code = 0x04 }.CreateCommand(1, device.ModelId));
        var engine = new OsdPairingSession(code => { reads.Add(code); return new(0, 0, 0, 31); }, (code, value) => writes.Add((code, value)));
        var readCount = reads.Count;
        Reject(() => engine.Send(command, session.Id, "main-menu", false, default));
        Check(reads.Count == readCount && writes.Count == 0, "imported metadata alone cannot read or write hardware");
        engine.SetCapabilities(baseline);
        Check(writes.Count == 0, "populating and selecting candidates does not write");
        var trial = engine.Send(command, session.Id, "main-menu", false, default); profile.Trials.Add(trial);
        Check(writes.Single() == ((byte)0x4D, 2u) && trial.WriteSucceeded && trial.After?.Succeeded == false && profile.Bindings.Count == 0, "one explicit test writes once; unreadable state does not bind an effect");
        profile.Confirm(trial, "success", new[] { "up" }, "Observed movement");
        var replay = engine.Send(profile.BoundCommand("up")!, session.Id, "main-menu", false, default); profile.Trials.Add(replay);
        Check(writes.Count == 2 && writes.Last() == writes.First(), "locally confirmed action replays its exact code/value");
        var experiment = engine.Send(probe, session.Id, "main-menu", false, default); profile.Trials.Add(experiment);
        Check(writes.Count == 3 && writes.Last() == ((byte)0xFD, 1u), "no-option code accepts a user-selected single experimental value");
        var json = OsdPairingStore.Serialize(profile); var restored = OsdPairingStore.Parse(json);
        Check(restored.Version == 3 && restored.Trials[0].Command.DeclaredValues.SequenceEqual(new byte[] { 1, 2 }) && restored.Trials[2].Command.Discovery == "capability-experiment", "file roundtrip retains declared values, experimental provenance and observations");
        var imported = OsdPairingProfile.Create(device, true); imported.ImportCandidates(restored);
        Check(imported.Bindings.Count == 0 && imported.Commands.Any(c => c.FromCapabilities), "import keeps candidate metadata but requires local confirmation");
        engine.SetCapabilities(new() { CapabilitiesParsed = true, Readings = new() { new() { Code = 0x4D, Advertised = true, AdvertisedOptions = new() { 1 } } } });
        Check(!engine.CanSend(command) && !engine.CanSend(probe), "changed or missing current declarations disable old candidates");
        Reject(() => engine.Send(command, session.Id, "main-menu", false, default));
        Check(writes.Count == 3, "rejected replay does not write");
        var legacy = OsdPairingStore.Parse(OsdPairingStore.Serialize(OsdPairingProfile.Create(device, true)).Replace("\"version\": 3", "\"version\": 1"));
        Check(legacy.Commands.Count == 3, "existing local pairing files remain readable");
        Console.WriteLine("PASS: advertised unknown OSD discovery, explicit single-send experiments, observed bindings, provenance, fresh-capability validation and offline files.");
    }
}
