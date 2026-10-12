using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentControl.Services;

// An isolated console host, started with redirected pipes and no visible window.
if (args.Length != 2 || !int.TryParse(args[0], out var parentId) || !long.TryParse(args[1], out var parentStart)) return;
Process parent;
try
{
    parent = Process.GetProcessById(parentId);
    if (parent.StartTime.ToUniversalTime().Ticks != parentStart) return;
}
catch { return; }
// Also terminate on an abrupt UI crash while native I/O is blocked. Do not
// destroy handles from the watcher while the request thread is using them.
_ = Task.Run(() => { using (parent) parent.WaitForExit(); Process.GetCurrentProcess().Kill(); });
using var native = new NativeDdc();
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (line.Length > 4096) break;
    DdcResponse response;
    try
    {
        var request = JsonSerializer.Deserialize<DdcRequest>(line) ?? throw new InvalidDataException();
        response = native.Execute(request);
    }
    catch (Exception) { response = new(13); }
    Console.WriteLine(JsonSerializer.Serialize(response));
}

internal sealed class NativeDdc : IDisposable
{
    private PhysicalMonitor[] monitors = Array.Empty<PhysicalMonitor>();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetVCPFeature(nint monitor, byte code, uint value);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorCapabilities(nint monitor, out uint capabilities, out uint temperatures);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorColorTemperature(nint monitor, out uint temperature);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorColorTemperature(nint monitor, uint temperature);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitor(nint monitor);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetCapabilitiesStringLength(nint monitor, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern bool CapabilitiesRequestAndCapabilitiesReply(nint monitor, System.Text.StringBuilder text, uint length);


    public DdcResponse Execute(DdcRequest request)
    {
        if (request.Operation == "open") return Open(request);
        if (request.Monitor < 0 || request.Monitor >= monitors.Length) return new(unchecked((int)0xC026258C));
        var handle = monitors[request.Monitor].Handle;
        switch (request.Operation)
        {
            case "read":
                var ok = GetVCPFeatureAndVCPFeatureReply(handle, request.Code, out var type, out var current, out var maximum);
                return new(ok ? 0 : Marshal.GetLastWin32Error(), type, current, maximum);
            case "write":
                if (request.Value > 65535) return new(87);
                return new(SetVCPFeature(handle, request.Code, request.Value) ? 0 : Marshal.GetLastWin32Error());
            case "capabilities":
                if (!GetCapabilitiesStringLength(handle, out var length)) return new(Marshal.GetLastWin32Error());
                if (length is 0 or > 65536) return new(13);
                var buffer = new System.Text.StringBuilder((int)length);
                return CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length) ? new(Text: buffer.ToString()) : new(Marshal.GetLastWin32Error());
            case "temperature-flags":
                return GetMonitorCapabilities(handle, out var flags, out var temperatures) ? new(Current: (flags & 8) != 0 ? temperatures & 255 : 0) : new(Marshal.GetLastWin32Error());
            case "temperature-read":
                return GetMonitorColorTemperature(handle, out var temperature) ? new(Current: temperature) : new(Marshal.GetLastWin32Error());
            case "temperature-write":
                if (request.Value > 8) return new(87);
                return new(SetMonitorColorTemperature(handle, request.Value) ? 0 : Marshal.GetLastWin32Error());
            default: return new(87);
        }
    }
    private DdcResponse Open(DdcRequest request)
    {
        if (monitors.Length != 0) return new(87);
        var result = new DdcResponse(unchecked((int)0xC026258D));
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info) || info.Device != request.Connection) return true;
                var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                EnumDisplayDevices(info.Device, 0, ref display, 1);
                if (!string.Equals(display.Id ?? "", request.Identity, StringComparison.OrdinalIgnoreCase)) return false;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count)) { result = new(Marshal.GetLastWin32Error()); return false; }
                if (count > 64) { result = new(13); return false; }
                if (count == 0) { result = new(Descriptions: Array.Empty<string>()); return false; }
                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) { result = new(Marshal.GetLastWin32Error()); return false; }
                monitors = physical;
                result = new(Descriptions: physical.Select(p => p.Description).ToArray());
            }
            catch { result = new(13); }
            return false;
        };
        EnumDisplayMonitors(0, 0, callback, 0); GC.KeepAlive(callback);
        return result;
    }
    public void Dispose()
    {
        foreach (var monitor in monitors) DestroyPhysicalMonitor(monitor.Handle);
        monitors = Array.Empty<PhysicalMonitor>();
    }
}
