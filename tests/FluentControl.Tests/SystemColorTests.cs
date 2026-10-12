using FluentControl.Services;
using System.Runtime.InteropServices;

internal static class SystemColorTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("System color: " + message); }
    private static ushort[] Identity() => Enumerable.Range(0, 768).Select(i => (ushort)((i % 256) * 257)).ToArray();
    private sealed class Backend : ISystemColorBackend
    {
        internal Dictionary<string, ushort[]> Ramps = new() { ["a"] = Identity(), ["b"] = Identity() };
        internal bool Allowed = true, Ignore, FailWrite, FailRead;
        internal int Writes;
        internal string Token = "original-target";
        public ColorReading Read(string id)
        {
            if (FailRead) throw new IOException("read failed");
            return new(Token + id, Allowed, Ramps[id].ToArray());
        }
        public void Write(string id, string token, ushort[] ramp)
        {
            Writes++; if (FailWrite) throw new IOException("write failed");
            if (!Ignore) Ramps[id] = ramp.ToArray();
        }
    }
    private static void Fails(Action action)
    { var failed = false; try { action(); } catch { failed = true; } Check(failed, "expected rejected operation"); }
    internal static void Run()
    {
        Check(Marshal.SizeOf<WindowsDisplayHdrBackend.SourceName>() == 84, "source-name ABI");
        Check(WindowsDisplayHdrBackend.Decode(true, 0, 0).LegacyGammaAllowed, "SDR without HDR support is allowed");
        foreach (var state in new[] { WindowsDisplayHdrBackend.Decode(true, 0x33, 2), WindowsDisplayHdrBackend.Decode(true, 0xC3, 1), WindowsDisplayHdrBackend.Decode(false, 7), DisplayHdrState.Unknown() })
            Check(!state.LegacyGammaAllowed, "HDR, ACM/WCG and unknown modes must block legacy gamma");
        var original = Identity(); var warm = SoftwareWhiteBalance.Transform(original, 4500); var cool = SoftwareWhiteBalance.Transform(original, 7500);
        Check(SoftwareWhiteBalance.Transform(original, 6500).SequenceEqual(original), "neutral preserves exact baseline");
        Check(warm[255] == original[255] && warm[767] < original[767] && cool[767] == original[767] && cool[255] < original[255], "warm/cool directions");
        foreach (var k in new[] { 2500, 4500, 6500, 10000 }) Check(SoftwareWhiteBalance.ValidRamp(SoftwareWhiteBalance.Transform(original, k)), "monotonic nonblack ramps");
        Check(!SoftwareWhiteBalance.ValidRamp(new ushort[768]), "reject black/invalid initial ramps");
        var dir = Path.Combine(Path.GetTempPath(), "fc-color-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var b = new Backend(); var path = Path.Combine(dir, "recovery.json"); var c = new SystemColorService(b, path);
            Check(c.Read("a").Kelvin == 0 && b.Writes == 0 && !File.Exists(path), "discovery is read-only and creates no recovery baseline");
            foreach (var invalid in new[] { -1d, 1, 2400, 10001, 4500.5, double.NaN }) Fails(() => c.Set("a", invalid));
            Check(b.Writes == 0, "invalid values never write");
            c.Set("a", 4500); c.Set("a", 7500);
            Check(b.Ramps["a"].SequenceEqual(cool) && b.Ramps["b"].SequenceEqual(original), "always transform original, isolate screens");
            c.Restore("a"); Check(b.Ramps["a"].SequenceEqual(original) && !c.HasSessions, "restore exact baseline");
            c.Set("a", 4500); c.Set("b", 7500); c.RestoreAll();
            Check(b.Ramps.Values.All(r => r.SequenceEqual(original)) && !c.HasSessions, "normal exit restores both owned displays");
            c.Set("a", 4500); var writes = b.Writes;
            var recovered = new SystemColorService(b, path);
            Check(recovered.Read("a").Recovery && b.Writes == writes, "restart reads durable recovery without applying");
            recovered.RestoreAll(); Check(b.Writes == writes, "exiting fresh process does not auto-restore a recovered session");
            Fails(() => recovered.Set("a", 7500)); recovered.Restore("a");
            Check(b.Ramps["a"].SequenceEqual(original), "explicit crash recovery");
            c = new(b, path); c.Set("a", 4500);
            var other = SoftwareWhiteBalance.Transform(original, 10000); b.Ramps["a"] = other; writes = b.Writes;
            c.RestoreAll(); Check(b.Writes == writes && b.Ramps["a"].SequenceEqual(other), "never overwrite newer external calibration at exit");
            c.Set("a", 4500); c.Restore("a"); Check(b.Ramps["a"].SequenceEqual(other), "new explicit session uses latest baseline");
            b.Ramps["a"] = original.ToArray(); c.Set("a", 4500); b.Allowed = false; writes = b.Writes;
            Fails(() => c.Set("a", 7500)); c.RestoreAll(); Check(!c.Read("a").Available && b.Writes == writes && c.HasSessions, "external HDR change never writes legacy gamma and keeps recovery");
            b.Allowed = true; b.Token = "new-target"; c.RestoreAll(); Check(b.Writes == writes, "reused source identity is not restored");
            b.Ramps["a"] = original.ToArray(); b.Ignore = true;
            Fails(() => c.Set("a", 4500)); Check(c.Read("a").Recovery, "TRUE with ignored ramp is unconfirmed, not success");
            b.Ignore = false; c.Restore("a");
            b.FailWrite = true; Fails(() => c.Set("a", 4500)); b.FailWrite = false;
            Check(new SystemColorService(b, path).Read("a").Recovery, "failure after journaling remains recoverable");
            c.Restore("a"); b.FailRead = true; writes = b.Writes; Fails(() => c.Set("a", 4500)); Check(b.Writes == writes, "read failure prevents writing"); b.FailRead = false;
            b.Ramps["a"] = new ushort[768]; Fails(() => c.Set("a", 4500)); Check(!c.Read("a").Available && b.Writes == writes, "invalid ramp cannot be a baseline");
            File.WriteAllText(path, "{bad"); Fails(() => new SystemColorService(b, path)); Check(File.ReadAllText(path) == "{bad", "corrupt recovery is preserved");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("PASS: software color isolation, original LUT preservation, conflict ownership, HDR/ACM guard, silent-write rejection, durable recovery and read-only discovery (simulated).");
    }
}
