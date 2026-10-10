namespace FluentControl.Services;

public sealed record ControlOption(double Value, string Label);

public sealed class ControlChannel
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Glyph { get; init; }
    public IReadOnlyList<ControlOption>? Options { get; init; }
    public string PropertyKey { get; init; } = "volume";
    public string DeviceId { get; init; } = "";
    public bool IsAvailable { get; set; } = true;
    public byte? VcpCode { get; init; }
    public bool IsAction { get; init; }
    public bool CanSave { get; init; } = true;
    public bool CompatibilityOnly { get; init; }
    public bool VerifyChoiceReadback { get; init; }
    // OSD enable/disable can act like menu commands on some firmware. Keep the
    // command picker independent from readback so the same command is replayable.
    public bool IsCommandChoice => VcpCode == 0xCA && Options is not null;
    public bool IsRepeatableChoice => Options is not null && (IsCommandChoice || PropertyKey is "color-preset" or "display-mode" or "temperature");
    public bool ReadbackUnconfirmed { get; set; }
    public bool RequiresConfirmation { get; init; }
    public int ApplyOrder { get; init; } = 50;
    public Func<double, double>? LinkedToDevice { get; set; }
    public Func<double, double>? DeviceToLinked { get; set; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public string Unit { get; init; } = "%";
    public double Value { get; set; }
    public bool IsMuted { get; set; }
    public Func<double>? Read { get; init; }
    public Func<bool>? ReadMute { get; init; }
    public required Action<double> Write { get; init; }
    public Action<bool>? WriteMute { get; init; }
}

public static class ControlOperations
{
    // A failed monitor must not stop the remaining monitors from receiving a command.
    public static List<string> Apply(IEnumerable<ControlChannel> channels, double value, bool linked = false)
    {
        var errors = new List<string>();
        foreach (var channel in channels)
        {
            var sent = false;
            try
            {
                if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                var target = Math.Clamp(linked ? channel.LinkedToDevice?.Invoke(value) ?? value : value, channel.Minimum, channel.Maximum);
                if (channel.Options is not null && !channel.Options.Any(x => x.Value == target))
                    throw new ArgumentOutOfRangeException(nameof(value), "Unsupported option");
                channel.Write(target);
                sent = true;
                if (channel.Read is not null)
                {
                    channel.ReadbackUnconfirmed = true;
                    channel.Value = channel.Read();
                    channel.ReadbackUnconfirmed = false;
                }
                else { channel.Value = target; channel.ReadbackUnconfirmed = false; }
                if (channel.VerifyChoiceReadback && channel.Value != target)
                {
                    string Label(double value) => channel.Options?.FirstOrDefault(o => o.Value == value)?.Label is { } label ? $"{label} (0x{(uint)value:X2})" : $"0x{(uint)value:X2}";
                    errors.Add(channel.Name + "：" + Strings.F("请求 {0}，显示器实际返回 {1}。", "Requested {0}; the display returned {1}.", Label(target), Label(channel.Value)));
                }
            }
            catch (Exception ex)
            {
                errors.Add(channel.Name + "：" + (sent && channel.IsRepeatableChoice
                    ? Strings.F("指令已发送，显示器状态未确认：{0}", "Command sent; display state unconfirmed: {0}", ex.Message)
                    : ex.Message));
            }
        }
        return errors;
    }
    public static string ChoiceReadbackText(IEnumerable<ControlChannel> channels)
    {
        var targets = channels.ToArray();
        if (targets.Length == 0 || targets.Any(c => c.ReadbackUnconfirmed || !c.IsAvailable))
            return Strings.T("当前读值未确认", "Current readback unconfirmed");
        var first = targets[0];
        var value = targets.All(c => c.Value == first.Value)
            ? (first.Options?.FirstOrDefault(o => o.Value == first.Value)?.Label is { } label ? label + " · " : "") + $"0x{(uint)first.Value:X2}"
            : Strings.T("不同", "Mixed");
        return Strings.F("当前读值：{0}", "Current readback: {0}", value);
    }
    public static double DisplayValue(ControlChannel channel, bool linked) => linked ? channel.DeviceToLinked?.Invoke(channel.Value) ?? channel.Value : channel.Value;
    public static void SetMute(ControlChannel channel, bool muted)
    {
        if (channel.WriteMute is null) return;
        channel.WriteMute(muted);
        channel.IsMuted = channel.ReadMute?.Invoke() ?? muted;
    }
}
