using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace FluentControl.Services;

internal sealed record LanguageOption(string Code, string Name);
internal static class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Catalog = Load();
    internal static string CurrentLanguage { get; private set; } = "zh-CN";
    internal static bool English => CurrentLanguage == "en-US";
    internal static string AppName => T("聚合控制", "Fluent Control");
    internal static IReadOnlyList<string> SupportedLanguages => new[] { "zh-CN", "zh-TW", "en-US", "ja-JP", "ko-KR", "de-DE", "fr-FR", "es-ES" };
    internal static LanguageOption[] LanguageOptions() => new[]
    {
        new LanguageOption("auto", T("跟随系统", "System default")), new("zh-CN", "简体中文"), new("zh-TW", "繁體中文"),
        new("en-US", "English"), new("ja-JP", "日本語"), new("ko-KR", "한국어"), new("de-DE", "Deutsch"), new("fr-FR", "Français"), new("es-ES", "Español")
    };
    internal static string ResolveLanguage(string requested)
    {
        var culture = requested == "auto" ? CultureInfo.CurrentUICulture.Name : requested;
        if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return culture.Contains("TW", StringComparison.OrdinalIgnoreCase) || culture.Contains("HK", StringComparison.OrdinalIgnoreCase) || culture.Contains("MO", StringComparison.OrdinalIgnoreCase) || culture.Contains("Hant", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        return SupportedLanguages.FirstOrDefault(x => x.StartsWith(culture.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase)) ?? "en-US";
    }
    internal static void SetLanguage(string language) => CurrentLanguage = ResolveLanguage(language);
    internal static string T(string zh, string en) => CurrentLanguage == "zh-CN" ? zh : Translate(en, CurrentLanguage);
    internal static string F(string zh, string en, params object[] args) => string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), T(zh, en), args);
    internal static string Translate(string key, string language) => language != "en-US" && Catalog.TryGetValue(language, out var entries) && entries.TryGetValue(key, out var result) ? result : key;
    internal static void ValidateCatalog()
    {
        var reference = Catalog["zh-TW"];
        foreach (var language in SupportedLanguages.Where(x => x is not "en-US" and not "zh-CN"))
        {
            if (!Catalog.TryGetValue(language, out var entries) || !reference.Keys.Order().SequenceEqual(entries.Keys.Order())) throw new InvalidDataException("Incomplete locale: " + language);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Value)) throw new InvalidDataException("Empty translation: " + entry.Key);
                var keySlots = System.Text.RegularExpressions.Regex.Matches(entry.Key, @"\{\d+\}").Select(x => x.Value).Order();
                var translatedSlots = System.Text.RegularExpressions.Regex.Matches(entry.Value, @"\{\d+\}").Select(x => x.Value).Order();
                if (!keySlots.SequenceEqual(translatedSlots)) throw new InvalidDataException("Invalid translation placeholders: " + language + " / " + entry.Key);
            }
        }
    }
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FluentControl.Locales.json") ?? throw new FileNotFoundException("Language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? throw new InvalidDataException("Invalid language resources.");
    }
    internal static string Channel(ControlChannel c) => c.PropertyKey switch
    {
        "brightness" => T("亮度", "Brightness"), "contrast" => T("对比度", "Contrast"), "speaker" => T("屏幕音量", "Monitor volume"),
        "temperature" => T("色温", "Color temperature"), "mouse-speed" => T("鼠标速度", "Mouse speed"), "pointer-size" => T("指针大小", "Pointer size"), _ => VcpCatalog.Find(c.PropertyKey)?.Name ?? c.Name
    };
}
