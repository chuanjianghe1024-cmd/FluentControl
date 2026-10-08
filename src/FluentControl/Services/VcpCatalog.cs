using System.Globalization;
using System.Text.RegularExpressions;
using static FluentControl.Services.Strings;
namespace FluentControl.Services;

public enum VcpKind { Continuous, Choice, Gamma, ReadOnly, Action }
public sealed record VcpDefinition(byte Code, string Key, string Zh, string En, string Category, VcpKind Kind = VcpKind.Continuous, bool Confirm = false, int Order = 50)
{
    public string Name => T(Zh, En);
}
public sealed class MonitorFeature
{
    public required VcpDefinition Definition { get; init; }
    public ControlChannel? Channel { get; init; }
    public string Information { get; init; } = "";
    public string Reason { get; init; } = "";
}
public sealed class VcpCapabilities
{
    public Dictionary<byte, byte[]> Features { get; } = new();
    public bool HasVcpSection { get; private set; }
    public string Version { get; private set; } = "";
    public static VcpCapabilities Parse(string text)
    {
        var result = new VcpCapabilities();
        if (text.Length > 65536) return result;
        result.Version = Regex.Match(text, @"mccs_ver\s*\(([^)]*)\)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var start = Regex.Match(text, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
        if (!start.Success) return result;
        var position = start.Index + start.Length;
        while (position < text.Length)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length) return new();
            if (text[position] == ')') { result.HasVcpSection = true; return result; }
            var token = Regex.Match(text[position..], @"\A([0-9a-fA-F]{2})(?=\s|\(|\))");
            if (!token.Success) return new();
            var code = byte.Parse(token.Value, NumberStyles.HexNumber);
            position += token.Length;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            var values = new List<byte>();
            if (position < text.Length && text[position] == '(')
            {
                var end = text.IndexOf(')', ++position);
                if (end < 0) return new();
                foreach (var value in text[position..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (value.Length != 2 || !byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) return new();
                    values.Add(number);
                }
                position = end + 1;
            }
            result.Features[code] = values.ToArray();
        }
        return new();
    }
}

public static class VcpCatalog
{
    public static IReadOnlyList<VcpDefinition> All { get; } = new VcpDefinition[]
    {
        new(0x10,"brightness","亮度","Brightness","picture"), new(0x12,"contrast","对比度","Contrast","picture"),
        new(0x14,"color-preset","色温预设","Color preset","color",VcpKind.Choice,Order:10),
        new(0x16,"gain-red","红色增益","Red gain","color"), new(0x18,"gain-green","绿色增益","Green gain","color"), new(0x1A,"gain-blue","蓝色增益","Blue gain","color"),
        new(0x6C,"black-red","红色黑电平","Red black level","color"), new(0x6E,"black-green","绿色黑电平","Green black level","color"), new(0x70,"black-blue","蓝色黑电平","Blue black level","color"),
        new(0x72,"gamma","Gamma","Gamma","color",VcpKind.Gamma), new(0x8A,"saturation","饱和度","Saturation","color"), new(0x90,"hue","色相","Hue","color"),
        new(0x59,"saturation-red","红色饱和度","Red saturation","color"), new(0x5A,"saturation-yellow","黄色饱和度","Yellow saturation","color"),
        new(0x5B,"saturation-green","绿色饱和度","Green saturation","color"), new(0x5C,"saturation-cyan","青色饱和度","Cyan saturation","color"),
        new(0x5D,"saturation-blue","蓝色饱和度","Blue saturation","color"), new(0x5E,"saturation-magenta","品红饱和度","Magenta saturation","color"),
        new(0x60,"input","输入源","Input source","input",VcpKind.Choice,true,100),
        new(0x62,"speaker","屏幕音量","Monitor volume","audio"), new(0x8D,"monitor-mute","屏幕静音","Monitor mute","audio",VcpKind.Choice),
        new(0x87,"sharpness","锐度","Sharpness","processing"), new(0x86,"scaling","画面缩放","Display scaling","processing",VcpKind.Choice),
        new(0xDC,"display-mode","显示器场景模式","Display preset","processing",VcpKind.Choice,Order:0),
        new(0xCA,"osd","OSD / 按键控制","OSD / button control","osd",VcpKind.Choice,true), new(0xCC,"osd-language","OSD 语言","OSD language","osd",VcpKind.Choice),
        new(0xD6,"power","电源模式","Power mode","power",VcpKind.Choice,true,110), new(0x04,"factory-reset","恢复出厂设置","Factory reset","power",VcpKind.Action,true),
        new(0xC0,"usage","使用时长","Usage time","information",VcpKind.ReadOnly), new(0xC9,"firmware","固件版本","Firmware version","information",VcpKind.ReadOnly),
        new(0xDF,"mccs","MCCS 版本","MCCS version","information",VcpKind.ReadOnly)
    };
    public static VcpDefinition? Find(string key) => All.FirstOrDefault(x => x.Key == key);
    public static string Category(string key) => key switch
    {
        "picture" => T("基础画面","Picture"), "color" => T("色彩","Color"), "input" => T("输入源","Input source"),
        "audio" => T("显示器音频","Monitor audio"), "processing" => T("图像处理","Image processing"), "osd" => T("显示器菜单","Monitor menu"),
        "power" => T("电源与配置","Power and settings"), "information" => T("信息读取","Information"), _ => T("型号扩展","Model extensions")
    };
    public static string OptionLabel(byte code, int value) => code switch
    {
        0x60 => value switch { 1 => "VGA 1", 2 => "VGA 2", 3 => "DVI 1", 4 => "DVI 2", 15 => "DisplayPort 1", 16 => "DisplayPort 2", 17 => "HDMI 1", 18 => "HDMI 2", _ => $"0x{value:X2}" },
        0x14 => value switch { 1 => "sRGB", 2 => T("显示器原生","Native"), 3 => "4000 K", 4 => "5000 K", 5 => "6500 K", 6 => "7500 K", 7 => "8200 K", 8 => "9300 K", 9 => "10000 K", 10 => "11500 K", _ => $"0x{value:X2}" },
        0x8D => value == 1 ? T("静音","Mute") : T("取消静音","Unmute"),
        0x86 => value switch { 1 => T("不缩放","No scaling"), 2 => T("拉伸至全屏","Stretch to full screen"), 8 => T("保持宽高比","Keep aspect ratio"), _ => $"0x{value:X2}" },
        0xD6 => value switch { 1 => T("开启","On"), 2 => T("待机","Standby"), 3 => T("休眠","Suspend"), 4 or 5 => T("关闭","Off"), _ => $"0x{value:X2}" },
        0xCA => value switch { 1 => T("禁用 OSD，保留按键事件","Disable OSD; keep button events"), 2 => T("启用 OSD","Enable OSD"), 3 => T("禁用 OSD 与按键事件","Disable OSD and button events"), _ => $"0x{value:X2}" },
        0xCC => value switch { 1 => "繁體中文", 2 => "English", 3 => "Français", 4 => "Deutsch", 5 => "Italiano", 6 => "日本語", 7 => "한국어", 8 => "Português", 9 => "Русский", 10 => "Español", 13 => "简体中文", _ => $"0x{value:X2}" },
        0xDC => value switch { 0 => T("标准","Standard"), 1 => T("办公","Office"), 2 => T("混合","Mixed"), 3 => T("电影","Movie"), 4 => T("用户自定义","User defined"), 5 => T("游戏","Game"), 6 => T("运动","Sports"), 7 => T("专业","Professional"), 8 => T("标准（中等功耗）","Standard (medium power)"), 9 => T("标准（低功耗）","Standard (low power)"), 10 => T("演示","Demo"), 0xF0 => T("动态对比度","Dynamic contrast"), _ => $"0x{value:X2}" },
        _ => $"0x{value:X2}"
    };
    public static ControlOption[] GammaOptions(byte[] values)
    {
        // MCCS 2.2: the capability prefix is metadata, not a list of writable bytes.
        if (values.Length < 3 || values[2] < 0xFA) return Array.Empty<ControlOption>();
        bool relative = values[0] == 0xFF;
        IEnumerable<int> choices = values[2] switch
        {
            0xFF or 0xFE => Enumerable.Range(0, relative ? 27 : 255),
            0xFD or 0xFC when values.Length >= 5 && values[3] <= values[4] => Enumerable.Range(values[3], values[4] - values[3] + 1),
            0xFB or 0xFA => values.Skip(3).Select(x => (int)x),
            _ => Array.Empty<int>()
        };
        return choices.Where(x => !relative || x <= 10 || x is >= 17 and <= 26).Distinct().Select(x => new ControlOption(
            (x << 8) | (relative ? 4 : 0), relative ? (x == 0 ? T("默认","Default") : (x <= 10 ? "−" : "+") + (x <= 10 ? x / 10d : (x - 16) / 10d).ToString("0.0", CultureInfo.InvariantCulture)) : (1 + x / 100d).ToString("0.00", CultureInfo.InvariantCulture))).ToArray();
    }
}
