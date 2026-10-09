namespace FluentControl.Services;

internal static class MonitorColorTemperature
{
    // Windows MC_COLOR_TEMPERATURE and MCCS 0x14 are different namespaces.
    // Never use an index in the filtered options array as a native command.
    private static readonly (uint Windows, uint Vcp, int Kelvin)[] Presets =
    {
        (1, 3, 4000), (2, 4, 5000), (3, 5, 6500), (4, 6, 7500),
        (5, 7, 8200), (6, 8, 9300), (7, 9, 10000), (8, 10, 11500)
    };

    internal static ControlChannel? Create(string deviceId, ControlChannel? preset, uint flags,
        Func<uint> readWindows, Action<uint> writeWindows)
    {
        if (preset is not null)
        {
            var supported = Presets.Where(p => preset.Options?.Any(o => o.Value == p.Vcp) == true).ToArray();
            if (supported.Length == 0) return null;
            double Decode(double raw) => Presets.FirstOrDefault(p => p.Vcp == raw).Windows;
            return new ControlChannel
            {
                Name = "色温", Detail = "VCP 0x14", DeviceId = deviceId, PropertyKey = "temperature", Glyph = "\uE753",
                Minimum = 1, Maximum = 8, Unit = "", Value = Decode(preset.Value), ApplyOrder = 10,
                Options = supported.Select(p => new ControlOption(p.Windows, p.Kelvin + " K")).ToArray(),
                CompatibilityOnly = true, VerifyChoiceReadback = true,
                // Keep old scenes working without displaying/saving a second control.
                Read = () => { preset.Value = preset.Read?.Invoke() ?? preset.Value; return Decode(preset.Value); },
                Write = value =>
                {
                    var match = supported.FirstOrDefault(p => p.Windows == value);
                    if (match.Windows == 0) throw new ArgumentOutOfRangeException(nameof(value));
                    preset.Write(match.Vcp);
                }
            };
        }
        var options = Presets.Where(p => (flags & (1u << ((int)p.Windows - 1))) != 0)
            .Select(p => new ControlOption(p.Windows, p.Kelvin + " K")).ToArray();
        if (options.Length == 0) return null;
        var current = readWindows();
        if (!options.Any(o => o.Value == current)) return null;
        return new ControlChannel
        {
            Name = "色温", Detail = "Windows", DeviceId = deviceId, PropertyKey = "temperature", Glyph = "\uE753",
            Minimum = 1, Maximum = 8, Unit = "", Value = current, Options = options, ApplyOrder = 10,
            VerifyChoiceReadback = true, Read = () => readWindows(),
            Write = value =>
            {
                if (!options.Any(o => o.Value == value)) throw new ArgumentOutOfRangeException(nameof(value));
                writeWindows((uint)value);
            }
        };
    }

    internal static bool IsSuperseded(ControlChannel channel, IReadOnlyDictionary<string, SavedValue> values,
        IReadOnlyDictionary<string, ControlChannel> available)
    {
        if (!channel.CompatibilityOnly) return false;
        var key = ProfileGroups.MonitorKey(channel.DeviceId, "color-preset");
        return values.TryGetValue(key, out var saved) && available.TryGetValue(key, out var preset) && ProfileExchange.CanApply(preset, saved.Value);
    }
}
