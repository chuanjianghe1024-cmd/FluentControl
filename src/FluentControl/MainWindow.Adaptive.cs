using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Net.Http;
using Windows.Foundation;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void BuildAdaptiveSettings(StackPanel section)
    {
        var settings = state.Settings.Adaptive;
        TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
        BuildAutomationSettings(section);
        if (settings.Mode == "solar") return;
        var curve = settings.Mode == "clock" ? settings.TimeCurve : settings.Curve;
        var name = new TextBox { Header = T("名称", "Name"), Text = curve.Name, MaxLength = 100 };
        name.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { curve.Name = name.Text.Trim(); SaveAdaptiveSettings(); } else name.Text = curve.Name; };
        section.Children.Add(name);
        section.Children.Add(Text(curve.Axis == "ambient"
            ? T("每根柱表示一个光照区间的固定亮度；达到下一阈值时跳档。点击添加阈值，拖动或输入数值调整。最后一档包含更高光照。", "Each bar sets a fixed brightness for a light band; reaching the next threshold switches bands. Click to add a threshold, drag or edit values. The last band includes higher light levels.")
            : T("点击图表添加节点，拖动节点调整；下方可输入精确数值。", "Click the chart to add a point, drag points to adjust, or enter exact values below.")));
        var canvas = new Canvas { Height = 250, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(canvas, "adaptive-curve");
        section.Children.Add(canvas);
        var readout = Text(""); section.Children.Add(readout);
        var liveReadout = Text(""); section.Children.Add(liveReadout);
        var preview = new Slider { Minimum = 0, Maximum = 100, Value = curve.Axis == "clock" ? 50 : 35,
            Header = T("模拟输入（不写入显示器）", "Simulated input (no display writes)") };
        section.Children.Add(preview);
        var rows = new StackPanel { Spacing = 6 }; section.Children.Add(rows);
        bool dragging = false;
        bool CanPreview() => true;
        void Draw()
        {
            if (dragging) return;
            canvas.Children.Clear(); double w = Math.Max(100, canvas.ActualWidth - (curve.Axis == "ambient" ? 76 : 48)), h = 200;
            // Plot uses log lux or linear local hours. Background is an illustration, not recorded data.
            for (int i = 0; curve.Axis == "clock" && i < 24; i++)
            {
                bool dark = curve.Axis == "clock" ? !SolarSchedule.InDayRange(i + .5, settings.LightThemeHour, settings.DarkThemeHour) : i < 9;
                var band = new Rectangle { Width = w / 24 + .5, Height = h, Fill = new SolidColorBrush(dark ? Microsoft.UI.Colors.SlateBlue : Microsoft.UI.Colors.Goldenrod), Opacity = dark ? .16 : .10, IsHitTestVisible = false };
                Canvas.SetLeft(band, 32 + i * w / 24); canvas.Children.Add(band);
            }
            for (int y = 0; y <= 100; y += 25)
            {
                var line = new Line { X1 = 32, X2 = 32 + w, Y1 = h * (1 - y / 100d), Y2 = h * (1 - y / 100d), Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray), Opacity = .3, IsHitTestVisible = false };
                canvas.Children.Add(line); var label = Text(y + "%"); label.FontSize = 10; Canvas.SetTop(label, Math.Max(0, line.Y1 - 8)); canvas.Children.Add(label);
            }
            foreach (var x in curve.Axis == "clock" ? new[] { 0d, 6d, 12d, 18d, 24d } : new[] { 0d, 10d, 100d, 1000d, 10000d })
            {
                var label = Text(curve.Axis == "clock" ? $"{x:00}:00" : $"{x:0} lux"); label.FontSize = 11;
                Canvas.SetLeft(label, Math.Clamp(32 + AdaptivePresets.Position(curve.Axis, x) * w - 18, 0, w)); Canvas.SetTop(label, 207); canvas.Children.Add(label);
            }
            var path = new Polyline { Stroke = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), StrokeThickness = 3, IsHitTestVisible = false };
            var bars = new List<Rectangle>();
            if (curve.Axis == "clock") canvas.Children.Add(path);
            else foreach (var point in curve.Points)
            {
                var bar = new Rectangle { Fill = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), Opacity = .65, IsHitTestVisible = false, Tag = "adaptive-band" };
                canvas.Children.Add(bar); bars.Add(bar);
            }
            void UpdatePlot()
            {
                if (curve.Axis == "clock")
                {
                    path.Points.Clear();
                    foreach (var p in curve.Points) path.Points.Add(new Point(32 + AdaptivePresets.Position(curve.Axis, p.X) * w, h * (1 - p.Brightness / 100)));
                }
                else for (int i = 0; i < bars.Count; i++)
                {
                    var p = curve.Points[i];
                    double left = 32 + AdaptivePresets.Position(curve.Axis, p.X) * w;
                    double right = i + 1 < bars.Count ? 32 + AdaptivePresets.Position(curve.Axis, curve.Points[i + 1].X) * w : 32 + w + 28;
                    bars[i].Width = Math.Max(.5, right - left - 1); bars[i].Height = h * p.Brightness / 100;
                    Canvas.SetLeft(bars[i], left); Canvas.SetTop(bars[i], h - bars[i].Height);
                }
                var input = AdaptivePresets.Input(curve.Axis, preview.Value / 100);
                readout.Text = $"{input:0.##} {(curve.Axis == "clock" ? "h" : "lux")} → {AdaptivePresets.Evaluate(curve, input):0.#}%";
            }
            UpdatePlot();
            foreach (var point in curve.Points)
            {
                var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue), Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = 2 };
                Canvas.SetLeft(dot, 25 + AdaptivePresets.Position(curve.Axis, point.X) * w); Canvas.SetTop(dot, h * (1 - point.Brightness / 100) - 7);
                dot.PointerPressed += (_, e) => { dragging = dot.CapturePointer(e.Pointer); e.Handled = true; };
                dot.PointerMoved += (_, e) =>
                {
                    if (!dragging || dot.PointerCaptures.Count == 0) return;
                    var position = e.GetCurrentPoint(canvas).Position;
                    point.Brightness = Math.Round(Math.Clamp(100 * (1 - position.Y / h), 0, 100));
                    var index = curve.Points.IndexOf(point);
                    if (index > 0 && index < curve.Points.Count - 1)
                    {
                        var x = AdaptivePresets.Input(curve.Axis, (position.X - 32) / w);
                        var step = curve.Axis == "ambient" ? 1d : .010000001;
                        var lo = curve.Points[index - 1].X + step;
                        var hi = curve.Points[index + 1].X - step;
                        if (lo <= hi) point.X = Math.Clamp(curve.Axis == "ambient" ? Math.Round(x) : x, lo, hi);
                    }
                    if (curve.Axis == "clock" && (index == 0 || index == curve.Points.Count - 1)) curve.Points[0].Brightness = curve.Points[^1].Brightness = point.Brightness;
                    Canvas.SetLeft(dot, 25 + AdaptivePresets.Position(curve.Axis, point.X) * w); Canvas.SetTop(dot, h * (1 - point.Brightness / 100) - 7);
                    UpdatePlot();
                    e.Handled = true;
                };
                dot.PointerCaptureLost += (_, _) => { if (!dragging) return; dragging = false; SaveAdaptiveSettings(); Draw(); BuildRows(); };
                dot.PointerReleased += (_, e) => { dot.ReleasePointerCapture(e.Pointer); e.Handled = true; };
                canvas.Children.Add(dot);
            }
            var input = AdaptivePresets.Input(curve.Axis, preview.Value / 100);
            readout.Text = CanPreview() ? $"{input:0.##} {(curve.Axis == "clock" ? "h" : "lux")} → {AdaptivePresets.Evaluate(curve, input):0.#}%" : T("预览已暂停，或正在等待光照来源。", "Preview paused or waiting for a light source.");
            if (CanPreview()) canvas.Children.Add(new Line { X1 = 32 + preview.Value / 100 * w, X2 = 32 + preview.Value / 100 * w, Y1 = 0, Y2 = h, Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray), StrokeThickness = 1, Opacity = .55, IsHitTestVisible = false });
            if (curve.Axis == "ambient")
            {
                var source = settings.LightSources.FirstOrDefault(x => x.Id == settings.ActiveSourceId);
                var live = desktopLightSourceId == settings.ActiveSourceId ? desktopLightSample : null;
                bool fresh = live is not null && live.Valid && live.Lux is double value && double.IsFinite(value) && value >= 0 &&
                    live.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(5) &&
                    DateTimeOffset.UtcNow - live.Timestamp <= TimeSpan.FromMilliseconds(Math.Max(5000, 3d * (source?.PollMilliseconds ?? 1000)));
                if (fresh)
                {
                    double lx = live!.Lux!.Value;
                    double pos = 32 + AdaptivePresets.Position("ambient", Math.Min(10000, lx)) * w;
                    canvas.Children.Add(new Line { X1 = pos, X2 = pos, Y1 = 0, Y2 = h, Stroke = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed), StrokeThickness = 2.5, IsHitTestVisible = false });
                    liveReadout.Text = F("当前光照 {0:0} lux → 目标 {1:0}% · {2:HH:mm:ss}", "Live {0:0} lux → target {1:0}% · {2:HH:mm:ss}",
                        lx, AdaptivePresets.Evaluate(curve, lx), live.Timestamp.ToLocalTime()) +
                        (live.Simulated ? " · " + T("模拟", "Simulated") : "");
                }
                else liveReadout.Text = T("当前光照：等待实时读数…", "Live light: waiting for a fresh reading…");
            }
            else liveReadout.Text = "";
        }
        void BuildRows()
        {
            rows.Children.Clear();
            foreach (var point in curve.Points.ToList())
            {
                var index = curve.Points.IndexOf(point); bool endpoint = index == 0 || index == curve.Points.Count - 1;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var x = new NumberBox { Header = curve.Axis == "clock" ? T("小时", "Hours") : "lux ≥", Value = point.X, Width = 140, IsEnabled = !endpoint, Minimum = 0, Maximum = curve.Axis == "clock" ? 24 : 10000, SmallChange = curve.Axis == "ambient" ? 1 : .5 };
                var y = new ComboBox { Header = T("亮度快捷值", "Brightness preset") + " %", Width = 140,
                    ItemsSource = Enumerable.Range(0, 11).Select(i => (i * 10).ToString()).ToArray(),
                    SelectedItem = (Math.Round(point.Brightness / 10) * 10).ToString() };
                var customY = new NumberBox { Header = T("自定义亮度 %", "Custom brightness %"), Value = point.Brightness, Width = 140, Minimum = 0, Maximum = 100, SmallChange = 1 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(y, "adaptive-preset-" + index);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(customY, "adaptive-y-" + index);
                x.ValueChanged += (_, e) =>
                {
                    if (endpoint) return;
                    double value = curve.Axis == "ambient" ? Math.Round(e.NewValue) : e.NewValue;
                    double step = curve.Axis == "ambient" ? 1 : .01;
                    if (!double.IsFinite(value) || value - curve.Points[index - 1].X < step || curve.Points[index + 1].X - value < step) { x.Value = point.X; return; }
                    point.X = value; if (x.Value != value) x.Value = value; SaveAdaptiveSettings(); Draw();
                };
                void SetBrightness(double number)
                {
                    if (!double.IsFinite(number) || number < 0 || number > 100) { customY.Value = point.Brightness; return; }
                    double value = Math.Round(number);
                    if (point.Brightness == value) return;
                    point.Brightness = value; customY.Value = value;
                    if (curve.Axis == "clock" && endpoint) curve.Points[0].Brightness = curve.Points[^1].Brightness = value;
                    SaveAdaptiveSettings(); Draw();
                }
                y.SelectionChanged += (_, _) => { if (y.SelectedItem is string selected && double.TryParse(selected, out var value)) SetBrightness(value); };
                customY.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) SetBrightness(e.NewValue); };
                customY.LostFocus += (_, _) => { if (curve.Axis == "clock" && endpoint) BuildRows(); };
                var remove = new Button { Content = T("删除", "Delete"), IsEnabled = !endpoint, VerticalAlignment = VerticalAlignment.Bottom };
                remove.Click += (_, _) => { curve.Points.Remove(point); SaveAdaptiveSettings(); BuildRows(); Draw(); };
                row.Children.Add(x); row.Children.Add(y); row.Children.Add(customY); row.Children.Add(remove); rows.Children.Add(row);
            }
        }
        canvas.PointerPressed += (_, e) =>
        {
            if (dragging || curve.Points.Count >= 64) return;
            var at = e.GetCurrentPoint(canvas).Position;
            if (at.Y < 0 || at.Y > 200 || at.X <= 32 || at.X >= canvas.ActualWidth - (curve.Axis == "ambient" ? 44 : 16)) return;
            var x = AdaptivePresets.Input(curve.Axis, (at.X - 32) / Math.Max(100, canvas.ActualWidth - (curve.Axis == "ambient" ? 76 : 48)));
            if (curve.Axis == "ambient") x = Math.Round(x);
            if (curve.Points.Any(p => Math.Abs(p.X - x) < (curve.Axis == "ambient" ? 1 : .01))) return;
            curve.Points.Add(new() { X = x, Brightness = Math.Round(Math.Clamp(100 * (1 - at.Y / 200), 0, 100)) });
            curve.Points.Sort((a, b) => a.X.CompareTo(b.X)); SaveAdaptiveSettings(); BuildRows(); Draw();
        };
        adaptiveCurveLiveUpdate = Draw;
        canvas.SizeChanged += (_, _) => Draw(); preview.ValueChanged += (_, _) => Draw(); BuildRows();
        var exchange = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (bool importing in new[] { true, false })
        {
            var button = new Button { Content = importing ? T("导入 JSON", "Import JSON") : T("导出 JSON", "Export JSON") };
            button.Click += async (_, _) =>
            {
                var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, MaxLength = AdaptivePresets.MaximumBytes,
                    Text = importing ? "" : AdaptivePresets.Export(curve), IsReadOnly = !importing };
                var dialog = new ContentDialog { Title = button.Content, Content = input, PrimaryButtonText = importing ? T("导入", "Import") : "",
                    CloseButtonText = T("关闭", "Close"), XamlRoot = Root.XamlRoot, RequestedTheme = ShellRoot.RequestedTheme };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || !importing) return;
                try { var imported = AdaptivePresets.Parse(input.Text); if (imported.Axis == "clock") settings.TimeCurve = imported; else settings.Curve = AdaptivePresets.AsBands(imported); settings.Mode = imported.Axis; settings.Enabled = false; SaveAdaptiveSettings(); BuildSettings(); }
                catch { ShowStatus(T("预设无效，请检查格式和节点范围。", "Invalid preset. Check its format and point ranges."), InfoBarSeverity.Error); }
            };
            exchange.Children.Add(button);
        }
        section.Children.Add(exchange);
    }
}
