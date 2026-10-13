using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization.Json;
namespace FluentControl.Setup;

internal static class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Catalog = Load();
    internal static readonly string[] Languages = ["zh-CN", "zh-TW", "en-US", "ja-JP", "ko-KR", "de-DE", "fr-FR", "es-ES"];
    internal static readonly string[] Names = ["简体中文", "繁體中文", "English", "日本語", "한국어", "Deutsch", "Français", "Español"];
    internal static string Language { get; private set; } = "en-US";
    internal static void SetLanguage(string language)
    {
        if (language == "auto") language = CultureInfo.CurrentUICulture.Name;
        Language = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? (language.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 || language.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0 || language.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ? "zh-TW" : "zh-CN")
            : Languages.FirstOrDefault(x => x.StartsWith(language.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase)) ?? "en-US";
    }
    internal static string T(string key) => Catalog[Language][key];
    internal static string F(string key, params object[] args) => string.Format(CultureInfo.GetCultureInfo(Language), T(key), args);
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FluentControl.Setup.Locales.json")!;
        var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, Dictionary<string, string>>),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        return (Dictionary<string, Dictionary<string, string>>)serializer.ReadObject(stream)!;
    }
}
