using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow
{
    private SystemColorService? systemColor;
    private string systemColorError = "";
    private readonly Dictionary<string, ControlChannel> colorChannels = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, SystemColorStatus> colorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer colorRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool colorChanging, colorExiting, colorExitReady;
    private static string SystemColorKey(string id) => "display-color/" + Uri.EscapeDataString(id) + "/temperature";

    private void InitializeSystemColor(IDisplayHdrBackend hdrBackend)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl");
            systemColor = new(uiTest ? new UiTestData.ColorBackend(hdrBackend) : new WindowsSystemColorBackend(),
                Path.Combine(root, uiTest ? "test-color-recovery.json" : "color-recovery.json"), StartupLog.Write);
        }
        catch (Exception ex) { systemColorError = ex.Message; StartupLog.Write("System color initialization: " + ex); }
        colorRefreshTimer.Tick += async (_, _) =>
        {
            if (closed || colorExiting || refreshing || pendingWrites > 0 || systemColor?.HasSessions != true || !await gate.WaitAsync(0)) return;
            try { await RefreshColorStatesAsync(); if (!closed) SynchronizeValues(); }
            catch (Exception ex) { StartupLog.Write("System color refresh: " + ex); }
            finally { gate.Release(); }
        };
        colorRefreshTimer.Start();
    }
    private void BindColorChannels()
    {
        colorChannels.Clear();
        if (systemColor is null) return;
        foreach (var device in displayDevices)
        {
            var id = device.Id;
            colorChannels[id] = new ControlChannel
            {
                DeviceId = id, PropertyKey = "system-temperature", Name = T("系统色温（软件）", "System temperature (software)"), Detail = "", Glyph = "", Unit = "K",
                Minimum = 0, Maximum = 10000, ApplyOrder = 95,
                Write = value => systemColor.Set(id, value),
                Read = () => systemColor.Read(id).Kelvin ?? throw new IOException(T("系统色温状态未确认。", "System temperature is unconfirmed."))
            };
        }
    }
    // Caller holds the shared device gate. Status queries do not apply a curve.
    private async Task RefreshColorStatesAsync()
    {
        if (systemColor is null) return;
        var ids = displayDevices.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var values = await Task.Run(() => ids.ToDictionary(id => id, systemColor.Read, StringComparer.OrdinalIgnoreCase));
        if (closed) return;
        colorStates = values;
        foreach (var (id, s) in values)
            if (colorChannels.TryGetValue(id, out var channel))
            {
                channel.IsAvailable = s.Available && !s.Recovery && s.Kelvin is not null;
                channel.ReadbackUnconfirmed = s.Kelvin is null;
                if (s.Kelvin is int k) channel.Value = k;
            }
    }
    private FrameworkElement CreateSystemColorRow(MonitorDevice device, int version, bool labelled = false)
    {
        var body = new StackPanel { Spacing = 8, Padding = new Thickness(12) };
        body.Children.Add(new TextBlock { Text = T("通过显卡调整画面冷暖，不修改显示器硬件色温。数值为近似目标，6500 K 保留原曲线；避免与夜间模式或校色软件同时使用。", "Adjusts warmth through the graphics driver without changing hardware color temperature. Values are approximate; 6500 K preserves the original curve. Avoid combining with night light or calibration tools."), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var valueText = new TextBlock();
        var slider = new Slider { Minimum = 2500, Maximum = 10000, StepFrequency = 100, Value = Math.Clamp(device.Preference.SystemTemperature, 2500, 10000), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(slider, MonitorTitle(device) + " · " + T("系统色温（软件）", "System temperature (software)"));
        AutomationProperties.SetAutomationId(slider, "system-temperature-" + device.Preference.Label);
        slider.ValueChanged += (_, _) => valueText.Text = F("软件目标：约 {0} K", "Software target: approximately {0} K", (int)slider.Value);
        valueText.Text = F("软件目标：约 {0} K", "Software target: approximately {0} K", (int)slider.Value);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Content = T("应用", "Apply") };
        var restore = new Button { Content = T("恢复原颜色", "Restore original colors") };
        AutomationProperties.SetAutomationId(apply, "system-color-apply-" + device.Preference.Label);
        apply.Click += async (_, _) => await ChangeSystemColorAsync(device, version, (int)slider.Value);
        restore.Click += async (_, _) => await ChangeSystemColorAsync(device, version, 0);
        actions.Children.Add(apply); actions.Children.Add(restore);
        var night = new HyperlinkButton { Content = T("Windows 夜间模式", "Windows night light"), Tag = "ms-settings:nightlight" };
        night.Click += OpenSettings_Click;
        void Update()
        {
            var s = colorStates.GetValueOrDefault(device.Id) ?? new SystemColorStatus(false, null, Error: systemColorError);
            apply.IsEnabled = s.Available && !s.Recovery && !colorChanging && !colorExiting;
            slider.IsEnabled = apply.IsEnabled;
            restore.IsEnabled = s.Available && (s.Recovery || s.Kelvin > 0) && !colorChanging && !colorExiting;
            status.Text = s.Error.Length > 0 ? s.Error : s.Recovery
                ? T("发现上次的颜色记录，请先恢复原颜色。", "A previous color session was found. Restore original colors first.")
                : s.Kelvin > 0 ? F("FC 正在应用约 {0} K", "FC is applying approximately {0} K", s.Kelvin)
                : s.Available ? T("FC 未接管系统颜色。", "FC is not controlling system colors.") : T("系统色温当前不可用。", "System temperature is currently unavailable.");
        }
        Update(); refreshRows.Add(Update);
        body.Children.Add(status); body.Children.Add(valueText); body.Children.Add(slider); body.Children.Add(actions); body.Children.Add(night);
        return new Expander { Header = (labelled ? MonitorTitle(device) + " · " : "") + T("系统色温（软件）", "System temperature (software)"), Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }
    private async Task ChangeSystemColorAsync(MonitorDevice device, int version, int kelvin)
    {
        if (systemColor is null || closed || refreshing || hdrChanging || colorExiting || colorChanging || version != generation) return;
        colorChanging = true; pendingWrites++; SynchronizeValues();
        var page = notificationContext;
        await gate.WaitAsync();
        try
        {
            if (closed || version != generation) return;
            await Task.Run(() => systemColor.Set(device.Id, kelvin));
            if (kelvin > 0) { device.Preference.SystemTemperature = kelvin; preferences?.Save(); }
            MarkProfileModified();
            ShowStatus(kelvin == 0 ? T("已恢复原颜色。", "Original colors restored.") : T("系统色温曲线已写入并核对。", "System temperature curve written and verified."), InfoBarSeverity.Success, page);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Warning, page); StartupLog.Write("System color change: " + ex); }
        finally
        {
            try { if (!closed) await RefreshColorStatesAsync(); }
            finally { gate.Release(); colorChanging = false; pendingWrites--; if (!closed) SynchronizeValues(); }
        }
    }
    private async Task ExitWithColorRestoreAsync()
    {
        if (closed || colorExiting) return;
        colorExiting = true; colorRefreshTimer.Stop(); Root.IsHitTestVisible = false;
        generation++; CloseMonitorOsd(); CloseMonitorAdaptation(); desktopPanel?.SetUnlocked(false);
        await gate.WaitAsync();
        try { if (systemColor is not null) await Task.Run(systemColor.RestoreAll); }
        catch (Exception ex) { StartupLog.Write("System color exit recovery: " + ex); }
        finally { gate.Release(); }
        colorExitReady = true; exitRequested = true; Close();
    }
}
