using FluentControl.Services;

internal static class StartupTemperatureTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static void Run()
    {
        var attempts = 0; var delays = new List<int>();
        StartupService.RetrySharingViolation(() =>
        {
            if (++attempts < 3) throw new IOException("sharing violation", unchecked((int)0x80070020));
        }, delays.Add);
        Check(attempts == 3 && delays.SequenceEqual(new[] { 40, 80 }), "Sharing conflicts retry with bounded backoff.");
        attempts = 0;
        try { StartupService.RetrySharingViolation(() => { attempts++; throw new UnauthorizedAccessException(); }, _ => { }); }
        catch (UnauthorizedAccessException) { }
        Check(attempts == 1, "Permissions are not treated as transient file locks.");
        attempts = 0;
        try { StartupService.RetrySharingViolation(() => { attempts++; throw new IOException("busy", unchecked((int)0x80070020)); }, _ => { }); }
        catch (IOException) { }
        Check(attempts == 6, "A permanent lock must stop retrying.");

        var raw = 0x0205u; var writes = new List<uint>(); var readCount = 0; var settle = false;
        var caps = VcpCapabilities.Parse("(vcp(14(04 05 08 0B)))");
        var preset = VcpDiscovery.Discover("display", caps,
            code => code != 0x14 ? null : new VcpReply(++readCount == 1 && settle ? 0x0204u : raw, 0),
            (_, value) => { writes.Add(value); raw = 0x0200 | value; }).Single(f => f.Definition.Code == 0x14).Channel!;
        Check(preset.Value == 5 && preset.Options!.Single(o => o.Value == 5).Label == "6500 K", "Preset read must ignore tolerance high byte.");
        foreach (var pair in new[] { (Raw: 4u, Label: "5000 K"), (Raw: 5u, Label: "6500 K"), (Raw: 8u, Label: "9300 K") })
        {
            Check(preset.Options!.Single(o => o.Value == pair.Raw).Label == pair.Label, "Kelvin labels use raw MCCS identifiers.");
            Check(ControlOperations.Apply(new[] { preset }, pair.Raw).Count == 0 && writes.Last() == pair.Raw && preset.Value == pair.Raw,
                "5000/6500/9300 K must round-trip the same raw preset, without a positional shift or high-byte write.");
        }
        settle = true; readCount = 0;
        Check(ControlOperations.Apply(new[] { preset }, 5).Count == 0 && readCount == 2, "Delayed preset readback settles without resending.");
        settle = false;
        var alias = MonitorColorTemperature.Create("display", preset, 0,
            () => throw new Exception("Windows API must not be mixed with VCP"), _ => throw new Exception("Wrong API"))!;
        Check(alias.CompatibilityOnly && alias.Options!.Select(o => o.Value).SequenceEqual(new double[] { 2, 3, 6 }), "Sparse Windows compatibility enums are mapped explicitly.");
        foreach (var pair in new[] { (Windows: 2, Vcp: 4u), (Windows: 3, Vcp: 5u), (Windows: 6, Vcp: 8u) })
            Check(ControlOperations.Apply(new[] { alias }, pair.Windows).Count == 0 && writes.Last() == pair.Vcp && alias.Value == pair.Windows && preset.Value == pair.Vcp,
                "Legacy temperatures use the same raw read/write source as the visible preset.");
        var nativeCurrent = 2u;
        var fallback = MonitorColorTemperature.Create("fallback", null, 0x26, () => nativeCurrent, value => nativeCurrent = value)!;
        Check(!fallback.CompatibilityOnly && fallback.Options!.Select(o => o.Value).SequenceEqual(new double[] { 2, 3, 6 }), "Native-only devices retain sparse Windows enum values.");
        Check(ControlOperations.Apply(new[] { fallback }, 6).Count == 0 && nativeCurrent == 6, "Native fallback does not use the filtered option index.");
        var mismatch = new ControlChannel { Name = "preset", Detail = "", Glyph = "", Options = preset.Options,
            Write = _ => { }, Read = () => 6, Value = 4, VerifyChoiceReadback = true };
        Check(ControlOperations.Apply(new[] { mismatch }, 8).Count == 1 && mismatch.Value == 6,
            "A real mismatch is reported and preserves actual hardware state; never fake success or guess a correction.");
        var canonicalKey = ProfileGroups.MonitorKey("display", "color-preset");
        var values = new Dictionary<string, SavedValue> { [canonicalKey] = new() { Value = 5 } };
        var available = new Dictionary<string, ControlChannel> { [canonicalKey] = preset };
        Check(MonitorColorTemperature.IsSuperseded(alias, values, available), "Saved canonical color preset takes precedence over stale legacy duplicate.");
        values[canonicalKey].Value = 99;
        Check(!MonitorColorTemperature.IsSuperseded(alias, values, available), "Invalid canonical value must not block a usable legacy temperature.");

        var second = new ControlChannel { Name = "M2", DeviceId = "two", Detail = "", Glyph = "", PropertyKey = "color-preset", Options = new[] { new ControlOption(8, "9300 K"), new ControlOption(11, "Custom") }, Write = _ => { } };
        var desktop = MonitorLinking.DesktopGroups(new[] { preset, second, alias,
            new ControlChannel { Name = "RGB", Detail = "", Glyph = "", PropertyKey = "gain-red", Write = _ => { } },
            new ControlChannel { Name = "Game", Detail = "", Glyph = "", PropertyKey = "display-mode", Write = _ => { } },
            new ControlChannel { Name = "Reset", Detail = "", Glyph = "", PropertyKey = "factory-reset", IsAction = true, Write = _ => { } },
            new ControlChannel { Name = "Power", Detail = "", Glyph = "", PropertyKey = "power", RequiresConfirmation = true, Write = _ => { } } }).ToArray();
        Check(desktop.Select(g => g.Key).SequenceEqual(new[] { "color-preset", "gain-red", "display-mode" }) && desktop[0].Count() == 2,
            "Desktop aggregation includes all safe properties without alias duplication or destructive actions.");
        var union = MonitorLinking.DesktopOptions(new[] { preset, second }, c => c.DeviceId)!;
        Check(union.Single(o => o.Value == 8).Label == "9300 K" && union.Single(o => o.Value == 4).Label.Contains("display"), "Desktop enum union identifies partially supported options.");
        Console.WriteLine("PASS: startup retry policy, preset namespaces/readback/legacy scenes and complete desktop aggregation.");
    }

    internal static void RunNative()
    {
        foreach (var apartment in new[] { ApartmentState.STA, ApartmentState.MTA })
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                var directory = Path.Combine(Path.GetTempPath(), "FluentControl-shortcuts-" + Guid.NewGuid());
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "FC.lnk");
                var executable = Environment.ProcessPath!;
                try
                {
                    for (var i = 0; i < 4; i++)
                    {
                        StartupService.SetShortcut(path, executable, true);
                        Check(StartupService.ShortcutMatches(path, executable), "STA/MTA shortcut must be readable after COM release.");
                    }
                    var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var unlock = Task.Run(async () => { await Task.Delay(140); locked.Dispose(); });
                    try { StartupService.SetShortcut(path, executable, true); }
                    finally { unlock.GetAwaiter().GetResult(); locked.Dispose(); }
                    var original = File.ReadAllBytes(path);
                    using (var permanent = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var rejected = false;
                        try { StartupService.SetShortcut(path, executable, true); } catch (IOException) { rejected = true; }
                        Check(rejected && File.ReadAllBytes(path).SequenceEqual(original), "A permanent target lock preserves the existing startup entry.");
                    }
                    Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Staging files must be cleaned after success or failure.");
                    StartupService.SetShortcut(path, executable, false);
                    Check(!File.Exists(path), "Disable deletes the test shortcut after all readers have closed.");
                }
                catch (Exception ex) { failure = ex; }
                finally { Directory.Delete(directory, true); }
            });
            thread.SetApartmentState(apartment); thread.Start(); thread.Join();
            if (failure is not null) throw new Exception("Native shortcut regression failed: " + apartment, failure);
        }
        Console.WriteLine("PASS: STA/MTA shortcut release before rename, transient/permanent external locks, replace/disable and staging cleanup.");
    }
}
