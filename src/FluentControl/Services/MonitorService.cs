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
    public bool CapabilitiesPending { get; set; }
    public string DiscoveryWarning { get; set; } = "";
    public string DisplayName => Preference.DisplayName;
}

public sealed class MonitorService : IDisposable
{
    private sealed record Endpoint(DdcProcessClient Client, int Index)
    {
        public DdcResponse Call(string operation, byte code = 0, uint value = 0) => Client.Request(new(operation, Index, code, value));
    }
    private readonly List<DdcProcessClient> hosts = new();
    private readonly Dictionary<MonitorDevice, Endpoint> diagnosticHandles = new();
    private readonly DdcProbeRecovery recovery;
    private readonly MonitorCapabilityCache capabilityCache;
    private readonly MonitorAdapterStore adapters = new();
    private readonly Action<string>? diagnostic;
    public List<string> Errors { get; } = new();

    public MonitorService(Action<string>? diagnostic = null)
    {
        this.diagnostic = diagnostic;
        recovery = new DdcProbeRecovery(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "DdcRecovery"), diagnostic);
        capabilityCache = new MonitorCapabilityCache(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentControl", "monitor-capabilities.json"), diagnostic);
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
    // Phase one reads only live brightness/contrast. No capability query, firmware
    // scan or private-code probe can delay these controls becoming visible.
    public List<MonitorDevice> Enumerate(bool forceCapabilities = false)
    {
        Errors.Clear();
        var devices = new List<MonitorDevice>();
        var logical = new List<MonitorInfo>();
        var friendlyNames = MonitorNames.ReadActive();
        MonitorCallback callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) logical.Add(info);
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new IOException("Cannot enumerate displays.");
        GC.KeepAlive(callback);
        MonitorReadBatch.Run(logical, x => x.Device, info =>
        {
            var identity = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            EnumDisplayDevices(info.Device, 0, ref identity, 1);
            DdcProcessClient? host = null;
            string[] descriptions = Array.Empty<string>();
            string warning = "";
            try
            {
                host = new DdcProcessClient();
                lock (hosts) hosts.Add(host);
                var opened = host.Request(new("open", Connection: info.Device, Identity: identity.Id ?? ""));
                if (opened.Error != 0) throw new Win32Exception(opened.Error);
                descriptions = opened.Descriptions ?? Array.Empty<string>();
            }
            catch (Exception ex) { warning = ex.Message; host?.Dispose(); }
            for (var i = 0; i < Math.Max(descriptions.Length, 1); i++)
            {
                var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                var hasIdentity = EnumDisplayDevices(info.Device, (uint)i, ref display, 1);
                var model = descriptions.Length > 0 && !string.IsNullOrWhiteSpace(descriptions[i]) ? descriptions[i] : display.Description;
                if (!string.IsNullOrEmpty(display.Id) && friendlyNames.TryGetValue(display.Id, out var friendly)) model = friendly;
                var device = new MonitorDevice
                {
                    Id = hasIdentity && !string.IsNullOrWhiteSpace(display.Id) ? display.Id.ToUpperInvariant() : $"{info.Device}/{model}/{i}",
                    Model = string.IsNullOrWhiteSpace(model) ? "显示器" : model,
                    ModelId = ModelIdentity.FromDevicePath(display.Id ?? ""), Connection = info.Device,
                    Left = info.Monitor.Left, Top = info.Monitor.Top, Width = info.Monitor.Right - info.Monitor.Left,
                    Height = info.Monitor.Bottom - info.Monitor.Top, IsPrimary = (info.Flags & 1) != 0,
                    CapabilitiesPending = descriptions.Length > 0, DiscoveryWarning = warning
                };
                lock (devices) devices.Add(device);
                if (descriptions.Length > 0 && host is not null)
                {
                    var endpoint = new Endpoint(host, i);
                    lock (diagnosticHandles) diagnosticHandles.Add(device, endpoint);
                    device.Features.AddRange(VcpDiscovery.Discover(device.Id, VcpCapabilities.Parse(""),
                        code => ReadLive(endpoint, code), (code, value) => WriteLive(device, endpoint, code, value),
                        definitions: VcpCatalog.All.Where(d => d.Code is 0x10 or 0x12)));
                    device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
                }
                else device.Features.AddRange(VcpCatalog.All.Select(d => new MonitorFeature { Definition = d, Reason = Strings.T("DDC/CI 不可用", "DDC/CI unavailable") }));
                if (warning.Length > 0) lock (Errors) Errors.Add(device.Model + ": " + warning);
            }
        }, (info, error) => { lock (Errors) Errors.Add(info.Device + ": " + error.Message); });
        return devices.OrderBy(d => d.Left).ThenBy(d => d.Top).ThenBy(d => d.Id).ToList();
    }

    private static VcpReply? ReadLive(Endpoint endpoint, byte code)
    {
        var result = DdcReadPolicy.Read(() => endpoint.Call("read", code), code);
        return result.Error == 0 ? new(result.Current, result.Maximum) : null;
    }
    private void WriteLive(MonitorDevice device, Endpoint endpoint, byte code, uint value)
    {
        var watch = Stopwatch.StartNew();
        var result = endpoint.Call("write", code, value); // One request, no retry.
        diagnostic?.Invoke($"Monitor write [{device.DisplayName} / {device.Model} / {device.ModelId}]: VCP=0x{code:X2}, value=0x{value:X4}, error=0x{unchecked((uint)result.Error):X8}, duration={watch.ElapsedMilliseconds}ms");
        if (result.Error != 0) throw new Win32Exception(result.Error);
    }

    // Return an independent feature set; only the UI thread merges it into the
    // visible device. Failure retains the basic controls and never cached values.
    public MonitorDevice Enrich(MonitorDevice device, bool forceCapabilities)
    {
        var enriched = new MonitorDevice { Id = device.Id, Model = device.Model, ModelId = device.ModelId, Connection = device.Connection, Preference = device.Preference };
        try
        {
            if (!diagnosticHandles.TryGetValue(device, out var endpoint)) return enriched;
            var key = MonitorCapabilityCache.Identity(device.Id, device.Model, device.Connection);
            if (recovery.ShouldSkip(key, forceCapabilities))
            {
                enriched.DiscoveryWarning = Strings.T("上次高级检测中断，已保留基础控制；可用“重新检测”再次尝试。", "The previous advanced scan was interrupted. Basic controls remain; use Detect again to retry.");
                return enriched;
            }
            recovery.Begin(key);
            ReadControls(enriched, endpoint, forceCapabilities);
            if (endpoint.Client.IsStopped) throw new IOException(Strings.T("显示器通信已中止，请重新检测。", "Display communication stopped. Detect displays again."));
            recovery.Complete(key);
        }
        catch (Exception ex) { enriched.DiscoveryWarning = ex.Message; diagnostic?.Invoke($"Monitor enrichment failed [{device.Model}]: {ex}"); }
        finally { capabilityCache.Save(); }
        return enriched;
    }

    public static void MergeEnrichment(MonitorDevice device, MonitorDevice enriched)
    {
        device.CapabilitiesPending = false; device.DiscoveryWarning = enriched.DiscoveryWarning;
        device.CapabilitiesText = enriched.CapabilitiesText; device.FirmwareVersion = enriched.FirmwareVersion;
        device.InstalledAdapter = enriched.InstalledAdapter;
        // Preserve basic channel objects: a queued slider operation still refers to
        // exactly this connection. New advanced controls are added after discovery.
        var basic = device.Features.Where(f => f.Definition.Code is 0x10 or 0x12).ToDictionary(f => f.Definition.Code);
        foreach (var feature in enriched.Features)
        {
            if (basic.TryGetValue(feature.Definition.Code, out var original) && original.Channel is { } channel)
            {
                if (feature.Channel is { } latest) channel.Value = latest.Value;
                continue;
            }
            device.Features.RemoveAll(f => f.Definition.Key == feature.Definition.Key);
            device.Features.Add(feature);
        }
        device.Channels.Clear(); device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
        device.Channels.AddRange(enriched.Channels.Where(c => !device.Channels.Any(existing => existing.PropertyKey == c.PropertyKey)));
    }

    private void ReadControls(MonitorDevice device, Endpoint handle, bool forceCapabilities)
    {
        var scan = Stopwatch.StartNew();
        var key = MonitorCapabilityCache.Identity(device.Id, device.Model, device.Connection);
        var metadata = capabilityCache.GetOrRead(key, () =>
        {
            var response = handle.Call("capabilities");
            if (response.Error != 0) diagnostic?.Invoke($"Capabilities query [{device.Model}]: 0x{unchecked((uint)response.Error):X8}");
            return new MonitorCapabilityData(response.Error == 0 ? response.Text : "", ReadTemperatureFlags(handle));
        }, forceCapabilities, out var cacheHit);
        device.CapabilitiesText = metadata.VcpText;
        var capabilityMs = scan.ElapsedMilliseconds;
        var reads = 0; var failures = 0; var longestRead = 0L;
        VcpReply? Read(byte code)
        {
            var watch = Stopwatch.StartNew();
            var response = DdcReadPolicy.Read(() => handle.Call("read", code), code);
            var ok = response.Error == 0; var current = response.Current; var maximum = response.Maximum; var error = response.Error;
            var elapsed = watch.ElapsedMilliseconds;
            if (code is 0x14 or 0xDC || !ok) diagnostic?.Invoke($"Monitor read [{device.DisplayName} / {device.Model} / {device.ModelId}]: VCP=0x{code:X2}, current=0x{current:X4}, maximum=0x{maximum:X4}, success={ok}, error=0x{unchecked((uint)error):X8}, duration={elapsed}ms");
            reads++; if (!ok) failures++; longestRead = Math.Max(longestRead, elapsed);
            if (elapsed >= 250) diagnostic?.Invoke($"Slow monitor read [{device.Model}]: VCP=0x{code:X2}, duration={elapsed}ms, success={ok}");
            return ok ? new VcpReply(current, maximum) : null;
        }
        void Write(byte code, uint value) => WriteLive(device, handle, code, value);
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
                () => Require(handle.Call("temperature-read")).Current,
                value =>
                {
                    diagnostic?.Invoke($"Windows color temperature write [{device.Model}]: enum={value}");
                    Require(handle.Call("temperature-write", value: value));
                });
            if (temperature is not null) device.Channels.Add(temperature);
        }
        catch (Exception ex) { diagnostic?.Invoke($"Color temperature unavailable [{device.Model}]: {ex.Message}"); }

        diagnostic?.Invoke($"Monitor read [{device.Model}]: capabilities={(cacheHit ? "cache" : "hardware")}, capabilitiesTime={capabilityMs}ms, vcpReads={reads}, failures={failures}, longestRead={longestRead}ms, total={scan.ElapsedMilliseconds}ms");
    }

    private static DdcResponse Require(DdcResponse response) => response.Error == 0 ? response : throw new Win32Exception(response.Error);
    private static uint? ReadTemperatureFlags(Endpoint handle)
    {
        var response = handle.Call("temperature-flags");
        return response.Error == 0 ? response.Current : null;
    }

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
            // Explicit diagnostic session bypasses the cache and capability quarantine.
            var key = MonitorCapabilityCache.Identity(device.Id, device.Model, device.Connection);
            recovery.Begin(key);
            var response = handle.Call("capabilities");
            recovery.Complete(key);
            capabilities = new(response.Text, response.Error == 0 ? "hardware" : "unavailable", response.Error == 0 ? null : response.Error);
        }
        var snapshot = MonitorDiagnostics.Capture(device, capabilities, code =>
        {
            var response = handle.Call("read", code);
            return new(response.Type, response.Current, response.Maximum, response.Error == 0 ? null : response.Error);
        }, token, progress);
        diagnostic?.Invoke($"Monitor diagnostics: controls={snapshot.Readings.Count}, success={snapshot.Readings.Count(r => r.Status == "ok")}, total={watch.ElapsedMilliseconds}ms");
        return snapshot;
    }

    // Explicit pairing only. The caller holds the lifecycle gate through completion.
    internal DiagnosticReply ReadPairing(MonitorDevice device, byte code)
    {
        if (!diagnosticHandles.TryGetValue(device, out var handle))
            throw new InvalidOperationException(Strings.T("DDC/CI 不可用", "DDC/CI unavailable"));
        var timer = Stopwatch.StartNew();
        var response = handle.Call("read", code);
        var ok = response.Error == 0; var type = response.Type; var current = response.Current; var maximum = response.Maximum;
        int? error = ok ? null : response.Error;
        diagnostic?.Invoke($"OSD pairing read [{device.Model} / {device.ModelId}]: VCP=0x{code:X2}, current=0x{current:X4}, maximum=0x{maximum:X4}, success={ok}, error=0x{unchecked((uint)(error ?? 0)):X8}, duration={timer.ElapsedMilliseconds}ms");
        return new(type, current, maximum, error);
    }

    internal void WritePairing(MonitorDevice device, byte code, uint value)
    {
        if (value > 65535 || !(code == 0xCA && (value & 255) is >= 1 and <= 3 || code == 0x03 || OsdCandidateDiscovery.IsUnknown(code)))
            throw new InvalidDataException("Invalid pairing command.");
        if (!diagnosticHandles.TryGetValue(device, out var handle))
            throw new InvalidOperationException(Strings.T("DDC/CI 不可用", "DDC/CI unavailable"));
        var timer = Stopwatch.StartNew(); var response = handle.Call("write", code, value);
        var ok = response.Error == 0; var error = response.Error;
        diagnostic?.Invoke($"OSD pairing write [{device.Model} / {device.ModelId}]: VCP=0x{code:X2}, value=0x{value:X4}, success={ok}, error=0x{unchecked((uint)error):X8}, duration={timer.ElapsedMilliseconds}ms");
        if (!ok) throw new System.ComponentModel.Win32Exception(error);
    }

    public void Dispose()
    {
        diagnosticHandles.Clear();
        foreach (var host in hosts) host.Dispose();
        hosts.Clear();
    }
}
