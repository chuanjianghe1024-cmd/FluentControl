using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FluentControl.Services;

public sealed class MonitorService : IDisposable
{
    private readonly List<nint> handles = new();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetVCPFeature(nint monitor, byte code, uint value);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitor(nint monitor);

    public List<ControlChannel> Enumerate()
    {
        var channels = new List<ControlChannel>();
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return true;
            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
            foreach (var item in physical)
            {
                handles.Add(item.Handle);
                foreach (var property in new[] { (Code: (byte)0x10, Name: "亮度"), (Code: (byte)0x12, Name: "对比度"), (Code: (byte)0x62, Name: "扬声器音量") })
                {
                    var handle = item.Handle;
                    var code = property.Code;
                    if (!GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out var current, out var max) || max == 0 || current > max) continue;
                    channels.Add(new ControlChannel
                    {
                        Name = string.IsNullOrWhiteSpace(item.Description) ? "外接显示器" : item.Description,
                        Detail = $"{property.Name} · DDC/CI",
                        Glyph = "\uE7F4",
                        Value = current * 100d / max,
                        // Keep requests serialized by the UI worker; re-query on explicit refresh.
                        Write = value =>
                        {
                            if (!SetVCPFeature(handle, code, (uint)Math.Round(Math.Clamp(value, 0, 100) * max / 100)))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "显示器未接受指令，请检查 DDC/CI、HDR 或节能模式。");
                        }
                    });
                }
            }
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new InvalidOperationException("无法枚举显示器。");
        GC.KeepAlive(callback);
        return channels;
    }
    public void Dispose()
    {
        foreach (var handle in handles) DestroyPhysicalMonitor(handle);
        handles.Clear();
    }
}
