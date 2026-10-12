using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FluentControl.Services;

public enum HdrAvailability { Available, Unsupported, Limited, Unknown, Disconnected, Ambiguous }

// Enabled is the user's Windows HDR setting; Active is the current signal mode.
// On new Windows versions these may differ (for example under a power policy).
public sealed record DisplayHdrState(HdrAvailability Availability, bool? Enabled = null, bool? Active = null, string Error = "")
{
    public bool LegacyGammaAllowed { get; init; }
    public bool CanSet => Availability == HdrAvailability.Available || Availability == HdrAvailability.Limited && Enabled == true;
    public static DisplayHdrState Unknown(string error = "") => new(HdrAvailability.Unknown, Error: error);
}

internal sealed record HdrTarget(string DevicePath, uint AdapterLow, int AdapterHigh, uint TargetId, bool Modern, DisplayHdrState State)
{
    public string SourceName { get; init; } = "";
    public int SourceTargetCount { get; init; }
}
internal interface IDisplayHdrBackend
{
    IReadOnlyList<HdrTarget> ReadActive();
    int Write(HdrTarget target, bool enabled);
}
internal sealed record HdrChange(bool Sent, bool Confirmed, DisplayHdrState State, string Error = "");

internal sealed class DisplayHdrController(IDisplayHdrBackend backend, Action<string>? log = null, Action? wait = null)
{
    internal IReadOnlyDictionary<string, DisplayHdrState> ReadAll()
    {
        // An enumeration failure must remain unknown, never become "unsupported".
        return backend.ReadActive().GroupBy(t => t.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().State : new DisplayHdrState(HdrAvailability.Ambiguous), StringComparer.OrdinalIgnoreCase);
    }

    private HdrTarget? Find(string path, out DisplayHdrState state)
    {
        var matches = backend.ReadActive().Where(t => string.Equals(t.DevicePath, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        state = matches.Length switch { 0 => new(HdrAvailability.Disconnected), 1 => matches[0].State, _ => new(HdrAvailability.Ambiguous) };
        return matches.Length == 1 ? matches[0] : null;
    }

    // Caller serializes this operation with monitor writes/refresh/disposal.
    internal HdrChange Set(string path, bool enabled)
    {
        var sent = false;
        try
        {
            // Re-resolve the exact interface path immediately before writing. Never
            // reuse an adapter/target ID cached before a hotplug or topology change.
            var target = Find(path, out var state);
            if (target is null || !state.CanSet) return new(false, false, state, state.Error);
            if (state.Enabled == enabled && state.Active == enabled) return new(false, true, state);
            if (enabled && state.Availability == HdrAvailability.Limited) return new(false, false, state);
            var error = backend.Write(target, enabled); // Exactly one write, no fallback/retry.
            log?.Invoke($"Windows HDR write: target={target.TargetId}, modern={target.Modern}, enabled={enabled}, error={error}");
            if (error != 0) return new(false, false, state, new Win32Exception(error).Message + $" ({error})");
            sent = true;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                if (wait is null) Thread.Sleep(250); else wait();
                try { Find(path, out state); }
                catch (Exception ex) { state = DisplayHdrState.Unknown(ex.Message); }
                if (state.Enabled == enabled && state.Active == enabled)
                    return new(true, true, state);
            }
            log?.Invoke($"Windows HDR readback unconfirmed: enabled={state.Enabled}, active={state.Active}, availability={state.Availability}");
            return new(true, false, state, state.Error);
        }
        catch (Exception ex)
        {
            log?.Invoke("Windows HDR: " + ex);
            return new(sent, false, DisplayHdrState.Unknown(ex.Message), ex.Message);
        }
    }
}

internal sealed class WindowsDisplayHdrBackend : IDisplayHdrBackend
{
    // SDK: wingdi.h. INFO_2 distinguishes HDR from Windows auto color management.
    internal static DisplayHdrState Decode(bool modern, uint flags, uint activeMode = 0)
    {
        if (modern)
            return new((flags & 16) == 0 ? HdrAvailability.Unsupported : (flags & 8) != 0 ? HdrAvailability.Limited : HdrAvailability.Available,
                (flags & 32) != 0, activeMode == 2) { LegacyGammaAllowed = activeMode == 0 && (flags & (2 | 32 | 128)) == 0 };
        if ((flags & 4) != 0) return DisplayHdrState.Unknown(); // Legacy wideColorEnforced is not HDR.
        return new((flags & 1) == 0 ? HdrAvailability.Unsupported : (flags & 8) != 0 ? HdrAvailability.Limited : HdrAvailability.Available,
            (flags & 2) != 0, (flags & 2) != 0) { LegacyGammaAllowed = (flags & 6) == 0 };
    }

    public IReadOnlyList<HdrTarget> ReadActive()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var modern = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var error = GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount);
            if (error != 0) throw new Win32Exception(error);
            if (pathCount > 256 || modeCount > 1024) throw new InvalidDataException("Display topology is too large.");
            var paths = new DisplayPath[pathCount];
            var modes = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * 64));
            try
            {
                error = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, 0);
                if (error == 122) continue;
                if (error != 0) throw new Win32Exception(error);
                var result = new List<HdrTarget>();
                foreach (var path in paths.Take((int)pathCount))
                {
                    var name = new TargetName { Header = Header.For<TargetName>(2, path.Target.Adapter, path.Target.Id) };
                    if (GetTargetName(ref name) != 0 || string.IsNullOrWhiteSpace(name.Path)) continue;
                    DisplayHdrState state;
                    if (modern)
                    {
                        var info = new ColorInfo2 { Header = Header.For<ColorInfo2>(15, path.Target.Adapter, path.Target.Id) };
                        error = GetColorInfo2(ref info);
                        state = error == 0 ? Decode(true, info.Flags, info.ActiveMode) : DisplayHdrState.Unknown(new Win32Exception(error).Message + $" ({error})");
                    }
                    else
                    {
                        var info = new ColorInfo { Header = Header.For<ColorInfo>(9, path.Target.Adapter, path.Target.Id) };
                        error = GetColorInfo(ref info);
                        state = error == 0 ? Decode(false, info.Flags) : DisplayHdrState.Unknown(new Win32Exception(error).Message + $" ({error})");
                    }
                    var source = new SourceName { Header = Header.For<SourceName>(1, path.Source.Adapter, path.Source.Id) };
                    var sourceOk = GetSourceName(ref source) == 0;
                    result.Add(new(name.Path, path.Target.Adapter.Low, path.Target.Adapter.High, path.Target.Id, modern, state)
                    {
                        SourceName = sourceOk ? source.Name : "",
                        SourceTargetCount = paths.Take((int)pathCount).Count(p => p.Source.Id == path.Source.Id && p.Source.Adapter.Low == path.Source.Adapter.Low && p.Source.Adapter.High == path.Source.Adapter.High)
                    });
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        throw new Win32Exception(122);
    }

    public int Write(HdrTarget target, bool enabled)
    {
        var request = new ColorState
        {
            Header = Header.For<ColorState>(target.Modern ? 16u : 10u, new() { Low = target.AdapterLow, High = target.AdapterHigh }, target.TargetId),
            Flags = enabled ? 1u : 0u
        };
        return DisplayConfigSetDeviceInfo(ref request);
    }

    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header
    {
        public uint Type, Size;
        public Luid Adapter;
        public uint Id;
        internal static Header For<T>(uint type, Luid adapter, uint id) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct Target
    {
        public Luid Adapter;
        public uint Id, Mode, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanOrder;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetName
    {
        public Header Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo { public Header Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo2 { public Header Header; public uint Flags, Encoding, Bits, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorState { public Header Header; public uint Flags; }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] data, ref uint modes, nint modeData, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)] private static extern int GetTargetName(ref TargetName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)] private static extern int GetSourceName(ref SourceName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo(ref ColorInfo info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo2(ref ColorInfo2 info);
    [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(ref ColorState state);
}
