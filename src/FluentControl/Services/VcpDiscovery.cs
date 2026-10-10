using static FluentControl.Services.Strings;
namespace FluentControl.Services;

public readonly record struct VcpReply(uint Current, uint Maximum);
public static class VcpDiscovery
{
    // Discovery only reads. Write-only reset becomes available only if advertised.
    public static List<MonitorFeature> Discover(string deviceId, VcpCapabilities capabilities, Func<byte, VcpReply?> read, Action<byte, uint> write, MonitorAdapter? adapter = null)
    {
        adapter?.Validate();
        var features = new List<MonitorFeature>();
        foreach (var definition in VcpCatalog.All)
        {
            var code = definition.Code;
            var adaptedOptions = adapter?.Controls.FirstOrDefault(c => c.Key == definition.Key)?.Options;
            bool advertised = capabilities.Features.TryGetValue(code, out var values);
            values ??= Array.Empty<byte>();
            var missing = T("未报告支持", "Not advertised");
            if (capabilities.HasVcpSection && !advertised && adaptedOptions is null && code is not 0x10 and not 0x12 and not 0x62 and not 0xCA)
            { features.Add(new() { Definition = definition, Reason = missing }); continue; }
            if (definition.Kind == VcpKind.Action)
            {
                features.Add(new() { Definition = definition, Reason = advertised ? "" : missing, Channel = !advertised ? null : new()
                {
                    Name = definition.Name, Detail = "VCP 0x04", Glyph = "\uE777", PropertyKey = definition.Key, DeviceId = deviceId,
                    VcpCode = code, IsAction = true, CanSave = false, RequiresConfirmation = true, Minimum = 1, Maximum = 1, Value = 1,
                    Write = value => { if (value != 1) throw new ArgumentOutOfRangeException(nameof(value)); write(code, 1); }
                }});
                continue;
            }
            VcpReply? reply;
            try { reply = read(code); } catch { reply = null; }
            if (reply is not VcpReply data)
            {
                features.Add(new() { Definition = definition, Reason = code == 0x62 ?
                    T("DDC/CI 音量不可用。可在“音量与输入”中调节系统音量。", "DDC/CI volume unavailable. Use Volume & input to adjust system volume.") :
                    T("不支持或当前无法读取", "Unsupported or currently unreadable") });
                continue;
            }
            // Some displays omit CA while returning its state. Reading that state
            // must not grant permission to write an unadvertised menu/keyboard lock.
            if (code == 0xCA && !advertised)
            {
                var state = (data.Current & 255) switch { 1 => T("关闭", "Off"), 2 => T("开启", "On"), var value => $"0x{value:X2}" };
                features.Add(new()
                {
                    Definition = definition,
                    Information = F("OSD 状态：{0}", "OSD state: {0}", state) + $" · 0x{data.Current:X4}",
                    Reason = T("只读状态；未验证菜单开关写入。", "Read-only state; OSD switching has not been verified.")
                });
                continue;
            }
            if (definition.Kind == VcpKind.ReadOnly)
            {
                var information = code == 0xC0 ? (((ulong)data.Maximum << 16) | data.Current).ToString() + " h" : $"{data.Current >> 8}.{data.Current & 255}";
                features.Add(new() { Definition = definition, Information = information }); continue;
            }
            IReadOnlyList<ControlOption>? options = null;
            double initial = data.Current, maximum = 65535;
            var continuous = definition.Kind == VcpKind.Continuous;
            bool lowByte = code is 0x14 or 0x8D or 0xCA;
            if (continuous)
            {
                if (data.Maximum is 0 or > 65535 || data.Current > data.Maximum)
                { features.Add(new() { Definition = definition, Reason = T("返回值范围无效", "Invalid reported range") }); continue; }
                maximum = 100; initial = 100d * data.Current / data.Maximum;
            }
            else
            {
                if (lowByte) initial = data.Current & 255;
                if (adaptedOptions is not null) options = adaptedOptions.Select(o => new ControlOption(o.Value, o.Label)).ToArray();
                else if (definition.Kind == VcpKind.Gamma) options = VcpCatalog.GammaOptions(values);
                else
                {
                    IEnumerable<byte> known = values;
                    if (code == 0x8D) known = (data.Maximum & 255) == 2 || initial is 1 or 2 ? new byte[] { 1, 2 } : Array.Empty<byte>();
                    if (code == 0xCA) known = initial is >= 1 and <= 3 ? (values.Length > 0 ? values.Where(x => x is >= 1 and <= 3) : new byte[] { 1, 2, 3 }) : Array.Empty<byte>();
                    // Values for enums are taken from capabilities; never guess HDMI ports or power states.
                    options = known.Distinct().Select(x => new ControlOption(x, VcpCatalog.OptionLabel(code, x))).ToArray();
                }
                if (options.Count == 0)
                { features.Add(new() { Definition = definition, Information = $"0x{data.Current:X4}", Reason = T("可读取，缺少可安全设置的选项", "Readable; writable options unavailable") }); continue; }
            }
            var verifyChoice = code is 0x14 or 0xDC;
            uint? pendingChoice = null;
            double ReadValue()
            {
                var expected = pendingChoice;
                pendingChoice = null;
                var now = read(code) ?? throw new IOException(T("无法回读显示器", "Cannot read back display"));
                // Color and picture presets can settle after the write acknowledgement.
                // Retry reads only; never resend or manufacture the selected value.
                if (expected is uint target)
                {
                    for (var attempt = 0; attempt < 3 && (lowByte ? now.Current & 255 : now.Current) != target; attempt++)
                    {
                        Thread.Sleep(80 << attempt);
                        now = read(code) ?? throw new IOException(T("无法回读显示器", "Cannot read back display"));
                    }
                }
                return continuous ? 100d * now.Current / data.Maximum : lowByte ? now.Current & 255 : now.Current;
            }
            var channel = new ControlChannel
            {
                Name = definition.Name, Detail = $"VCP 0x{code:X2}", DeviceId = deviceId, PropertyKey = definition.Key, Glyph = "\uE7F4",
                VcpCode = code, Value = initial, Minimum = 0, Maximum = maximum, Unit = continuous ? "%" : "", Options = options,
                RequiresConfirmation = definition.Confirm, CanSave = code is not 0xD6 and not 0xCA, ApplyOrder = definition.Order,
                VerifyChoiceReadback = verifyChoice,
                Read = code is 0x60 or 0xD6 ? null : ReadValue,
                Write = value =>
                {
                    if (!double.IsFinite(value) || value < 0 || value > maximum || (options is not null && !options.Any(x => x.Value == value)))
                        throw new ArgumentOutOfRangeException(nameof(value));
                    uint native = continuous ? (uint)Math.Round(value * data.Maximum / 100) : (uint)value;
                    if (code is 0x8D or 0xCA)
                    {
                        var before = read(code) ?? throw new IOException(T("无法回读显示器", "Cannot read back display"));
                        native = (before.Current & 0xFF00) | (native & 255); // preserve screen blank / power-button fields
                    }
                    write(code, native);
                    if (verifyChoice) pendingChoice = native;
                }
            };
            features.Add(new() { Definition = definition, Channel = channel });
        }
        foreach (var code in capabilities.Features.Keys.Where(x => !VcpCatalog.All.Any(d => d.Code == x)).Order())
            features.Add(new() { Definition = new(code, "vcp-" + code.ToString("X2"), "扩展功能", "Extended feature", "extensions", VcpKind.ReadOnly), Reason = T("需要型号适配或厂商 SDK", "Requires a model adapter or vendor SDK") });
        return features;
    }
}
