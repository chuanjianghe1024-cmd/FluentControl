using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow
{
    private readonly DisplayHdrController hdr;
    private bool hdrChanging;

    private void ReadHdrStates(List<MonitorDevice> devices)
    {
        try
        {
            var values = hdr.ReadAll();
            foreach (var device in devices)
                device.Hdr = values.TryGetValue(device.Id, out var value) ? value : DisplayHdrState.Unknown();
        }
        catch (Exception ex)
        {
            StartupLog.Write("Windows HDR discovery: " + ex);
            foreach (var device in devices) device.Hdr = DisplayHdrState.Unknown(ex.Message);
        }
    }

    private static string HdrDescription(DisplayHdrState value) => value.Availability switch
    {
        HdrAvailability.Unsupported => T("Windows 未报告此屏幕支持 HDR。", "Windows does not report HDR support for this display."),
        HdrAvailability.Limited => T("HDR 受到系统或电源策略限制，请检查 Windows 显示设置。", "HDR is limited by system or power policy. Check Windows display settings."),
        HdrAvailability.Disconnected or HdrAvailability.Ambiguous or HdrAvailability.Unknown =>
            T("无法确认此屏幕的 HDR 状态，请重新读取设备。", "Cannot confirm HDR status for this display. Refresh devices.") + (value.Error.Length > 0 ? " " + value.Error : ""),
        _ when value.Enabled != value.Active => T("HDR 设置与当前信号不一致，请检查 Windows 显示设置。", "The HDR setting differs from the active signal. Check Windows display settings."),
        _ when value.Active == true => T("HDR 已开启；部分屏幕会限制亮度、对比度或色温，仍可尝试可用控制。", "HDR is on. Some displays limit brightness, contrast or color temperature; available controls remain usable."),
        _ => T("切换此屏幕的 Windows HDR；画面可能短暂黑屏，随后重新读取控制项。", "Toggle Windows HDR for this display. The screen may briefly go black; controls will then refresh.")
    };

    private FrameworkElement CreateHdrRow(MonitorDevice device, int version, bool labelled = false)
    {
        var toggle = new ToggleSwitch { IsOn = device.Hdr.Enabled == true, IsEnabled = device.Hdr.CanSet && !hdrChanging,
            OnContent = T("开启", "On"), OffContent = device.Hdr.Enabled is null ? "—" : T("关闭", "Off"), VerticalAlignment = VerticalAlignment.Center };
        var label = (labelled ? MonitorTitle(device) + " · " : "") + "Windows HDR";
        AutomationProperties.SetName(toggle, MonitorTitle(device) + " · Windows HDR");
        AutomationProperties.SetAutomationId(toggle, "hdr-" + device.Preference.Label);
        // Attach after initial state; rebuilding either view never sends a write.
        toggle.Toggled += async (_, _) => await ChangeHdrAsync(device, version, toggle.IsOn);
        return SettingsRow(label, HdrDescription(device.Hdr), toggle);
    }

    private async Task ChangeHdrAsync(MonitorDevice device, int version, bool enabled)
    {
        if (closed || refreshing || hdrChanging || colorExiting || version != generation || !device.Hdr.CanSet) return;
        hdrChanging = true; pendingWrites++;
        RefreshButton.IsEnabled = false; MainScroll.IsEnabled = false;
        var page = notificationContext;
        var invalidated = false;
        HdrChange? result = null;
        CloseMonitorOsd(); CloseMonitorAdaptation(); desktopPanel?.SetUnlocked(false);
        await gate.WaitAsync();
        try
        {
            if (closed || version != generation) return;
            // Invalidate queued sliders/choices and old enrichment before changing
            // signal mode; no stale DDC request may run on the previous handles.
            generation++; invalidated = true;
            ShowStatus(T("正在切换 HDR，随后重新读取设备…", "Changing HDR, then refreshing devices…"), InfoBarSeverity.Informational, page);
            if (enabled && systemColor is not null) await Task.Run(() => systemColor.Restore(device.Id));
            result = await Task.Run(() => hdr.Set(device.Id, enabled));
        }
        catch (Exception ex) { result = new(false, false, device.Hdr, ex.Message); }
        finally
        {
            gate.Release(); hdrChanging = false; pendingWrites--;
            if (!closed) { MainScroll.IsEnabled = true; RefreshButton.IsEnabled = !refreshing; }
        }
        if (closed || !invalidated) return;
        // Refresh destroys old DDC helpers and reads live controls after the bounded
        // HDR settling/readback. It never restores old brightness/scene values.
        await RefreshAsync(forceMonitorCapabilities: true);
        if (closed || result is null) return;
        var message = result.Confirmed
            ? T("HDR 状态已确认，显示器控制项已重新读取。", "HDR state confirmed; display controls refreshed.")
            : result.Sent
                ? T("HDR 指令已发送，但状态尚未确认。请检查画面或 Windows 显示设置后重新读取设备。", "HDR command sent, but the state is unconfirmed. Check the screen or Windows display settings, then refresh devices.")
                : T("未能切换 HDR。", "Could not change HDR.") + " " + HdrDescription(result.State);
        if (result.Error.Length > 0) message += " " + result.Error;
        ShowStatus(MonitorTitle(device) + " · " + message, result.Confirmed ? InfoBarSeverity.Success : InfoBarSeverity.Warning, page);
    }

    private async Task RunHdrChecksAsync()
    {
        MonitorRows.Visibility = CombinedRows.Visibility = Visibility.Visible;
        Root.UpdateLayout();
        var switches = Descendants<ToggleSwitch>(MonitorRows).Where(t => AutomationProperties.GetAutomationId(t).StartsWith("hdr-")).ToArray();
        if (switches.Length != 2 || !switches[0].IsEnabled || switches[1].IsEnabled)
            throw new InvalidOperationException("Per-display HDR availability is not represented.");
        var combined = Descendants<ToggleSwitch>(CombinedRows).Where(t => AutomationProperties.GetAutomationId(t).StartsWith("hdr-")).ToArray();
        if (combined.Length != 2 || !combined[0].IsEnabled || combined[1].IsEnabled)
            throw new InvalidOperationException("Overall view must retain independent HDR targets.");
        var internalPanel = new MonitorDevice { Id = "internal-panel", Model = "Internal", Connection = "test", Hdr = new(HdrAvailability.Available, false, false) };
        var internalRow = CreateHdrRow(internalPanel, generation);
        MonitorRows.Children.Add(internalRow); Root.UpdateLayout();
        var internalEnabled = Descendants<ToggleSwitch>(internalRow).Single().IsEnabled;
        MonitorRows.Children.Remove(internalRow);
        if (!internalEnabled)
            throw new InvalidOperationException("HDR requires no DDC features or channels.");
        var previous = displayDevices[0]; var oldVersion = generation;
        switches[0].IsOn = true;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((hdrChanging || refreshing) && DateTime.UtcNow < deadline) await Task.Delay(25);
        if (hdrChanging || refreshing) throw new InvalidOperationException("HDR UI did not finish refreshing.");
        if (!displayDevices[0].Hdr.Active.GetValueOrDefault() || displayDevices[1].Hdr.Enabled == true || ReferenceEquals(previous, displayDevices[0]))
            throw new InvalidOperationException("HDR toggle did not refresh exactly the selected display state.");
        await ChangeHdrAsync(previous, oldVersion, false);
        if (displayDevices[0].Hdr.Enabled != true) throw new InvalidOperationException("Stale HDR control wrote after refresh.");
        await ChangeHdrAsync(displayDevices[0], generation, false);
        if (displayDevices[0].Hdr.Active != false) throw new InvalidOperationException("HDR off was not confirmed.");
        DisplayMode_SelectionChanged(DisplayMode, null!);
        StartupLog.Write("Windows HDR UI checks passed (simulated backend; no native HDR writes)");
    }
}
