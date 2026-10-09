using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using static FluentControl.Services.Strings;

namespace FluentControl;

public sealed partial class MainWindow
{
    private ContentDialog? monitorAdaptationDialog;
    private Task monitorDiagnosticCapture = Task.CompletedTask;
    private MonitorDiagnosticSnapshot? monitorDiagnosticBaseline;
    private readonly List<MonitorDiagnosticObservation> monitorDiagnosticObservations = new();
    private void CloseMonitorAdaptation() => monitorAdaptationDialog?.Hide();

    private Button CreateAdaptationButton(MonitorDevice device)
    {
        var button = new Button { Content = T("型号适配", "Model adapters") };
        AutomationProperties.SetAutomationId(button, "adaptation-" + device.Id);
        button.Click += async (_, _) => await ShowAdapterManagerAsync(device);
        return button;
    }

    private async Task ShowMonitorAdaptationAsync(MonitorDevice device)
    {
        if (closed || refreshing || applyingProfile || monitorAdaptationDialog is not null || !displayDevices.Contains(device)) return;
        var version = generation;
        CloseMonitorOsd(); desktopPanel?.SetUnlocked(false);
        await WaitForWritesAsync();
        if (closed || refreshing || generation != version || monitorAdaptationDialog is not null) return;
        monitorDiagnosticBaseline = null; monitorDiagnosticObservations.Clear();
        monitorDiagnosticCapture = Task.CompletedTask;
        using var lifetime = new CancellationTokenSource();
        using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var open = true; var busy = false;
        var savedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "Diagnostics",
            (uiTest ? "test-" : "") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        var body = new StackPanel { Spacing = 10, MinWidth = 440 };
        body.Children.Add(Empty(T("先采集基线，再用显示器实体菜单只改一个选项，填写选项名称并记录变化。采集只读，期间暂停 FC 配置切换和桌面调节。", "Capture a baseline, change one setting in the monitor's physical menu, then label and record the change. Capture is read-only; FC profile switching and desktop adjustments are paused.")));
        var connection = new TextBox { Header = T("连接方式", "Connection setup"), PlaceholderText = "HDMI / DP / USB-C · KVM / Dock", MaxLength = 500 };
        var environment = new TextBox { Header = T("固件、模式与厂商软件备注", "Firmware, modes and vendor software notes"), PlaceholderText = "Firmware / HDR / SDR / DDC/CI", MaxLength = 1000 };
        body.Children.Add(connection); body.Children.Add(environment);
        var label = new TextBox { Header = T("本次在原厂菜单选择的选项", "Setting selected in the native menu"), PlaceholderText = T("例如：色温 6500 K / 场景 FPS", "For example: color temperature 6500 K / FPS mode"), MaxLength = 160 };
        AutomationProperties.SetAutomationId(label, "diagnostic-label"); body.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        var baseline = new Button { Content = T("采集基线", "Capture baseline") };
        var sample = new Button { Content = T("记录变化", "Record change"), IsEnabled = false };
        var export = new Button { Content = T("导出诊断报告", "Export diagnostic report"), IsEnabled = false };
        AutomationProperties.SetAutomationId(baseline, "diagnostic-baseline");
        AutomationProperties.SetAutomationId(sample, "diagnostic-sample");
        AutomationProperties.SetAutomationId(export, "diagnostic-export");
        buttons.Children.Add(baseline); buttons.Children.Add(sample); buttons.Children.Add(export); body.Children.Add(buttons);
        var submit = new Button { Content = T("导出并前往网站提交", "Export and submit on the website"), IsEnabled = false };
        body.Children.Add(submit);
        body.Children.Add(Empty(T("提交时会打开网站；使用 GitHub 或 Google 登录后，选择刚导出的报告上传。", "Submission opens the website. Sign in with GitHub or Google, then choose the exported report to upload.")));
        var includeRaw = new CheckBox { Content = new TextBlock { Text = T("导出时附带原始能力串（可能含设备标识）", "Include raw capabilities in export (may contain device identifiers)"), TextWrapping = TextWrapping.Wrap, MaxWidth = 370 } };
        body.Children.Add(includeRaw);
        var progress = new ProgressBar { IsIndeterminate = false, Visibility = Visibility.Collapsed };
        body.Children.Add(progress);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetAutomationId(status, "diagnostic-status"); body.Children.Add(status);
        var history = new ComboBox { Header = T("采集记录", "Captured observations"), HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Label" };
        body.Children.Add(history);
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var raw = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140 };
        body.Children.Add(new Expander { Header = T("原始能力串", "Raw capabilities"), Content = raw, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(details);
        body.Children.Add(Empty(T("未知私有码和动作指令只列出，不探测。差异仅是观测结果，不会自动生成可执行指令或解锁原厂菜单导航。", "Unknown private codes and action commands are listed without probing. Differences are observations, not executable commands or native menu navigation support.")));
        var dialog = new ContentDialog
        {
            Title = device.Model + " · " + T("适配助手", "Adaptation assistant"), XamlRoot = Root.XamlRoot,
            CloseButtonText = T("关闭", "Close"), Content = new ScrollViewer { Content = body, MaxHeight = 480, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        monitorAdaptationDialog = dialog;
        bool Active() => open && !closed && generation == version && ReferenceEquals(monitorAdaptationDialog, dialog);
        void UpdateButtons()
        {
            baseline.IsEnabled = !busy && monitorDiagnosticBaseline is null && !captureCancellation.IsCancellationRequested;
            sample.IsEnabled = !busy && monitorDiagnosticBaseline is not null && !string.IsNullOrWhiteSpace(label.Text) && monitorDiagnosticObservations.Count < MonitorDiagnostics.MaxObservations && !captureCancellation.IsCancellationRequested;
            export.IsEnabled = !busy && monitorDiagnosticBaseline is not null;
            submit.IsEnabled = export.IsEnabled;
            connection.IsEnabled = environment.IsEnabled = label.IsEnabled = !busy;
        }
        string Reading(DiagnosticReading? r) => r is null ? "—" : r.Status switch
        {
            "ok" => $"0x{r.Current:X4} ({r.Current}) / max 0x{r.Maximum:X4} · type {r.Type}",
            "not-probed" => T("未探测", "Not probed"),
            _ => T("读取失败", "Read failed") + (r.ErrorCode.HasValue ? $" ({r.ErrorCode})" : "")
        };
        void ShowObservation(MonitorDiagnosticObservation item)
        {
            var text = new List<string>();
            if (ReferenceEquals(item.Snapshot, monitorDiagnosticBaseline))
                text.AddRange(item.Snapshot.Readings.Select(r => $"{r.HexCode} {VcpCatalog.All.FirstOrDefault(d => d.Code == r.Code)?.Name ?? r.Key}: {Reading(r)}"));
            else
            {
                text.Add(T("相对基线的差异", "Differences from baseline"));
                foreach (var diff in item.Differences)
                    text.Add($"0x{diff.Code:X2} · " + (diff.Kind == "value" ? T("数值变化", "Value change") : T("读取状态变化", "Read status change")) + $"\n{Reading(diff.Before)} → {Reading(diff.After)}");
                if (item.Differences.Count == 0) text.Add(T("未观察到差异；这不代表该选项没有效果或不支持。", "No differences observed. This does not mean the setting had no effect or is unsupported."));
            }
            details.Text = string.Join("\n", text); raw.Text = item.Snapshot.RawCapabilities ?? "";
        }
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is MonitorDiagnosticObservation item) ShowObservation(item); };
        label.TextChanged += (_, _) => UpdateButtons();
        string Report(bool rawText) => MonitorDiagnostics.Serialize(new()
        {
            Baseline = monitorDiagnosticBaseline!, Observations = monitorDiagnosticObservations.ToList(), IncludesRawCapabilities = rawText,
            ConnectionNotes = connection.Text.Trim(), EnvironmentNotes = environment.Text.Trim(), Simulated = uiTest
        });
        async Task SaveLocalAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(savedPath)!);
            var json = Report(false); // Automatic local copies also omit vendor identifiers.
            await File.WriteAllTextAsync(savedPath + ".tmp", json);
            File.Move(savedPath + ".tmp", savedPath, true);
        }
        async Task CaptureAsync()
        {
            if (!Active() || busy) return;
            busy = true; UpdateButtons(); progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            status.Text = T("正在读取原始参数…", "Reading raw parameters…");
            var sampleLabel = label.Text.Trim(); var basis = monitorDiagnosticBaseline;
            var reporter = new Progress<DiagnosticProgress>(p =>
            {
                if (!Active() || !busy) return;
                progress.IsIndeterminate = false; progress.Maximum = p.Total; progress.Value = p.Completed;
                status.Text = F("已读取 {0}/{1} · VCP {2}", "Read {0}/{1} · VCP {2}", p.Completed, p.Total, $"0x{p.Code:X2}");
            });
            try
            {
                var token = captureCancellation.Token;
                await gate.WaitAsync(token);
                MonitorDiagnosticSnapshot snapshot;
                try
                {
                    if (!Active() || !displayDevices.Contains(device)) return;
                    snapshot = await Task.Run(() => uiTest ? UiTestData.CaptureDiagnostics(device, basis, token, reporter) :
                        (monitors ?? throw new InvalidOperationException(T("DDC/CI 不可用", "DDC/CI unavailable"))).CaptureDiagnostics(device, basis, token, reporter));
                }
                finally { gate.Release(); }
                if (!Active()) return;
                MonitorDiagnosticObservation observation;
                if (basis is null)
                {
                    monitorDiagnosticBaseline = snapshot;
                    observation = new() { Label = T("基线", "Baseline"), Snapshot = snapshot };
                }
                else
                {
                    observation = new() { Label = sampleLabel, Snapshot = snapshot, Differences = MonitorDiagnostics.Compare(basis, snapshot) };
                    monitorDiagnosticObservations.Add(observation);
                }
                history.Items.Add(observation); history.SelectedItem = observation;
                await SaveLocalAsync();
                if (Active()) status.Text = F("已记录 {0} 次调节；报告保存在：{1}", "Recorded {0} adjustments. Report saved to: {1}", monitorDiagnosticObservations.Count, savedPath) +
                    F("\n成功读取 {0} 项，失败 {1} 项。", "\nRead {0} controls successfully; {1} failed.", snapshot.Readings.Count(r => r.Status == "ok"), snapshot.Readings.Count(r => r.Status == "failed"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Active()) status.Text = ex.Message; }
            finally { busy = false; if (Active()) { progress.Visibility = Visibility.Collapsed; UpdateButtons(); } }
        }
        baseline.Click += async (_, _) => { if (busy) return; monitorDiagnosticCapture = CaptureAsync(); await monitorDiagnosticCapture; };
        sample.Click += async (_, _) => { if (busy) return; monitorDiagnosticCapture = CaptureAsync(); await monitorDiagnosticCapture; };
        async Task ExportAsync(bool submitToWebsite)
        {
            if (!Active() || busy || monitorDiagnosticBaseline is null) return;
            busy = true; UpdateButtons();
            try
            {
                var json = Report(includeRaw.IsChecked == true);
                var picker = new FileSavePicker { SuggestedFileName = "FluentControl-" + device.ModelId + "-diagnostics" };
                picker.FileTypeChoices.Add("FluentControl diagnostics JSON", new List<string> { ".json" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync(); if (file is null || !Active()) return;
                await FileIO.WriteTextAsync(file, json);
                if (submitToWebsite && Active()) await Windows.System.Launcher.LaunchUriAsync(new Uri("https://fctrl.app/adapters/submit"));
                if (Active()) status.Text = T("诊断报告已导出。", "Diagnostic report exported.");
            }
            catch (Exception ex) { if (Active()) status.Text = ex.Message; }
            finally { busy = false; if (Active()) UpdateButtons(); }
        }
        export.Click += async (_, _) => await ExportAsync(false);
        submit.Click += async (_, _) => await ExportAsync(true);
        dialog.Closing += (_, _) => { open = false; lifetime.Cancel(); };
        try { await dialog.ShowAsync(); }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            open = false; lifetime.Cancel();
            // Cancellation stops subsequent requests; an in-flight native call must finish
            // before capture releases the lifecycle gate or its cancellation sources.
            await monitorDiagnosticCapture;
            if (ReferenceEquals(monitorAdaptationDialog, dialog)) monitorAdaptationDialog = null;
        }
    }
}
