using System.Text.Json;
using System.Runtime.InteropServices;
using static FluentControl.Services.Strings;

namespace FluentControl.Services;

internal sealed record ColorReading(string Token, bool Allowed, ushort[] Ramp);
internal interface ISystemColorBackend
{
    ColorReading Read(string devicePath);
    void Write(string devicePath, string token, ushort[] expected, ushort[] ramp);
}
internal sealed record SystemColorStatus(bool Available, int? Kelvin, bool Recovery = false, string Error = "");
internal sealed class ColorSession
{
    public string Token { get; set; } = "";
    public ushort[] Original { get; set; } = Array.Empty<ushort>();
    public ushort[] Last { get; set; } = Array.Empty<ushort>();
    public ushort[]? Pending { get; set; }
    public int Kelvin { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Recovery { get; set; }
}

// Software white-balance effect relative to the captured LUT, not a measured
// panel CCT or a replacement ICC calibration. 6500 K is an exact identity.
internal static class SoftwareWhiteBalance
{
    internal static bool Valid(double value) => double.IsFinite(value) && (value == 0 || value >= 2500 && value <= 10000 && value == Math.Round(value));
    internal static ushort[] Transform(ushort[] baseline, int kelvin)
    {
        if (baseline.Length != 768 || !Valid(kelvin) || kelvin == 0) throw new ArgumentOutOfRangeException(nameof(kelvin));
        // Relative Planck radiance at representative RGB wavelengths, converted
        // to approximate gamma-encoded gains. This is deliberately an estimate.
        var wavelengths = new[] { 610e-9, 550e-9, 460e-9 };
        var ratios = wavelengths.Select(w => (Math.Exp(.01438776877 / (w * 6500)) - 1) / (Math.Exp(.01438776877 / (w * kelvin)) - 1)).ToArray();
        var max = ratios.Max(); var result = new ushort[768];
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < 256; i++) result[c * 256 + i] = (ushort)Math.Round(baseline[c * 256 + i] * Math.Pow(ratios[c] / max, 1 / 2.2));
        return result;
    }
    internal static bool Same(ushort[] a, ushort[] b) => a.Length == 768 && b.Length == 768 && a.Zip(b).All(p => Math.Abs((int)p.First - p.Second) <= 256);
    internal static bool ValidRamp(ushort[] a) => a.Length == 768 && Enumerable.Range(0, 3).All(c =>
        a[c * 256 + 255] > a[c * 256] + 4096 && Enumerable.Range(1, 255).All(i => a[c * 256 + i] >= a[c * 256 + i - 1]));
}

internal sealed class SystemColorService
{
    private readonly ISystemColorBackend backend;
    private readonly string path;
    private readonly Dictionary<string, ColorSession> sessions;
    private readonly Action<string>? log;
    internal bool HasSessions => sessions.Count > 0;
    internal SystemColorService(ISystemColorBackend backend, string path, Action<string>? log = null)
    {
        this.backend = backend; this.path = path; this.log = log;
        sessions = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Color recovery file is too large.");
        var loaded = JsonSerializer.Deserialize<Dictionary<string, ColorSession>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid color recovery file.");
        if (loaded.Count > 32) throw new InvalidDataException("Too many color recovery entries.");
        foreach (var (id, s) in loaded)
        {
            if (id.Length is 0 or > 1024 || s is null || string.IsNullOrEmpty(s.Token) || s.Token.Length > 2048 || s.Original?.Length != 768 ||
                s.Last?.Length != 768 || s.Pending is not null && s.Pending.Length != 768 || !SoftwareWhiteBalance.Valid(s.Kelvin) || !SoftwareWhiteBalance.ValidRamp(s.Original))
                throw new InvalidDataException("Invalid color recovery entry.");
            s.Recovery = true; sessions.Add(id, s);
        }
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sessions)); File.Move(temp, path, true);
    }
    private static string Blocked => T("仅支持可确认的 SDR 独立屏幕；HDR、自动颜色管理或复制模式下请使用 Windows 夜间模式。", "Requires a confirmed SDR display with an independent output. Use Windows night light for HDR, auto color management or duplicated displays.");
    private static string External => T("颜色曲线已被其他程序或显示模式改变，FC 已停止接管；再次应用将以当前颜色为基准。", "The color curve changed outside FC. Control was released; applying again will use the current colors as the baseline.");
    internal static void EnsureUnchanged(ushort[] expected, ushort[] current)
    {
        if (!SoftwareWhiteBalance.Same(expected, current)) throw new IOException(External);
    }
    private static bool Owned(ColorSession s, ColorReading r) => s.Token == r.Token && (SoftwareWhiteBalance.Same(s.Last, r.Ramp) || s.Pending is not null && SoftwareWhiteBalance.Same(s.Pending, r.Ramp));
    internal SystemColorStatus Read(string id)
    {
        try
        {
            var r = backend.Read(id);
            if (!r.Allowed) return new(false, null, sessions.ContainsKey(id), Blocked);
            if (!SoftwareWhiteBalance.ValidRamp(r.Ramp)) return new(false, null, sessions.ContainsKey(id), T("系统颜色曲线无效，未进行修改。", "The system color curve is invalid. No changes were made."));
            if (sessions.TryGetValue(id, out var s))
            {
                if (!Owned(s, r))
                {
                    // Never overwrite a newer calibration or restore onto a reused source.
                    sessions.Remove(id); Save(); return new(true, null, false, External);
                }
                if (s.Recovery || s.Pending is not null) return new(true, null, true);
                return new(true, s.Kelvin);
            }
            return new(true, 0);
        }
        catch (Exception ex) { return new(false, null, sessions.ContainsKey(id), ex.Message); }
    }
    internal void Set(string id, double value)
    {
        if (!SoftwareWhiteBalance.Valid(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) { if (!Restore(id)) throw new InvalidOperationException(External); return; }
        var r = backend.Read(id);
        if (!r.Allowed) throw new InvalidOperationException(Blocked);
        if (!SoftwareWhiteBalance.ValidRamp(r.Ramp)) throw new IOException(T("系统颜色曲线无效，未进行修改。", "The system color curve is invalid. No changes were made."));
        if (sessions.TryGetValue(id, out var s) && !Owned(s, r))
        { sessions.Remove(id); Save(); throw new InvalidOperationException(External); }
        if (s is { Recovery: true } || s?.Pending is not null)
            throw new InvalidOperationException(T("请先恢复上次的系统颜色，再应用新色温。", "Restore the previous system colors before applying a new temperature."));
        if (s is null)
        {
            if (sessions.Count >= 32) throw new InvalidOperationException("Too many active color sessions.");
            s = new() { Token = r.Token, Original = r.Ramp.ToArray(), Last = r.Ramp.ToArray() };
            sessions.Add(id, s);
        }
        var next = SoftwareWhiteBalance.Transform(s.Original, (int)value);
        s.Pending = next; Save(); // Durable recovery before the native write.
        backend.Write(id, r.Token, r.Ramp, next);
        var actual = backend.Read(id);
        if (!actual.Allowed || actual.Token != r.Token || !SoftwareWhiteBalance.Same(next, actual.Ramp))
            throw new IOException(T("系统未确认色温曲线已生效，请重新读取或恢复。", "The system did not confirm the color curve. Refresh or restore colors."));
        s.Last = actual.Ramp.ToArray(); s.Kelvin = (int)value; s.Pending = null; Save();
        log?.Invoke($"System color applied and read back: {value} K (approximate)");
    }
    internal bool Restore(string id)
    {
        if (!sessions.TryGetValue(id, out var s)) return true;
        var r = backend.Read(id);
        if (!r.Allowed) throw new InvalidOperationException(Blocked);
        if (!Owned(s, r)) { sessions.Remove(id); Save(); log?.Invoke("System color restore skipped: newer external curve or target changed."); return false; }
        // Pending may be the only record of an applied but unconfirmed write.
        // Preserve the observed owned curve before replacing that intent, so a
        // failed restore can still be retried, including after a process restart.
        s.Last = r.Ramp.ToArray();
        var original = s.Original.ToArray(); s.Pending = original; Save();
        backend.Write(id, r.Token, r.Ramp, original);
        var actual = backend.Read(id);
        if (!actual.Allowed || actual.Token != r.Token || !SoftwareWhiteBalance.Same(original, actual.Ramp))
            throw new IOException(T("原颜色尚未恢复，请重新读取后重试。", "Original colors are not restored yet. Refresh and try again."));
        sessions.Remove(id); Save(); log?.Invoke("System color baseline restored.");
        return true;
    }
    internal void RestoreAll()
    {
        foreach (var id in sessions.Where(p => !p.Value.Recovery).Select(p => p.Key).ToArray())
            try { Restore(id); } catch (Exception ex) { log?.Invoke("System color restore deferred: " + ex.Message); }
    }
}

internal sealed class WindowsSystemColorBackend : ISystemColorBackend
{
    private readonly WindowsDisplayHdrBackend displays = new();
    private HdrTarget Resolve(string id)
    {
        var matches = displays.ReadActive().Where(t => string.Equals(t.DevicePath, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new IOException(T("无法确认显示器连接，请重新读取设备。", "Cannot identify the display connection. Refresh devices."));
        return matches[0];
    }
    private static bool Allowed(HdrTarget t) => t.State.LegacyGammaAllowed && t.SourceTargetCount == 1 && !string.IsNullOrWhiteSpace(t.SourceName);
    private static string Token(HdrTarget t) => $"{t.DevicePath.ToUpperInvariant()}|{t.AdapterHigh}:{t.AdapterLow}:{t.TargetId}|{t.SourceName}";
    public ColorReading Read(string id)
    {
        var t = Resolve(id);
        if (!Allowed(t)) return new(Token(t), false, Array.Empty<ushort>());
        return new(Token(t), true, WithDc(t, dc =>
        {
            var ramp = new ushort[768];
            if (!GetDeviceGammaRamp(dc, ramp)) throw new IOException(T("显卡驱动无法读取系统颜色曲线。", "The graphics driver cannot read the system color curve."));
            return ramp;
        }));
    }
    public void Write(string id, string token, ushort[] expected, ushort[] ramp)
    {
        var t = Resolve(id); // No cached source IDs/DCs; check mode again before every write.
        if (!Allowed(t) || Token(t) != token || ramp.Length != 768) throw new IOException(T("显示模式已变化，请重新读取设备。", "Display mode changed. Refresh devices."));
        WithDc(t, dc =>
        {
            // Journaling and topology resolution can take time. Recheck ownership
            // on the same DC immediately before writing. GDI offers no atomic CAS;
            // an external writer racing the final native call remains possible.
            var current = new ushort[768];
            if (!GetDeviceGammaRamp(dc, current)) throw new IOException(T("显卡驱动无法读取系统颜色曲线。", "The graphics driver cannot read the system color curve."));
            SystemColorService.EnsureUnchanged(expected, current);
            if (!SetDeviceGammaRamp(dc, ramp)) throw new IOException(T("显卡驱动未接受系统颜色曲线。", "The graphics driver did not accept the system color curve."));
            return true;
        });
    }
    private static T WithDc<T>(HdrTarget t, Func<nint, T> run)
    {
        var dc = CreateDC("DISPLAY", t.SourceName, null, 0);
        if (dc == 0) throw new IOException("Cannot open display DC.");
        try { return run(dc); } finally { DeleteDC(dc); }
    }
    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode)] private static extern nint CreateDC(string driver, string device, string? output, nint mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetDeviceGammaRamp(nint dc, [Out] ushort[] ramp);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetDeviceGammaRamp(nint dc, [In] ushort[] ramp);
}
