using FluentControl.Services;
using System.Runtime.InteropServices;

internal static class DisplayHdrTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Windows HDR: " + message); }

    private sealed class Backend : IDisplayHdrBackend
    {
        internal List<HdrTarget> Targets = new()
        {
            new("panel-a", 9, 0, 1, true, new(HdrAvailability.Available, false, false)),
            new("panel-b", 9, 0, 2, true, new(HdrAvailability.Unsupported, false, false))
        };
        internal int Writes, Reads, Error;
        internal bool Apply = true, FailRead;
        internal HdrTarget? WrittenTarget;
        internal Action<Backend>? BeforeRead;
        public IReadOnlyList<HdrTarget> ReadActive()
        {
            Reads++; BeforeRead?.Invoke(this);
            if (FailRead) throw new IOException("Unavailable");
            return Targets.ToArray();
        }
        public int Write(HdrTarget target, bool enabled)
        {
            WrittenTarget = target; Writes++;
            if (Error == 0 && Apply)
            {
                var i = Targets.FindIndex(t => t.DevicePath == target.DevicePath);
                Targets[i] = target with { State = target.State with { Enabled = enabled, Active = enabled } };
            }
            return Error;
        }
    }

    internal static void Run()
    {
        // ABI offsets/sizes from Microsoft's wingdi.h, including 64-byte mode union.
        Check(Marshal.SizeOf<WindowsDisplayHdrBackend.DisplayPath>() == 72 && Marshal.SizeOf<WindowsDisplayHdrBackend.Header>() == 20, "CCD path/header ABI");
        Check(Marshal.SizeOf<WindowsDisplayHdrBackend.TargetName>() == 420 && Marshal.SizeOf<WindowsDisplayHdrBackend.ColorInfo>() == 32 &&
            Marshal.SizeOf<WindowsDisplayHdrBackend.ColorInfo2>() == 36 && Marshal.SizeOf<WindowsDisplayHdrBackend.ColorState>() == 24, "device-info ABI");
        Check(Marshal.OffsetOf<WindowsDisplayHdrBackend.ColorInfo2>("ActiveMode").ToInt32() == 32, "new active mode offset");
        Check(WindowsDisplayHdrBackend.Decode(true, 0xD3, 1).Enabled == false && WindowsDisplayHdrBackend.Decode(true, 0xD3, 1).Active == false,
            "WCG/ACM is not HDR even when advanced color is active");
        Check(WindowsDisplayHdrBackend.Decode(true, 0x33, 2).Active == true, "new HDR mode");
        Check(WindowsDisplayHdrBackend.Decode(true, 0x33, 0).Enabled == true && WindowsDisplayHdrBackend.Decode(true, 0x33, 0).Active == false, "requested HDR differs from active signal");
        Check(WindowsDisplayHdrBackend.Decode(false, 3).CanSet && WindowsDisplayHdrBackend.Decode(false, 3).Active == true, "legacy HDR");
        Check(!WindowsDisplayHdrBackend.Decode(false, 7).CanSet, "do not toggle legacy enforced WCG as HDR");
        Check(!WindowsDisplayHdrBackend.Decode(true, 0x18).CanSet && WindowsDisplayHdrBackend.Decode(true, 0x38).CanSet, "policy blocks enable but permits explicit disable");

        var b = new Backend(); var c = new DisplayHdrController(b, wait: () => { });
        Check(c.ReadAll().Count == 2 && b.Writes == 0, "discovery never writes and preserves per-screen partial support");
        b.Targets[0] = b.Targets[0] with { TargetId = 42, AdapterLow = 11 };
        var changed = c.Set("PANEL-A", true);
        Check(changed.Confirmed && changed.Sent && b.Writes == 1 && b.WrittenTarget!.TargetId == 42 && b.WrittenTarget.AdapterLow == 11,
            "resolve current exact path after topology change, do not reuse stale IDs");
        Check(b.Targets[1].State.Enabled == false, "second screen untouched");
        Check(c.Set("panel-a", true).Confirmed && b.Writes == 1, "no-op performs no write");
        Check(!c.Set("panel-b", true).Sent && !c.Set("missing", true).Sent && b.Writes == 1, "unsupported/disconnected never written");
        b.Targets.Add(b.Targets[0]);
        Check(c.ReadAll()["panel-a"].Availability == HdrAvailability.Ambiguous && !c.Set("panel-a", false).Sent && b.Writes == 1, "ambiguous identity never guessed");
        b.Targets.RemoveAt(2); b.Error = 5;
        Check(!c.Set("panel-a", false).Confirmed && b.Writes == 2, "native failure sent only once");
        b.Error = 0; b.Apply = false; var before = b.Reads;
        changed = c.Set("panel-a", false);
        Check(changed.Sent && !changed.Confirmed && b.Writes == 3 && b.Reads - before == 13 && changed.State.Enabled == true,
            "bounded readback mismatch remains unconfirmed with real state; no write retry");
        b.BeforeRead = backend => { if (backend.Writes >= 4) backend.FailRead = true; };
        changed = c.Set("panel-a", false);
        Check(changed.Sent && !changed.Confirmed && changed.State.Enabled is null && b.Writes == 4, "read failure cannot fabricate disabled state");
        b.BeforeRead = null; b.FailRead = false; b.Apply = true;
        b.BeforeRead = backend => { if (backend.Writes == 5) backend.Targets.Clear(); };
        changed = c.Set("panel-a", false);
        Check(!changed.Confirmed && changed.State.Availability == HdrAvailability.Disconnected && b.Writes == 5, "disconnect after write not mistaken for success");
        Console.WriteLine("Windows HDR controller checks passed (simulated; no native HDR writes).");
    }
}
