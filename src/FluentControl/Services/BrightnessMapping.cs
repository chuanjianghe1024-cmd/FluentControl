namespace FluentControl.Services;

// Maps a shared 0–100 slider to each display's own DDC brightness percentage.
public sealed class BrightnessMapping
{
    public bool Enabled { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Offset { get; set; }
    public double Curve { get; set; } = 1;
    public void Validate()
    {
        if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || !double.IsFinite(Offset) || !double.IsFinite(Curve) ||
            Minimum < 0 || Maximum > 100 || Minimum >= Maximum || Offset < -50 || Offset > 50 || Curve < .2 || Curve > 5)
            throw new ArgumentException(Strings.T("亮度映射参数无效。", "Invalid brightness mapping."));
    }
    public double ToDevice(double logical)
    {
        if (!Enabled) return Math.Clamp(logical, 0, 100);
        Validate();
        return Math.Clamp(Minimum + (Maximum - Minimum) * Math.Pow(Math.Clamp(logical, 0, 100) / 100, Curve) + Offset, 0, 100);
    }
    public double ToLinked(double value)
    {
        if (!Enabled) return value;
        Validate();
        return 100 * Math.Pow(Math.Clamp((value - Offset - Minimum) / (Maximum - Minimum), 0, 1), 1 / Curve);
    }
    public BrightnessMapping Copy() => new() { Enabled = Enabled, Minimum = Minimum, Maximum = Maximum, Offset = Offset, Curve = Curve };
}
