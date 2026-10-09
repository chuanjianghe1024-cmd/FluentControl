using FluentControl.Services;
using System.Text.Json;

internal static class PresetLibraryTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var profile = new ControlProfile { Name = "Office", Applications = new() { "Editor" }, Monitors = new()
        { ["left"] = new() { ModelId = "DEL1234", ModelName = "Panel" }, ["right"] = new() { ModelId = "DEL1234", ModelName = "Panel" } }, Values = new()
        { [ProfileGroups.MonitorKey("left", "brightness")] = new() { Value = 40 }, [ProfileGroups.MonitorKey("right", "brightness")] = new() { Value = 40 }, ["audio/mic/volume"] = new() { Value = 75, Muted = true }, ["mouse/mouse-speed"] = new() { Value = 8 } } };
        var state = new UserState { Profiles = new() { profile }, SelectedProfileId = profile.Id };
        ProfileGroups.Normalize(state); MonitorPresetLibrary.Migrate(state); MonitorPresetLibrary.Migrate(state);
        Check(state.MonitorPresets.Count == 1 && profile.MonitorPresetIds.Count == 2 && profile.MonitorPresetIds.Values.Distinct().Count() == 1, "Same model can reuse one preset; migration is idempotent.");
        Check(profile.Values.Count == 4 && profile.Values["audio/mic/volume"].Muted == true, "Global scene retains microphone and mouse.");
        state.MonitorPresets[0].Values["brightness"] = 80;
        Check(profile.Values[ProfileGroups.MonitorKey("left", "brightness")].Value == 40, "Editing library preset cannot mutate global snapshots.");
        var exported = MonitorPresetLibrary.Export(state.MonitorPresets, "Model library");
        var shared = ProfileExchange.SerializeBundle(exported);
        Check(!shared.Contains("audio/") && !shared.Contains("mouse/") && !shared.Contains("left"), "Public library export contains no local device identities or non-monitor state.");
        var imported = MonitorPresetLibrary.Import(ProfileExchange.ParseBundle(shared), Array.Empty<MonitorPreset>(), "download");
        Check(imported.Single().Scenario == "Office" && imported[0].Applications.Single() == "Editor", "Scenario and application metadata round trip.");
        Check(MonitorPresetLibrary.Import(exported, imported, "same file").Count == 0, "Repeated imports do not duplicate identical model presets.");
        var target = new MonitorDevice { Id = "new-screen", ModelId = "HWV4321", Model = "Other panel", Connection = "test" };
        var secondConnected = new MonitorDevice { Id = "other-screen", ModelId = "TST0002", Model = "Unsaved panel", Connection = "test" };
        var sameModel = new MonitorDevice { Id = "same-model-screen", ModelId = "HWV4321", Model = "Other panel", Connection = "test" };
        var models = MonitorPresetLibrary.GroupModels(imported, new[] { target, secondConnected, sameModel });
        Check(models.Select(m => m.ModelId).SequenceEqual(new[] { "HWV4321", "TST0002", "DEL1234" }) && imported.Count == 1,
            "Library includes unsaved connected models once and preserves offline saved models without creating presets.");
        var written = new List<double>();
        target.Channels.Add(new() { Name = "Brightness", Detail = "", Glyph = "", PropertyKey = "brightness", Value = 10, Write = written.Add });
        target.Channels.Add(new() { Name = "Color", Detail = "", Glyph = "", PropertyKey = "color-preset", Options = new[] { new ControlOption(5, "6500K") }, Write = _ => throw new Exception("Unsupported preset sent") });
        var preset = imported[0]; preset.Values["color-preset"] = 8; preset.Values["speaker"] = 20; preset.Values["power"] = 4;
        var plan = MonitorPresetLibrary.Plan(preset, target);
        Check(plan.Applicable.Count == 1 && plan.Skipped.Count == 3 && !MonitorPresetLibrary.SameModel(preset, target), "Cross-model plans intersect actual options and reject unsafe commands.");
        foreach (var item in plan.Applicable) ControlOperations.Apply(new[] { item.Channel }, item.Value);
        Check(written.SequenceEqual(new[] { 80d }), "Cross-model application sends only compatible values.");
        ProfileUpdates.Merge(profile, new Dictionary<string, SavedValue> { ["mouse/mouse-speed"] = new() { Value = 12 } }, new Dictionary<string, MonitorDescriptor>(), new Dictionary<string, BrightnessMapping>());
        Check(profile.Monitors.Count == 2 && profile.Values["audio/mic/volume"].Value == 75 && profile.Values["mouse/mouse-speed"].Value == 12, "Offline screen and audio snapshots survive global updates.");
        var directory = Path.Combine(Path.GetTempPath(), "fc-library-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "state.json"); state.ProfileOrganizationVersion = 0;
            var original = JsonSerializer.Serialize(state); File.WriteAllText(path, original);
            var migrated = new UserStateStore(path);
            Check(File.ReadAllText(path + ".before-model-library-v1.bak") == original && migrated.State.ProfileOrganizationVersion == 1, "Migration preserves the exact pre-migration backup.");
            var count = migrated.State.MonitorPresets.Count; var reopened = new UserStateStore(path);
            Check(reopened.State.MonitorPresets.Count == count, "Reopening does not duplicate migrated presets.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: model library migration, shared presets, global snapshots, cross-model options and offline preservation");
    }
}
