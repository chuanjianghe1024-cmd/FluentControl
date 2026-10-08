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
    public bool IsDefaultAudio { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public string Unit { get; init; } = "%";
    public double Value { get; set; }
    public bool IsMuted { get; set; }
    public Func<double>? Read { get; init; }
    public required Action<double> Write { get; init; }
    public Action<bool>? WriteMute { get; init; }
}

public static class ControlOperations
{
    // A failed monitor must not stop the remaining monitors from receiving a command.
    public static List<string> Apply(IEnumerable<ControlChannel> channels, double value)
    {
        var errors = new List<string>();
        foreach (var channel in channels)
        {
            try
            {
                var target = Math.Clamp(value, channel.Minimum, channel.Maximum);
                channel.Write(target);
                channel.Value = channel.Read?.Invoke() ?? target;
            }
            catch (Exception ex) { errors.Add(channel.Name + "：" + ex.Message); }
        }
        return errors;
    }
}
