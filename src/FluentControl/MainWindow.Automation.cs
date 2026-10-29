using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net.Http;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer adaptiveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly LightHistory lightHistory = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "LightHistory"));
    private readonly long adaptiveClockOrigin = System.Diagnostics.Stopwatch.GetTimestamp();
    private double? adaptiveLatestTarget;
    private string adaptiveLatestInfo = "";
    private readonly LightSmoothing adaptiveFilter = new();
    private readonly HttpClient lightClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private CancellationTokenSource? lightRequest;
    private CancellationTokenSource? localDiscovery;
    private int adaptiveRevision;
    private long manualBrightnessHold;
    private bool adaptiveBusy, adaptiveOutputFault;
    private DateTimeOffset adaptiveDue, adaptiveLastTick, adaptiveLastWrite;
    private readonly Dictionary<string, double> adaptiveReadbacks = new();
    private readonly Dictionary<string, double> adaptiveAppliedTargets = new();
    private readonly Dictionary<string, AdaptiveBrightnessTransition> adaptiveTransitions = new();
    private readonly HashSet<string> adaptiveFailedTargets = new();
    private TextBlock? adaptiveLiveStatus;
    private string? automaticTheme;
    private void InitializeAdaptiveAutomation()
    {
        FcsRemoteGateway.Configure(state.Settings.RemoteGateway);
        StartSensorArrival();
        ControlOperations.ManualBrightnessRequested += HoldAdaptiveBrightness;
        adaptiveTimer.Tick += async (_, _) => await TickAdaptiveAsync();
        if (!uiTest) adaptiveTimer.Start();
    }
    private void StopAdaptiveAutomation()
    {
        sensorArrivalTimer.Stop(); sensorLifetime.Cancel();
        adaptiveTimer.Stop(); lightRequest?.Cancel(); localDiscovery?.Cancel(); desktopLightRequest?.Cancel();
        ControlOperations.ManualBrightnessRequested -= HoldAdaptiveBrightness;
        adaptiveRevision++; automaticTheme = null;
    }
    private void HoldAdaptiveBrightness() => Interlocked.Exchange(ref manualBrightnessHold, DateTimeOffset.UtcNow.AddMinutes(10).UtcTicks);
    private void SaveAdaptiveSettings()
    {
        adaptiveRevision++; lightRequest?.Cancel(); desktopLightRequest?.Cancel(); desktopLightSample = null; desktopLightDue = default; desktopLightFailed = false; adaptiveLatestTarget = null; adaptiveFilter.Reset(); adaptiveDue = default; adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); adaptiveTransitions.Clear(); adaptiveOutputFault = false; adaptiveFailedTargets.Clear();
        if (!state.Settings.Adaptive.Enabled || !state.Settings.Adaptive.ThemeEnabled || state.Settings.Adaptive.Mode == "ambient") { automaticTheme = null; ApplyAppTheme(); }
        SaveState(); UpdateDesktopStatus();
    }
    private void AdaptiveStatus(string text) { if (adaptiveLiveStatus is not null) adaptiveLiveStatus.Text = text; }
    private async Task TickAdaptiveAsync(bool fixture = false)
    {
        if (fixture && !uiTest) throw new InvalidOperationException("Automation fixture requires simulated devices.");
        UpdateDesktopStatus();
        if (closed || sensorSetupActive || (uiTest && !fixture) || adaptiveBusy) return;
        if (state.Settings.Adaptive.Mode != "ambient") _ = RefreshDesktopLightAsync();
        var now = DateTimeOffset.UtcNow;
        if (adaptiveLastTick != default && (now < adaptiveLastTick || now - adaptiveLastTick > TimeSpan.FromSeconds(10)))
        {
            // Sleep/resume and wall-clock changes invalidate all pre-sleep samples.
            adaptiveLatestTarget = null; adaptiveFilter.Reset(); desktopLightSample = null; desktopLightDue = default; adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); adaptiveTransitions.Clear(); adaptiveDue = default; adaptiveLastWrite = now;
        }
        adaptiveLastTick = now;
        var s = state.Settings.Adaptive;
        bool chartOpen = s.Mode == "ambient" && selectedAdaptiveSection == "presets" && adaptiveCurveLiveUpdate is not null;
        bool collectOnly = (!s.Enabled || adaptiveOutputFault) && (s.RecordLightHistory || desktopPanel is not null || chartOpen) && s.Mode == "ambient" &&
            (s.ActiveSourceId != "simulation" || chartOpen);
        if ((!s.Enabled && !collectOnly) || (adaptiveOutputFault && !collectOnly) || refreshing || hdrChanging || colorExiting || pendingWrites > 0 || applyingProfile || monitorAdaptationDialog is not null) return;
        var source = s.LightSources.FirstOrDefault(x => x.Id == s.ActiveSourceId);
        var period = s.Mode == "ambient" ? source?.PollMilliseconds ?? 1000 : 1000;
        bool sampleDue = now >= adaptiveDue;
        if (!sampleDue && adaptiveTransitions.Count > 0 && adaptiveTransitions.Values.All(t => t.Complete)) return;
        if (sampleDue) adaptiveDue = now.AddMilliseconds(period);
        var revision = adaptiveRevision; var version = generation;
        var targetIds = AdaptivePresets.Targets(s).OrderBy(id => displayDevices.FirstOrDefault(d => d.Id == id)?.Left ?? int.MaxValue)
            .ThenBy(id => displayDevices.FirstOrDefault(d => d.Id == id)?.Top ?? int.MaxValue).ToArray(); var mode = s.Mode;
        var curve = AdaptivePresets.Copy(mode == "clock" ? s.TimeCurve : s.Curve);
        adaptiveBusy = true;
        bool outputStage = false;
        try
        {
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId));
            double hour = local.TimeOfDay.TotalHours, target;
            bool day = SolarSchedule.InDayRange(hour, s.LightThemeHour, s.DarkThemeHour);
            string info;
            if (!sampleDue)
            {
                if (adaptiveLatestTarget is not double cachedTarget) return;
                target = cachedTarget; info = adaptiveLatestInfo;
            }
            else if (mode == "solar")
            {
                var sun = SolarSchedule.Calculate(local.Date, s.Latitude, s.Longitude);
                day = sun.IsDay(now); target = day ? s.DayBrightness : s.NightBrightness;
                info = sun.Sunrise is { } rise && sun.Sunset is { } set
                    ? $"☀ {TimeZoneInfo.ConvertTime(rise, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId)):HH:mm} / ☾ {TimeZoneInfo.ConvertTime(set, TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId)):HH:mm}"
                    : day ? T("极昼", "Polar day") : T("极夜", "Polar night");
            }
            else if (mode == "clock") { target = AdaptivePresets.Evaluate(curve, hour); info = local.ToString("HH:mm:ss"); }
            else
            {
                LightSample sample;
                if (s.ActiveSourceId == "simulation")
                {
                    sample = LightFeed.Parse(LightFeed.Serialize(new() { Format = "fluentcontrol.light-sample", Version = 1, SourceId = "fc-simulation", Timestamp = now,
                        Lux = 200 + 180 * Math.Sin(now.ToUnixTimeMilliseconds() / 1000d * Math.PI / 30), Valid = true, Simulated = true }), now, 1000);
                }
                else
                {
                    if (source is null) throw new InvalidDataException();
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(LocalLightProtocol.IsLocal(source.Url) ? 25 : 3)); lightRequest = cancellation;
                    try { sample = LocalLightProtocol.IsLocal(source.Url)
                        ? (uiTest ? await LocalLightSensor.ReadAsync(source, cancellation.Token) : await FcsGatewayClient.ReadAutoAsync(source, cancellation.Token))
                        : await NetworkSignalSensor.ReadSourceAsync(lightClient, source, cancellation.Token); }
                    finally { lightRequest = null; }
                }
                if (closed || revision != adaptiveRevision || version != generation) return;
                PublishDesktopLight(s.ActiveSourceId, sample);
                if (s.RecordLightHistory && !uiTest) await RecordLightSampleAsync(s.ActiveSourceId, sample);
                if (closed || revision != adaptiveRevision || version != generation) return;
                if (!s.Enabled || adaptiveOutputFault) { AdaptiveStatus($"{sample.SourceId} · {sample.Lux:0.##} lux"); return; }
                double? lux = adaptiveFilter.Add(sample, smooth: false);
                if (lux is null) { AdaptiveStatus(T("等待 3 个有效样本…", "Waiting for 3 valid samples…")); return; }
                target = AdaptivePresets.Evaluate(curve, lux.Value);
                info = $"{sample.SourceId} · {lux:0.##} lux" + (sample.Simulated ? " · " + T("模拟", "Simulated") : "");
            }
            adaptiveLatestTarget = target; adaptiveLatestInfo = info;
            if (closed || revision != adaptiveRevision || version != generation || !s.Enabled) return;
            if (s.ThemeEnabled && mode != "ambient")
            {
                string next = day ? "light" : "dark";
                if (automaticTheme != next) { automaticTheme = next; ApplyAppTheme(); }
            }
            AdaptiveStatus(info + $" → {target:0.#}%");
            if (now.UtcTicks < Interlocked.Read(ref manualBrightnessHold)) { adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); adaptiveTransitions.Clear(); AdaptiveStatus(info + " · " + T("手动调整保持中", "Manual adjustment hold")); return; }
            if (now - adaptiveLastWrite < TimeSpan.FromMilliseconds(s.Transition.IntervalMs) || pendingWrites > 0) return;
            if (!await gate.WaitAsync(0)) return;
            outputStage = true;
            try
            {
                if (closed || refreshing || hdrChanging || colorExiting || revision != adaptiveRevision || version != generation || !s.Enabled || pendingWrites > 0) return;
                var messages = new List<string>();
                foreach (var targetId in targetIds)
                {
                    if (closed || refreshing || hdrChanging || revision != adaptiveRevision || version != generation || !s.Enabled || pendingWrites > 0) break;
                    var device = displayDevices.FirstOrDefault(x => x.Id == targetId);
                    var channel = device?.Channels.FirstOrDefault(x => x.PropertyKey == "brightness" && x.IsAvailable && x.Options is null && x.Read is not null && x.Maximum > x.Minimum);
                    if (device is null || channel is null) { messages.Add(T("目标屏幕不可用", "Target display unavailable")); continue; }
                    if (adaptiveFailedTargets.Contains(targetId)) { messages.Add(device.DisplayName + " · " + T("屏幕写入失败，重新启用后再试", "Display write failed; re-enable to retry")); continue; }
                    double? previousReadback = adaptiveReadbacks.TryGetValue(targetId, out var readback) ? readback : null;
                    var transition = adaptiveTransitions.TryGetValue(targetId, out var existing) ? existing : adaptiveTransitions[targetId] = new AdaptiveBrightnessTransition();
                    try
                    {
                        var result = await Task.Run(() =>
                        {
                            // Re-resolve HDR for each target; a blocked/failed screen must not block another.
                            var states = hdr.ReadAll();
                            if (!states.TryGetValue(device.Id, out var value) || value.Active == true || value.Enabled == true ||
                                !(value.Availability == HdrAvailability.Unsupported || value.Availability == HdrAvailability.Available && value.Active == false && value.Enabled == false)) return "HDR";
                            double current = channel.Read!();
                            if (!double.IsFinite(current)) throw new InvalidDataException();
                            if (previousReadback is double previous && Math.Abs(current - previous) > 1) { HoldAdaptiveBrightness(); return "manual"; }
                            if (Math.Abs((DateTimeOffset.UtcNow - now).TotalSeconds) > 5 || DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref manualBrightnessHold) || revision != adaptiveRevision || version != generation || closed || !s.Enabled || pendingWrites > 0 || applyingProfile) return "manual";
                            channel.Value = current; channel.ReadbackUnconfirmed = false;
                            double percent = (current - channel.Minimum) / (channel.Maximum - channel.Minimum) * 100;
                            // The target band remains discrete; each monitor eases independently.
                            double? next = transition.Next(percent, target, DateTimeOffset.UnixEpoch + System.Diagnostics.Stopwatch.GetElapsedTime(adaptiveClockOrigin), s.Transition);
                            if (next is null) return "";
                            var errors = ControlOperations.Apply(new[] { channel }, channel.Minimum + next.Value / 100 * (channel.Maximum - channel.Minimum), automatic: true);
                            if (errors.Count != 0) throw new IOException(string.Join("; ", errors));
                            return "";
                        });
                        if (closed || revision != adaptiveRevision || version != generation) return;
                        if (result == "HDR") messages.Add(device.DisplayName + " · " + T("HDR 或显示状态未确认，暂停亮度调节", "HDR or display state unconfirmed; brightness paused"));
                        else if (result == "manual") messages.Add(T("手动调整保持中", "Manual adjustment hold"));
                        else { adaptiveReadbacks[targetId] = channel.Value; adaptiveAppliedTargets[targetId] = target; }
                    }
                    catch (Exception ex)
                    {
                        if (closed || revision != adaptiveRevision || version != generation) return;
                        adaptiveFailedTargets.Add(targetId);
                        adaptiveReadbacks.Remove(targetId); adaptiveAppliedTargets.Remove(targetId); adaptiveTransitions.Remove(targetId);
                        messages.Add(device.DisplayName + " · " + T("屏幕写入失败，重新启用后再试", "Display write failed; re-enable to retry"));
                        StartupLog.Write("Adaptive target: " + ex.GetType().Name);
                    }
                }
                adaptiveLastWrite = DateTimeOffset.UtcNow;
                if (targetIds.Length == 0) messages.Add(T("目标屏幕不可用", "Target display unavailable"));
                if (messages.Count > 0) AdaptiveStatus(info + " · " + string.Join("; ", messages.Distinct()));
                SynchronizeValues();
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (closed || revision != adaptiveRevision || version != generation) return;
            if (mode == "ambient") FailDesktopLight();
            if (s.RecordLightHistory && mode == "ambient" && !uiTest) await RecordLightSampleAsync(s.ActiveSourceId, null);
            adaptiveLatestTarget = null; adaptiveFilter.Reset(); adaptiveDue = DateTimeOffset.UtcNow.AddSeconds(10);
            AdaptiveStatus(T("光照来源超时或已取消", "Light source timed out or canceled"));
        }
        catch (Exception ex)
        {
            if (closed || revision != adaptiveRevision || version != generation) return;
            if (mode == "ambient" && !outputStage) FailDesktopLight();
            if (revision == adaptiveRevision && s.RecordLightHistory && mode == "ambient" && !outputStage && !uiTest) await RecordLightSampleAsync(s.ActiveSourceId, null);
            adaptiveLatestTarget = null; adaptiveFilter.Reset(); adaptiveReadbacks.Clear(); adaptiveAppliedTargets.Clear(); adaptiveTransitions.Clear(); adaptiveDue = DateTimeOffset.UtcNow.AddSeconds(10);
            if (outputStage) adaptiveOutputFault = true;
            AdaptiveStatus(outputStage ? T("屏幕写入失败，重新启用后再试", "Display write failed; re-enable to retry") : T("数据或屏幕不可用，已暂停；稍后重试", "Data or display unavailable; paused, retrying later"));
            StartupLog.Write("Adaptive automation: " + ex.GetType().Name); // no source URL, token or location in logs
        }
        finally { lightRequest = null; adaptiveBusy = false; }
    }

    private async Task RecordLightSampleAsync(string subscriptionId, LightSample? sample)
    {
        try { await lightHistory.AppendAsync(subscriptionId, sample); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StartupLog.Write("Light history unavailable: " + ex.GetType().Name); }
    }
}
