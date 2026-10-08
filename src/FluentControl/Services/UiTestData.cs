namespace FluentControl.Services;

// Only used by --ui-test. These channels never change real hardware or settings.
internal static class UiTestData
{
    private static ControlChannel Channel(string name, string key, double value, bool isDefault = false, string detail = "") => new()
    {
        Name = name, DeviceId = name, PropertyKey = key, Value = value, Glyph = "\uE7F4", Detail = detail,
        IsDefaultAudio = isDefault, Write = _ => { }
    };
    internal static List<MonitorDevice> Monitors()
    {
        var result = new List<MonitorDevice>();
        for (var i = 0; i < 2; i++)
        {
            var device = new MonitorDevice { Id = "ui-test-monitor-" + i, Model = "测试显示器", ModelId = "TST0001", Connection = "test", Left = i * 1920, Width = 1920, Height = 1080, IsPrimary = i == 0 };
            var replies = new Dictionary<byte, VcpReply>
            {
                [0x10] = new((uint)(30 + i * 40),100), [0x12] = new(60,100), [0x62] = new(35,100),
                [0x14] = new(5,0), [0x16] = new(50,100), [0x18] = new(50,100), [0x1A] = new(50,100),
                [0x60] = new(15,0), [0x8D] = new(0x202,0x202), [0xD6] = new(1,0), [0xC9] = new(0x0102,0)
            };
            if (i == 1) replies.Remove(0x62); // only the first screen exposes speaker volume
            device.CapabilitiesText = "(prot(monitor) vcp(04 10 12 14(05 08) 16 18 1A 60(0F 11) 62 8D D6(01 04) C9 E1) mccs_ver(2.2))";
            if (i == 1) device.CapabilitiesText = device.CapabilitiesText.Replace("14(05 08)", "14(08 0B)");
            device.Features.AddRange(VcpDiscovery.Discover(device.Id, VcpCapabilities.Parse(device.CapabilitiesText),
                code => replies.TryGetValue(code, out var reply) ? reply : null,
                (code, value) => { if (replies.TryGetValue(code, out var reply)) replies[code] = reply with { Current = value }; }));
            device.Channels.AddRange(device.Features.Where(f => f.Channel is not null).Select(f => f.Channel!));
            result.Add(device);
        }
        return result;
    }
    internal static List<ControlChannel> Audio() => new()
    {
        Channel("默认扬声器", "volume", 50, true, "声音输出 · 默认设备 · 默认通话"),
        Channel("默认麦克风", "volume", 70, true, "麦克风输入 · 默认设备"),
        Channel("Voicemeeter 测试设备", "volume", 80)
    };
    internal static List<ControlChannel> Mouse() => new()
    {
        new() { Name = "鼠标速度", PropertyKey = "mouse-speed", Detail = "测试", Glyph = "\uE962", Minimum = 1, Maximum = 20, Unit = "", Value = 10, Write = _ => { } },
        new() { Name = "指针大小", PropertyKey = "pointer-size", Detail = "测试", Glyph = "\uE8B0", Minimum = 1, Maximum = 15, Unit = "", Value = 1, Write = _ => { } }
    };
}
