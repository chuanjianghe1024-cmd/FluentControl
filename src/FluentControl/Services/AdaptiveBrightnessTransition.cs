namespace FluentControl.Services;

public sealed class BrightnessTransitionSettings
{
    public int DurationMs { get; set; } = 2000;
    public int IntervalMs { get; set; } = 100;
    public int Steps { get; set; } // zero selects interval scheduling
    public string Curve { get; set; } = "smooth";
    public double X1 { get; set; } = 1d / 3;
    public double Y1 { get; set; }
    public double X2 { get; set; } = 2d / 3;
    public double Y2 { get; set; } = 1;
    public void Normalize()
    {
        DurationMs = Math.Clamp(DurationMs, 1000, 3000);
        IntervalMs = Math.Clamp(IntervalMs, 100, 500);
        Steps = Math.Clamp(Steps, 0, DurationMs / IntervalMs);
        if (Curve is not ("linear" or "smooth" or "custom")) Curve = "smooth";
        X1 = double.IsFinite(X1) ? Math.Clamp(X1, 0, 1) : 1d / 3;
        X2 = double.IsFinite(X2) ? Math.Clamp(X2, X1, 1) : Math.Max(X1, 2d / 3);
        Y1 = double.IsFinite(Y1) ? Math.Clamp(Y1, 0, 1) : 0;
        Y2 = double.IsFinite(Y2) ? Math.Clamp(Y2, Y1, 1) : 1;
    }
    public double TickMs => Steps > 0 ? (double)DurationMs / Steps : IntervalMs;
    public double Evaluate(double x)
    {
        x = Math.Clamp(x, 0, 1);
        if (x is 0 or 1 || Curve == "linear") return x;
        if (Curve != "custom") return x * x * (3 - 2 * x);
        static double Bezier(double t, double a, double b) => 3 * (1-t) * (1-t) * t * a + 3 * (1-t) * t * t * b + t * t * t;
        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++)
        { double t = (lo + hi) / 2; if (Bezier(t, X1, X2) < x) lo = t; else hi = t; }
        return Bezier((lo + hi) / 2, Y1, Y2);
    }
}

// Caller supplies elapsed monotonic time and last successful write/readback.
// Next does not advance hardware state: failures must stop the caller's transition.
public sealed class AdaptiveBrightnessTransition
{
    private DateTimeOffset started, lastOpportunity;
    private double from, to;
    public bool Complete { get; private set; }
    public void Reset() { started = default; lastOpportunity = default; Complete = false; }
    public double? Next(double current, double target, DateTimeOffset now, BrightnessTransitionSettings? settings = null)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return null;
        settings ??= new(); settings.Normalize();
        current = Math.Clamp(current, 0, 100); target = Math.Round(Math.Clamp(target, 0, 100));
        if (started == default || now < started || target != to)
        { started = now; lastOpportunity = now; from = current; to = target; Complete = false; return null; }
        double elapsed = (now - started).TotalMilliseconds;
        Complete = elapsed >= settings.DurationMs;
        if ((now - lastOpportunity).TotalMilliseconds + .001 < settings.TickMs && !Complete) return null;
        // No backlog: delayed ticks evaluate the current time, not missed steps.
        lastOpportunity = now;
        var next = Math.Round(from + (to - from) * settings.Evaluate(elapsed / settings.DurationMs));
        return Math.Abs(next - current) < .5 ? null : next;
    }
}
