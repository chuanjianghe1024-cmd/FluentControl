using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FluentControl.Services;

public sealed class MonitorDevice
{
    public required string Id { get; init; }
    public required string Model { get; init; }
    public required string Connection { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
    public string ModelId { get; init; } = "";
    public string CapabilitiesText { get; set; } = "";
    public List<MonitorFeature> Features { get; } = new();
    public MonitorPreference Preference { get; set; } = new();
    public List<ControlChannel> Channels { get; } = new();
    public string DisplayName => Preference.DisplayName;
}

public sealed class MonitorService : IDisposable
{
    private readonly List<nint> handles = new();
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

    public List<MonitorDevice> Enumerate()
    {
        var devices = new List<MonitorDevice>();
        var friendlyNames = MonitorNames.ReadActive();
        Exception? callbackError = null;
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                PhysicalMonitor[] physical = Array.Empty<PhysicalMonitor>();
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) && count > 0)
                {
                    var found = new PhysicalMonitor[count];
                    if (GetPhysicalMonitorsFromHMONITOR(monitor, count, found))
                    {
                        physical = found;
                        handles.AddRange(found.Select(x => x.Handle));
                    }
                }
                for (var i = 0; i < Math.Max(physical.Length, 1); i++)
                {
                    var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                    var hasIdentity = EnumDisplayDevices(info.Device, (uint)i, ref display, 1);
                    var item = physical.Length > 0 ? physical[i] : default;
                    var model = !string.IsNullOrWhiteSpace(item.Description) ? item.Description : display.Description;
                    if (!string.IsNullOrEmpty(display.Id) && friendlyNames.TryGetValue(display.Id, out var friendly)) model = friendly;
                    var device = new MonitorDevice
                    {
                        Id = hasIdentity && !string.IsNullOrWhiteSpace(display.Id) ? display.Id.ToUpperInvariant() : $"{info.Device}/{model}/{i}",
                        Model = string.IsNullOrWhiteSpace(model) ? "显示器" : model,
                        ModelId = ModelIdentity.FromDevicePath(display.Id ?? ""),
                        Connection = info.Device,
                        Left = info.Monitor.Left, Top = info.Monitor.Top,
                        Width = info.Monitor.Right - info.Monitor.Left, Height = info.Monitor.Bottom - info.Monitor.Top,
                        IsPrimary = (info.Flags & 1) != 0
                    };
                    devices.Add(device);
                    if (physical.Length == 0)
                    {
                        device.Features.AddRange(VcpCatalog.All.Select(d => new MonitorFeature { Definition = d, Reason = Strings.T("DDC/CI 不可用", "DDC/CI unavailable") }));
                        continue;
                    }
                    if (GetMonitorCapabilities(item.Handle, out var capabilities, out var temperatureFlags) &&
                        (capabilities & 8) != 0 && GetMonitorColorTemperature(item.Handle, out var temperature))
                    {
                        var kelvin = new[] { 4000, 5000, 6500, 7500, 8200, 9300, 10000, 11500 };
                        var options = Enumerable.Range(0, 8).Where(n => (temperatureFlags & (1u << n)) != 0)
                            .Select(n => new ControlOption(n + 1, kelvin[n] + " K")).ToArray();
                        var temperatureHandle = item.Handle;
                        if (options.Any(x => x.Value == temperature)) device.Channels.Add(new ControlChannel
                        {
                            Name = "色温", Detail = "", DeviceId = device.Id, PropertyKey = "temperature", Glyph = "\uE753",
                            Value = temperature, Minimum = 1, Maximum = 8, Unit = "", Options = options,
                            Read = () => GetMonitorColorTemperature(temperatureHandle, out var t) ? t : throw new Win32Exception(Marshal.GetLastWin32Error()),
                            Write = value =>
                            {
                                if (!options.Any(x => x.Value == value)) throw new ArgumentOutOfRangeException(nameof(value));
                                if (!SetMonitorColorTemperature(temperatureHandle, (uint)value))
                                    throw new Win32Exception(Marshal.GetLastWin32Error(), device.DisplayName + " — " + Strings.T("无法设置色温", "Color temperature was rejected"));
                            }
                        });
                    }
                    var handle = item.Handle;
                    if (GetCapabilitiesStringLength(handle, out var length) && length is > 0 and <= 65536)
                    {
                        var buffer = new System.Text.StringBuilder((int)length);
                        if (CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) device.CapabilitiesText = buffer.ToString();
                    }
                    VcpReply? Read(byte code) => GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out var current, out var maximum) ? new VcpReply(current, maximum) : null;
                    void Write(byte code, uint value)
                    {
                        if (!SetVCPFeature(handle, code, value)) throw new Win32Exception(Marshal.GetLastWin32Error(), device.DisplayName + $" · VCP 0x{code:X2}: " + Strings.T("未接受指令，请检查 DDC/CI、HDR 或节能模式。", "Command rejected. Check DDC/CI, HDR or power-saving mode."));
                    }
                    device.Features.AddRange(VcpDiscovery.Discover(device.Id, VcpCapabilities.Parse(device.CapabilitiesText), Read, Write));
                    device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
                }
                return true;
            }
            catch (Exception ex) { callbackError = ex; return false; }
        };
        var success = EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        if (callbackError is not null) throw callbackError;
        if (!success) throw new InvalidOperationException(Strings.T("无法枚举显示器。", "Cannot enumerate monitors."));
        return devices.OrderBy(x => x.Left).ThenBy(x => x.Top).ThenBy(x => x.Id).ToList();
    }
    public void Dispose()
    {
        foreach (var handle in handles) DestroyPhysicalMonitor(handle);
        handles.Clear();
    }
}
