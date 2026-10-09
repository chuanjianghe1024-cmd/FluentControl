using FluentControl.Services;

internal static class WindowGeometryTests
{
    internal static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var fourK = new WindowArea(0, 0, 3840, 2120);
        foreach (var sample in new[] { (1d, 1120, 780), (1.25, 1400, 975), (1.5, 1680, 1170), (1.75, 1960, 1365), (2d, 2240, 1560) })
        {
            var size = WindowGeometry.ClientSize(1120, 780, sample.Item1, fourK, 24, 60);
            Check(size == (sample.Item2, sample.Item3), "4K window must retain its logical client size at each display scale.");
        }
        Check(WindowGeometry.ClientSize(440, 220, 1.5, fourK, 0, 0) == (660, 330), "150% identification must not squeeze scaled text into 440x220 physical pixels.");
        Check(WindowGeometry.ClientSize(580, 660, 1.5, fourK, 24, 60) == (870, 990), "OSD must use the target display's scale.");
        var small = new WindowArea(-1280, 40, 1280, 720);
        var fit = WindowGeometry.ClientSize(1120, 780, 2, small, 32, 80);
        Check(fit == (1184, 576), "Window frame and taskbar work area must remain on screen.");
        Check(WindowGeometry.Center(small, fit.Width + 32, fit.Height + 80) == (-1248, 72), "Negative monitor coordinates and taskbar offsets must be preserved.");
        Check(WindowGeometry.Center(new(-3840, -100, 3840, 2120), 1680, 1170) == (-2760, 375), "Center on the target 4K monitor, not the primary display.");
        Check(WindowGeometry.ClientSize(441, 221, 1.25, fourK, 0, 0) == (552, 277), "Fractional pixel sizes round up to avoid clipping.");
        Console.WriteLine("PASS: 4K 100/125/150/175/200% window sizes, identification/OSD scale, small work areas and negative monitor coordinates.");
    }
}
