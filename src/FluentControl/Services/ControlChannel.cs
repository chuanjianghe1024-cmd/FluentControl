namespace FluentControl.Services;

public sealed class ControlChannel
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Glyph { get; init; }
    public double Value { get; set; }
    public Func<double>? Read { get; init; }
    public required Action<double> Write { get; init; }
    public Func<bool>? ReadMute { get; init; }
    public Action<bool>? WriteMute { get; init; }
}
