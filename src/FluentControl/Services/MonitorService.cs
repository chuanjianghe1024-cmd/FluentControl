using System.ComponentModel;
using System.Diagnostics;
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
    public string FirmwareVersion { get; set; } = "";
    public MonitorAdapter? InstalledAdapter { get; set; }
    public List<MonitorFeature> Features { get; } = new();
    public MonitorPreference Preference { get; set; } = new();
    public List<ControlChannel> Channels { get; } = new();
    public string DisplayName => Preference.DisplayName;
}

public sealed class MonitorService : IDisposable
{
    private readonly List<nint> handles = new();
    private readonly Dictionary<MonitorDevice, nint> diagnosticHandles = new();
    private readonly MonitorCapabilityCache capabilityCache;
    private readonly MonitorAdapterStore adapters = new();
    private readonly Action<string>? diagnostic;
    public List<string> Errors { get; } = new();

    public MonitorService(Action<string>? diagnostic = null)
    {
        this.diagnostic = diagnostic;
        capabilityCache = new MonitorCapabilityCache(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentControl", "monitor-capabilities.json"), diagnostic);
    }

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

    public List<MonitorDevice> Enumerate(bool forceCapabilities = false)
    {
        var scan = Stopwatch.StartNew();
        Errors.Clear();
        var devices = new List<MonitorDevice>();
        var pending = new List<(MonitorDevice Device, nint Handle)>();
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
                        device.Features.AddRange(VcpCatalog.All.Select(d => new MonitorFeature { Definition = d, Reason = Strings.T("DDC/CI 不可用", "DDC/CI unavailable") }));
                    else { diagnosticHandles.Add(device, item.Handle); pending.Add((device, item.Handle)); }
                }
                return true;
            }
            catch (Exception ex) { callbackError = ex; return false; }
        };
        var success = EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        if (callbackError is not null || !success)
        {
            Dispose();
            throw callbackError ?? new InvalidOperationException(Strings.T("无法枚举显示器。", "Cannot enumerate monitors."));
        }
        var enumerationMs = scan.ElapsedMilliseconds;
        MonitorReadBatch.Run(pending, x => x.Device.Id,
            x => ReadControls(x.Device, x.Handle, forceCapabilities),
            (x, error) =>
            {
                lock (Errors) Errors.Add(x.Device.Model + "：" + error.Message);
                diagnostic?.Invoke($"Monitor scan failed [{x.Device.Model}]: {error}");
                foreach (var definition in VcpCatalog.All.Where(d => !x.Device.Features.Any(f => f.Definition.Code == d.Code)))
                    x.Device.Features.Add(new() { Definition = definition, Reason = Strings.T("不支持或当前无法读取", "Unsupported or currently unreadable") });
            });
        capabilityCache.Save();
        diagnostic?.Invoke($"Monitor scan complete: displays={devices.Count}, enumeration={enumerationMs}ms, total={scan.ElapsedMilliseconds}ms, forceCapabilities={forceCapabilities}");
        return devices.OrderBy(x => x.Left).ThenBy(x => x.Top).ThenBy(x => x.Id).ToList();
    }

    private void ReadControls(MonitorDevice device, nint handle, bool forceCapabilities)
    {
        var scan = Stopwatch.StartNew();
        var key = MonitorCapabilityCache.Identity(device.Id, device.Model, device.Connection);
        var metadata = capabilityCache.GetOrRead(key, () =>
        {
            var text = "";
            if (GetCapabilitiesStringLength(handle, out var length) && length is > 0 and <= 65536)
            {
                var buffer = new System.Text.StringBuilder((int)length);
                if (CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) text = buffer.ToString();
            }
            return new MonitorCapabilityData(text, ReadTemperatureFlags(handle));
        }, forceCapabilities, out var cacheHit);
        device.CapabilitiesText = metadata.VcpText;
        var capabilityMs = scan.ElapsedMilliseconds;
        var reads = 0; var failures = 0; var longestRead = 0L;
        VcpReply? Read(byte code)
        {
            var watch = Stopwatch.StartNew();
            var ok = GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out var current, out var maximum);
            var elapsed = watch.ElapsedMilliseconds;
            if (code == 0x14) diagnostic?.Invoke($"Color preset read [{device.Model}]: current=0x{current:X4}, maximum=0x{maximum:X4}, success={ok}");
            reads++; if (!ok) failures++; longestRead = Math.Max(longestRead, elapsed);
            if (elapsed >= 250) diagnostic?.Invoke($"Slow monitor read [{device.Model}]: VCP=0x{code:X2}, duration={elapsed}ms, success={ok}");
            return ok ? new VcpReply(current, maximum) : null;
        }
        void Write(byte code, uint value)
        {
            diagnostic?.Invoke($"Monitor write [{device.Model}]: VCP=0x{code:X2}, value=0x{value:X4}");
            if (!SetVCPFeature(handle, code, value)) throw new Win32Exception(Marshal.GetLastWin32Error(), device.DisplayName + $" · VCP 0x{code:X2}: " + Strings.T("未接受指令，请检查 DDC/CI、HDR 或节能模式。", "Command rejected. Check DDC/CI, HDR or power-saving mode."));
        }
        // The known read-only firmware query is needed even when capabilities omit C9.
        // Reuse this reply during discovery so each refresh reads it only once.
        var firmware = Read(0xC9);
        device.FirmwareVersion = firmware is VcpReply fw ? $"{fw.Current >> 8}.{fw.Current & 255}" : "";
        device.InstalledAdapter = adapters.Find(device.ModelId, device.FirmwareVersion);
        device.Features.AddRange(VcpDiscovery.Discover(device.Id, VcpCapabilities.Parse(device.CapabilitiesText), code => code == 0xC9 ? firmware : Read(code), Write, device.InstalledAdapter));
        foreach (var command in device.InstalledAdapter?.NativeMenu ?? new())
        {
            var name = Strings.T("原厂菜单", "Native menu") + " · " + NativeMenuName(command.Action);
            var keyName = "native-osd-" + command.Action;
            device.Features.Add(new()
            {
                Definition = new(command.Code, keyName, name, name, "extensions", VcpKind.Action, true),
                Channel = new()
                {
                    Name = name, Detail = device.InstalledAdapter!.Id, Glyph = "\uE700", DeviceId = device.Id, PropertyKey = keyName,
                    VcpCode = command.Code, IsAction = true, CanSave = false, RequiresConfirmation = true, Minimum = 1, Maximum = 1, Value = 1,
                    Write = value => { if (value != 1) throw new ArgumentOutOfRangeException(nameof(value)); Write(command.Code, command.Value); }
                }
            });
        }
        device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
        // Prefer a single raw VCP read/write namespace over two competing color controls.
        var preset = device.Channels.FirstOrDefault(c => c.PropertyKey == "color-preset");
        var flags = preset is null ? metadata.ColorTemperatureFlags ?? (cacheHit ? ReadTemperatureFlags(handle) : null) ?? 0 : 0;
        try
        {
            var temperature = MonitorColorTemperature.Create(device.Id, preset, flags,
                () => GetMonitorColorTemperature(handle, out var value) ? value : throw new Win32Exception(Marshal.GetLastWin32Error()),
                value =>
                {
                    diagnostic?.Invoke($"Windows color temperature write [{device.Model}]: enum={value}");
                    if (!SetMonitorColorTemperature(handle, value)) throw new Win32Exception(Marshal.GetLastWin32Error());
                });
            if (temperature is not null) device.Channels.Add(temperature);
        }
        catch (Exception ex) { diagnostic?.Invoke($"Color temperature unavailable [{device.Model}]: {ex.Message}"); }

        diagnostic?.Invoke($"Monitor read [{device.Model}]: capabilities={(cacheHit ? "cache" : "hardware")}, capabilitiesTime={capabilityMs}ms, vcpReads={reads}, failures={failures}, longestRead={longestRead}ms, total={scan.ElapsedMilliseconds}ms");
    }

    private static uint? ReadTemperatureFlags(nint handle) =>
        GetMonitorCapabilities(handle, out var flags, out var temperatures) ? (flags & 8) != 0 ? temperatures & 255 : 0 : null;

    internal static string NativeMenuName(string action) => action switch
    {
        "open" => Strings.T("打开", "Open"), "close" => Strings.T("关闭", "Close"), "up" => Strings.T("上", "Up"),
        "down" => Strings.T("下", "Down"), "left" => Strings.T("左", "Left"), "right" => Strings.T("右", "Right"),
        "enter" => Strings.T("确认", "Confirm"), _ => action
    };

    // Caller holds the same gate as refresh/disposal and writes, until all native calls finish.
    public MonitorDiagnosticSnapshot CaptureDiagnostics(MonitorDevice device, MonitorDiagnosticSnapshot? baseline,
        CancellationToken token, IProgress<DiagnosticProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (!diagnosticHandles.TryGetValue(device, out var handle))
            throw new InvalidOperationException(Strings.T("DDC/CI 不可用", "DDC/CI unavailable"));
        var watch = Stopwatch.StartNew();
        DiagnosticCapabilities capabilities;
        if (baseline is not null)
        {
            if (baseline.DeviceId != device.Id) throw new InvalidOperationException("Monitor connection changed.");
            capabilities = new(baseline.RawCapabilities ?? "", "baseline", baseline.CapabilitiesErrorCode);
        }
        else
        {
            // Explicit diagnostic session reads hardware metadata once, bypassing the regular cache.
            var text = ""; int? error = null;
            if (!GetCapabilitiesStringLength(handle, out var length)) error = Marshal.GetLastWin32Error();
            else if (length is 0 or > 65536) error = 13; // invalid data
            else
            {
                token.ThrowIfCancellationRequested();
                var buffer = new System.Text.StringBuilder((int)length);
                if (CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) text = buffer.ToString();
                else error = Marshal.GetLastWin32Error();
            }
            capabilities = new(text, error.HasValue ? "unavailable" : "hardware", error);
        }
        var snapshot = MonitorDiagnostics.Capture(device, capabilities, code =>
        {
            var ok = GetVCPFeatureAndVCPFeatureReply(handle, code, out var type, out var current, out var maximum);
            return new(type, current, maximum, ok ? null : Marshal.GetLastWin32Error());
        }, token, progress);
        diagnostic?.Invoke($"Monitor diagnostics: controls={snapshot.Readings.Count}, success={snapshot.Readings.Count(r => r.Status == "ok")}, total={watch.ElapsedMilliseconds}ms");
        return snapshot;
    }

    public void Dispose()
    {
        diagnosticHandles.Clear();
        foreach (var handle in handles) DestroyPhysicalMonitor(handle);
        handles.Clear();
    }
}
