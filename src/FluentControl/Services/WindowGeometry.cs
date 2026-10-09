namespace FluentControl.Services;

internal readonly record struct WindowArea(int X, int Y, int Width, int Height);

internal static class WindowGeometry
{
    // XAML sizes are DIPs; native window sizes, frames and work areas are pixels.
    internal static (int Width, int Height) ClientSize(double width, double height, double scale,
        WindowArea area, int frameWidth, int frameHeight)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var margin = (int)Math.Ceiling(16 * scale);
        var availableWidth = Math.Max(1, area.Width - Math.Max(0, frameWidth) - margin * 2);
        var availableHeight = Math.Max(1, area.Height - Math.Max(0, frameHeight) - margin * 2);
        return ((int)Math.Clamp(Math.Ceiling(width * scale), 1, availableWidth),
            (int)Math.Clamp(Math.Ceiling(height * scale), 1, availableHeight));
    }

    internal static (int X, int Y) Center(WindowArea area, int width, int height) =>
        (area.X + Math.Max(0, (area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 2));
}
