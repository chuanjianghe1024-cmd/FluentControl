using System.Globalization;
namespace FluentControl.Services;

internal static class Strings
{
    internal static bool English { get; private set; }
    internal static void SetLanguage(string language) => English = language == "en-US" || (language == "auto" && !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
    internal static string T(string zh, string en) => English ? en : zh;
    internal static string Channel(ControlChannel c) => c.PropertyKey switch
    {
        "brightness" => T("亮度", "Brightness"), "contrast" => T("对比度", "Contrast"), "speaker" => T("屏幕音量", "Monitor volume"),
        "temperature" => T("色温", "Color temperature"), "mouse-speed" => T("鼠标速度", "Mouse speed"), "pointer-size" => T("指针大小", "Pointer size"), _ => c.Name
    };
}
