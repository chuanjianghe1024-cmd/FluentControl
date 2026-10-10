namespace FluentControl.Services;

// Only used by --ui-test. These channels never change real hardware or settings.
internal static class UiTestData
{
    private static readonly Dictionary<MonitorDevice, Func<byte, DiagnosticReply>> DiagnosticReaders = new();
    internal static MonitorDiagnosticSnapshot CaptureDiagnostics(MonitorDevice device, MonitorDiagnosticSnapshot? baseline,
        CancellationToken token, IProgress<DiagnosticProgress>? progress) => MonitorDiagnostics.Capture(device,
            new(device.CapabilitiesText, baseline is null ? "simulated" : "baseline"), DiagnosticReaders[device], token, progress);
    internal static List<MonitorDevice> Monitors()
    {
        var result = new List<MonitorDevice>();
        for (var i = 0; i < 2; i++)
        {
            var device = new MonitorDevice { Id = "ui-test-monitor-" + i, Model = "测试显示器 " + (i + 1), ModelId = i == 0 ? "TST0001" : "TST0002", Connection = "test", Left = i * 1920, Width = 1920, Height = 1080, IsPrimary = i == 0 };
            var scale = (uint)(i + 1); // Different models use different native VCP maxima.
            var replies = new Dictionary<byte, VcpReply>
            {
                [0x10] = new((uint)(30 + i * 40) * scale,100 * scale), [0x12] = new(60 * scale,100 * scale), [0x62] = new(35,100),
                [0x14] = new(5,0), [0x16] = new(50,100), [0x18] = new(50,100), [0x1A] = new(50,100),
                [0x60] = new(15,0), [0x8D] = new(0x202,0x202), [0xD6] = new(1,0), [0xDC] = new(0,0), [0xC9] = new(0x0102,0)
            };
            if (i == 1) replies.Remove(0x62); // only the first screen exposes speaker volume
            DiagnosticReaders[device] = code => replies.TryGetValue(code, out var reply) ? new(0, reply.Current, reply.Maximum) : new(0, 0, 0, 50);
            device.CapabilitiesText = "(prot(monitor) vcp(04 10 12 14(05 08) 16 18 1A 60(0F 11) 62 8D D6(01 04) DC(00 05) C9 E1) mccs_ver(2.2))";
            if (i == 1) device.CapabilitiesText = device.CapabilitiesText.Replace("14(05 08)", "14(08 0B)");
            device.Features.AddRange(VcpDiscovery.Discover(device.Id, VcpCapabilities.Parse(device.CapabilitiesText),
                code => replies.TryGetValue(code, out var reply) ? reply : null,
                (code, value) => { if (replies.TryGetValue(code, out var reply)) replies[code] = reply with { Current = value }; }));
            device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
            var temperature = MonitorColorTemperature.Create(device.Id, device.Channels.Single(c => c.PropertyKey == "color-preset"), 0,
                () => throw new InvalidOperationException("UI fixture must use raw VCP"), _ => throw new InvalidOperationException("UI fixture must use raw VCP"));
            if (temperature is not null) device.Channels.Add(temperature);
            result.Add(device);
        }
        return result;
    }
    internal sealed class AudioBackend : ISystemAudioBackend
    {
        private readonly Dictionary<SystemAudioTarget, SystemAudioValue> values = new()
        { [SystemAudioTarget.Output] = new(50, false), [SystemAudioTarget.Input] = new(70, false) };
        internal bool InputAvailable { get; set; } = true;
        public event Action? Changed;
        public SystemAudioValue Read(SystemAudioTarget target)
        {
            if (target == SystemAudioTarget.Input && !InputAvailable) throw new IOException("No input");
            return values[target];
        }
        public SystemAudioValue SetVolume(SystemAudioTarget target, double volume)
        { values[target] = Read(target) with { Volume = volume }; Changed?.Invoke(); return values[target]; }
        public SystemAudioValue SetMute(SystemAudioTarget target, bool muted)
        { values[target] = Read(target) with { Muted = muted }; Changed?.Invoke(); return values[target]; }
        internal void ChangeDefault(SystemAudioTarget target, double volume, bool muted)
        { values[target] = new(volume, muted); Changed?.Invoke(); }
        public void Dispose() => Changed = null;
    }
    internal static List<ControlChannel> Mouse() => new()
    {
        new() { Name = "鼠标速度", PropertyKey = "mouse-speed", Detail = "测试", Glyph = "\uE962", Minimum = 1, Maximum = 20, Unit = "", Value = 10, Write = _ => { } },
        new() { Name = "指针大小", PropertyKey = "pointer-size", Detail = "测试", Glyph = "\uE8B0", Minimum = 1, Maximum = 15, Unit = "", Value = 1, Write = _ => { } }
    };
}
