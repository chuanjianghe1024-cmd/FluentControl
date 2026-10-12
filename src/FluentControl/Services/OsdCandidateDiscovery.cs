using System.Globalization;
using static FluentControl.Services.Strings;

namespace FluentControl.Services;

// These are capabilities, not verified actions. Generating a list never reads or writes VCPs.
public sealed class OsdAdvertisedFeature
{
    public byte Code { get; init; }
    public IReadOnlyList<byte> Values { get; init; } = Array.Empty<byte>();
    public DateTimeOffset CapturedAt { get; init; }
    public string Display => F("未知指令 0x{0}", "Unknown command 0x{0}", Code.ToString("X2"));
    public OsdCommand CreateCommand(uint value, string model)
    {
        if (!OsdCandidateDiscovery.IsUnknown(Code) || value > 65535 || Values.Count > 0 && !Values.Any(v => v == value))
            throw new InvalidDataException(T("该值不在显示器声明的候选中。", "This value is not among the monitor's advertised candidates."));
        return new()
        {
            Name = Display, Code = Code, Value = value, SourceModel = model,
            Discovery = Values.Count > 0 ? "capability-option" : "capability-experiment",
            DeclaredValues = Values.ToList(), AdvertisementCapturedAt = CapturedAt,
            Source = Values.Count > 0 ? T("代码和值来自本次显示器能力声明；用途待用户确认。", "Code and value come from this monitor's capabilities; the effect requires user confirmation.") :
                T("代码来自本次显示器能力声明；参数为用户选择的试验值，显示器未提供取值列表。", "The code comes from this monitor's capabilities. The user selected an experimental value because no value list was advertised.")
        };
    }
}

public static class OsdCandidateDiscovery
{
    // Standard feature identifiers, including features not implemented in the FC controls.
    // Classification reference: https://www.ddcutil.com/vcpinfo_output/ (MCCS 2.x / 3.0).
    // This set prevents resets, information, notifications and table operations from being
    // mislabeled as unknown navigation commands. Manufacturer-specific codes are not inferred.
    private static readonly HashSet<byte> StandardCodes = (
        "00 01 02 03 04 05 06 08 0A 0B 0C 0E 10 11 12 13 14 16 17 18 1A 1C 1E 1F " +
        "20 22 24 26 28 29 2A 2C 2E 30 32 34 36 38 39 3A 3C 3E 40 41 42 43 44 46 48 4A 4C " +
        "52 54 56 58 59 5A 5B 5C 5D 5E 60 62 63 64 66 6B 6C 6D 6E 6F 70 71 72 73 74 75 76 78 7A 7C 7E " +
        "80 82 84 86 87 88 8A 8B 8C 8D 8E 8F 90 91 92 93 94 95 96 97 98 99 9A 9B 9C 9D 9E 9F " +
        "A0 A2 A4 A5 AA AC AE B0 B2 B4 B6 B7 B8 B9 BA BB BC BD BE C0 C2 C3 C4 C6 C8 C9 CA CC CD CE CF D0 D2 D4 D6 D7 DA DB DC DE DF")
        .Split(' ').Select(s => byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToHashSet();
    public static bool IsUnknown(byte code) => !StandardCodes.Contains(code) && !VcpCatalog.All.Any(d => d.Code == code);
    public static List<OsdAdvertisedFeature> Discover(MonitorDiagnosticSnapshot? snapshot) => snapshot is not { CapabilitiesParsed: true, CapabilitiesErrorCode: null }
        ? new() : snapshot.Readings.Where(r => r.Advertised && IsUnknown(r.Code)).OrderBy(r => r.Code)
            .Select(r => new OsdAdvertisedFeature { Code = r.Code, Values = r.AdvertisedOptions.Distinct().Order().ToArray(), CapturedAt = snapshot.CapturedAt }).ToList();
    public static string KnownReason(byte code) => code switch
    {
        0x01 or 0x04 or 0x05 or 0x06 or 0x08 or 0x0A or 0x1E or 0x1F => T("重置或自动调整，不作为未知按键试发", "Reset or automatic adjustment; excluded from unknown-button tests"),
        0x02 or 0x03 or 0x52 => T("变化通知或按键事件，不是通用按键注入", "Change notification or button event, not universal key injection"),
        0x0B or 0x66 or 0xAA or 0xAC or 0xAE or 0xB2 or 0xB6 or 0xC0 or 0xC2 or 0xC6 or 0xC8 or 0xC9 or 0xDF => T("已知信息读取项", "Known information query"),
        _ => VcpCatalog.All.FirstOrDefault(d => d.Code == code)?.Name ?? T("已定义的标准功能，不作为未知按键试发", "Defined standard feature; excluded from unknown-button tests")
    };
    public static OsdCommand GetOrAdd(OsdPairingProfile profile, OsdAdvertisedFeature feature, uint value)
    {
        var candidate = feature.CreateCommand(value, profile.Identity.ModelId);
        var previous = profile.Commands.FirstOrDefault(c => c.Code == candidate.Code && c.Value == value && c.Discovery == candidate.Discovery && c.DeclaredValues.SequenceEqual(candidate.DeclaredValues));
        if (previous is not null) return previous;
        if (profile.Commands.Count >= OsdPairingProfile.MaxCommands) throw new InvalidDataException("Pairing candidates are full.");
        profile.Commands.Add(candidate); return candidate;
    }
}
