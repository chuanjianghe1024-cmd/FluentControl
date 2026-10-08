using System.Runtime.InteropServices;
namespace FluentControl.Services;

internal static class MonitorNames
{
    internal static Dictionary<string, string> ReadActive()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (GetDisplayConfigBufferSizes(2, out var paths, out var modes) != 0 || paths > 256 || modes > 1024) break;
                var data = new DisplayPath[paths]; var modeBuffer = Marshal.AllocHGlobal(checked((int)Math.Max(1, modes) * 64));
                try
                {
                    var status = QueryDisplayConfig(2, ref paths, data, ref modes, modeBuffer, 0);
                    if (status == 122) continue;
                    if (status != 0) break;
                    foreach (var path in data.Take((int)paths))
                    {
                        var target = new TargetName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                        if (DisplayConfigGetDeviceInfo(ref target) == 0 && !string.IsNullOrWhiteSpace(target.Path) && !string.IsNullOrWhiteSpace(target.FriendlyName)) result[target.Path] = target.FriendlyName.Trim();
                    }
                    break;
                }
                finally { Marshal.FreeHGlobal(modeBuffer); }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or ArgumentException) { }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id, Mode, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Target { public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanOrder; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] data, ref uint modes, nint modeData, nint topology);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref TargetName name);
}
