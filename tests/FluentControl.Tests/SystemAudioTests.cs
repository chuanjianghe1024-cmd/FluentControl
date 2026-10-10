using System.Text.Json;
using FluentControl.Services;

internal static class SystemAudioTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("System audio: " + message); }

    private sealed class FakeEndpoint(double volume, bool muted = false)
    {
        internal SystemAudioValue Value = new(volume, muted);
        internal bool Available = true;
    }
    private sealed class Backend : ISystemAudioBackend
    {
        internal FakeEndpoint Output = new(30), Input = new(60);
        internal int Writes;
        internal bool Disposed;
        public event Action? Changed;
        private FakeEndpoint Current(SystemAudioTarget target)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            var endpoint = target == SystemAudioTarget.Output ? Output : Input;
            if (!endpoint.Available) throw new IOException("Disconnected");
            return endpoint;
        }
        public SystemAudioValue Read(SystemAudioTarget target) => Current(target).Value;
        public SystemAudioValue SetVolume(SystemAudioTarget target, double volume)
        {
            var endpoint = Current(target); Writes++;
            endpoint.Value = endpoint.Value with { Volume = volume };
            Changed?.Invoke(); return endpoint.Value;
        }
        public SystemAudioValue SetMute(SystemAudioTarget target, bool muted)
        {
            var endpoint = Current(target); Writes++;
            endpoint.Value = endpoint.Value with { Muted = muted };
            Changed?.Invoke(); return endpoint.Value;
        }
        public void Dispose() { Disposed = true; Changed = null; }
    }

    internal static void Run()
    {
        using var backend = new Backend();
        var controls = new SystemAudioControls(backend);
        var output = controls.Channels.Single(c => c.DeviceId == SystemAudioControls.OutputId);
        var input = controls.Channels.Single(c => c.DeviceId == SystemAudioControls.InputId);
        Check(controls.Channels.Count == 2 && backend.Writes == 0, "initialization is read-only and exposes two logical controls");
        var previous = backend.Output;
        backend.Output = new(77, true); // Can be a physical or a virtual endpoint; no naming heuristic.
        Check(controls.Refresh() && output.Value == 77 && output.IsMuted && backend.Writes == 0,
            "changing the default reads its actual volume/mute without applying stale values");
        Check(ControlOperations.Apply(new[] { output }, 44).Count == 0 && backend.Output.Value.Volume == 44 && previous.Value.Volume == 30,
            "an existing logical control follows the new default and leaves the old endpoint alone");
        ControlOperations.SetMute(output, false);
        Check(!backend.Output.Value.Muted && !output.IsMuted, "mute follows the current default with readback");
        backend.Input.Available = false;
        controls.Refresh();
        Check(!input.IsAvailable && output.IsAvailable, "missing input does not hide or block output");
        var failed = ControlOperations.Apply(new[] { input }, 90);
        Check(failed.Count == 1 && input.Value == 60 && !input.IsAvailable, "disconnected input is not reported as success");
        backend.Input = new(12, true); controls.Refresh();
        Check(input.IsAvailable && input.Value == 12 && input.IsMuted, "a new input recovers without recreating profile keys");
        var writes = backend.Writes;
        Check(ControlOperations.Apply(new[] { output }, double.NaN).Count == 1 && backend.Writes == writes, "invalid values are never sent");
        ControlOperations.Apply(new[] { output }, 130);
        Check(backend.Output.Value.Volume == 100, "volume is clamped to the system range");
        backend.Dispose();
        Check(ControlOperations.Apply(new[] { output }, 20).Count == 1 && !output.IsAvailable, "stale channels cannot use a disposed backend");
        MigrationChecks();
        Console.WriteLine("PASS: system audio default switching, volume/mute readback, missing endpoint isolation, stable profile keys and legacy migration.");
    }

    private static void MigrationChecks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluentControl-system-audio-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "state.json");
            var profile = new ControlProfile { Name = "Legacy", Values = new()
            { ["audio/opaque-endpoint/volume"] = new() { Value = 83, Muted = true }, ["monitor/offline/brightness"] = new() { Value = 21 } } };
            var state = new UserState { ProfileOrganizationVersion = 1, Profiles = new() { profile }, Settings = new()
            {
                DesktopRows = new() { "monitor/all/brightness", "audio/opaque-endpoint/volume" },
                DesktopIndividualRows = new() { "audio/another-endpoint/volume", "monitor/offline/brightness" },
                DesktopLinkedRows = null
            } };
            var original = JsonSerializer.Serialize(state); File.WriteAllText(path, original);
            var store = new UserStateStore(path);
            Check(File.ReadAllText(path + ".before-system-audio-v1.bak") == original, "migration keeps the exact previous state file");
            Check(store.State.SystemAudioVersion == 1 && store.State.Settings.DesktopRows.SequenceEqual(new[]
                { "monitor/all/brightness", SystemAudioControls.OutputKey, SystemAudioControls.InputKey }), "old desktop selection becomes stable system rows");
            Check(store.State.Settings.DesktopIndividualRows!.Contains("monitor/offline/brightness") && store.State.Settings.DesktopLinkedRows is null,
                "independent desktop mode and offline monitor selections survive");
            profile = store.State.Profiles.Single();
            Check(profile.Values["audio/opaque-endpoint/volume"].Value == 83 && SystemAudioControls.NeedsProfileUpdate(profile),
                "legacy levels are retained with an explicit update requirement, never guessed from opaque IDs");
            ProfileUpdates.Merge(profile, new Dictionary<string, SavedValue>
            { [SystemAudioControls.OutputKey] = new() { Value = 40, Muted = false }, [SystemAudioControls.InputKey] = new() { Value = 70, Muted = true } },
                new Dictionary<string, MonitorDescriptor>(), new Dictionary<string, BrightnessMapping>());
            Check(!SystemAudioControls.NeedsProfileUpdate(profile) && profile.Values.ContainsKey("monitor/offline/brightness") && profile.Values.ContainsKey("audio/opaque-endpoint/volume"),
                "updating records logical levels while preserving legacy data and offline monitors");
            store.Save(); var persisted = File.ReadAllText(path);
            _ = new UserStateStore(path);
            Check(File.ReadAllText(path) == persisted && File.ReadAllText(path + ".before-system-audio-v1.bak") == original, "migration is idempotent and does not overwrite the backup");
        }
        finally { Directory.Delete(directory, true); }
    }
}
