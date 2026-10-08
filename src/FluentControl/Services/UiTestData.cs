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
            var device = new MonitorDevice { Id = "ui-test-monitor-" + i, Model = "测试显示器", Connection = "test", Left = i * 1920, Width = 1920, Height = 1080, IsPrimary = i == 0 };
            device.Channels.Add(Channel("亮度", "brightness", 30 + i * 40));
            device.Channels.Add(Channel("对比度", "contrast", 60));
            device.Channels.Add(new ControlChannel { Name = "色温", PropertyKey = "temperature", Value = 3, Minimum = 1, Maximum = 8, Unit = "", Detail = "", Glyph = "\uE753", Options = new[] { new ControlOption(1, "4000 K"), new ControlOption(3, "6500 K") }, Write = _ => { } });
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
