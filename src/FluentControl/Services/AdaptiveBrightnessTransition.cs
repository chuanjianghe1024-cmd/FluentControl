namespace FluentControl.Services;

// A separate easing state for each monitor. A new target starts from its actual readback.
public sealed class AdaptiveBrightnessTransition
{
    private DateTimeOffset started;
    private double from, to;
    public void Reset() => started = default;
    public double? Next(double current, double target, DateTimeOffset now)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return null;
        current = Math.Clamp(current, 0, 100); target = Math.Clamp(target, 0, 100);
        if (started == default || now < started || now - started > TimeSpan.FromMinutes(5) || Math.Abs(target - to) >= .5)
        {
            started = now; from = current; to = target;
        }
        if (Math.Abs(current - target) < 1) return null;
        double t = Math.Clamp((now - started).TotalSeconds / 16d, 0, 1);
        double eased = t * t * (3 - 2 * t);
        double desired = t >= 1 ? target : from + (to - from) * eased;
        double step = Math.Clamp(desired - current, -12, 12);
        if (Math.Abs(step) < 1) return null;
        return Math.Clamp(Math.Round(current + step), 0, 100);
    }
}
