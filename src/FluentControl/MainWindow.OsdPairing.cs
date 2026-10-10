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
    private Task osdPairingOperation = Task.CompletedTask;
    private static string PairingAction(string action) => action switch
    {
        "back" => T("返回 / 退出", "Back / exit"), "toggle" => T("打开 / 关闭菜单", "Toggle menu"),
        "increase" => T("增加", "Increase"), "decrease" => T("减少", "Decrease"),
        "enable" => T("启用 OSD", "Enable OSD"), "disable" => T("禁用 OSD，保留按键事件", "Disable OSD, keep button events"),
        "disable-events" => T("禁用 OSD 与按键事件", "Disable OSD and button events"), _ => MonitorService.NativeMenuName(action)
    };
    private static string PairingContext(string context) => context switch
    {
        "closed" => T("菜单关闭", "Menu closed"), "main-menu" => T("主菜单", "Main menu"),
        "submenu" => T("子菜单", "Submenu"), "adjustment" => T("数值调节", "Value adjustment"), _ => T("未知", "Unknown")
    };
    private static string PairingOutcome(string outcome) => outcome switch
    {
        "success" => T("成功", "Success"), "failure" => T("失败", "Failure"), "other" => T("其他效果", "Other effect"),
        "uncertain" => T("不确定", "Uncertain"), _ => T("未确认", "Unconfirmed")
    };
    private Button CreateOsdPairingButton(MonitorDevice device)
    {
        var button = new Button { Content = T("OSD 配对 / 遥控", "OSD pairing / remote") };
        AutomationProperties.SetAutomationId(button, "osd-pairing-" + device.Id);
        button.Click += async (_, _) => { CloseMonitorOsd(); Activate(); await ShowOsdPairingAsync(device); };
        return button;
    }

    private async Task ShowOsdPairingAsync(MonitorDevice device, OsdPairingSession? fixture = null, string? fixtureDirectory = null)
    {
        if (closed || refreshing || applyingProfile || monitorAdaptationDialog is not null || !displayDevices.Contains(device)) return;
        var version = generation;
        CloseMonitorOsd(); desktopPanel?.SetUnlocked(false); await WaitForWritesAsync();
        if (closed || refreshing || version != generation || monitorAdaptationDialog is not null) return;
        var directory = fixtureDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", uiTest ? "TestOsdPairings" : "OsdPairings");
        var store = new OsdPairingStore(directory, device.Id);
        var identity = OsdIdentity.Capture(device);
        OsdPairingProfile profile;
        try { profile = store.Load(identity, uiTest) ?? OsdPairingProfile.Create(device, uiTest); }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); return; }
        if (profile.Sessions.Count >= 64)
        {
            // Archive locally before starting a new bounded history. Never silently discard evidence.
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "archive-" + Guid.NewGuid().ToString("N") + ".json"), OsdPairingStore.Serialize(profile));
                profile = OsdPairingProfile.Create(device, uiTest);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); return; }
        }
        if (profile.Commands.Count == 3 && device.InstalledAdapter is { } adapter)
            foreach (var c in adapter.NativeMenu) profile.Commands.Add(new() { Name = PairingAction(c.Action), Code = c.Code, Value = c.Value, Source = adapter.Id + " · " + adapter.Evidence, SourceModel = device.ModelId });
        var session = new OsdSessionRecord { Width = device.Width, Height = device.Height };
        profile.Sessions.Add(session);
        var engine = fixture ?? (uiTest ? new OsdPairingSession(_ => new(0, 0, 0, 50), (_, _) => throw new IOException("No simulated pairing writer")) :
            new OsdPairingSession(code => monitors!.ReadPairing(device, code), (code, value) => monitors!.WritePairing(device, code, value)));
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? observationCancellation = null;
        var open = true; var busy = false; var baselineReady = false; OsdTrial? selectedTrial = null; OsdCommand? pendingConfirmation = null;
        var caps = VcpCapabilities.Parse(device.CapabilitiesText);
        var canDisableEvents = System.Version.TryParse(identity.MccsVersion, out var mccs) && mccs >= new System.Version(2, 2) || caps.Features.TryGetValue(0xCA, out var options) && options.Contains((byte)3);
        var root = new StackPanel { Spacing = 10, MinWidth = 420 };
        root.Children.Add(Empty(device.Model + " · " + identity.ModelId + " · " + T("固件版本", "Firmware version") + " " + identity.Firmware + " · MCCS " + identity.MccsVersion));
        root.Children.Add(Empty(T("全部在本地完成，无需登录或上传。请观察显示器，确认实际效果后绑定。", "Everything works locally, without sign-in or upload. Observe the monitor and bind only confirmed effects.")));
        var navigation = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { T("1 · 基础测试", "1 · Basic tests"), T("2 · 候选指令", "2 · Candidate commands"), T("3 · 遥控", "3 · Remote"), T("4 · 记录与文件", "4 · History and files") }) navigation.Items.Add(name);
        AutomationProperties.SetAutomationId(navigation, "pairing-navigation"); root.Children.Add(navigation);
        var pages = Enumerable.Range(0, 4).Select(_ => new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed }).ToArray();
        foreach (var page in pages) root.Children.Add(page);
        navigation.SelectionChanged += (_, _) => { for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed; };
        navigation.SelectedIndex = 0;
        var reuse = new CheckBox { Content = Empty(T("读回失败时，允许沿用本次会话已读取的按键字段", "If readback fails, allow known button fields from this session")) };
        AutomationProperties.SetAutomationId(reuse, "pairing-reuse-fields"); pages[0].Children.Add(reuse);
        pages[0].Children.Add(Empty(T("这只沿用本次窗口内的按键字段，不使用历史值。禁用 OSD 后实体菜单可能不可用；可以再次发送启用。", "Only button fields read in this window are reused. Disabling OSD may prevent the physical menu from working; you can send Enable again.")));
        var basicButtons = new Dictionary<string, Button>();
        foreach (var entry in new[] { ("osd-enable", "enable"), ("osd-disable", "disable"), ("osd-disable-events", "disable-events") })
        {
            var button = new Button { Content = PairingAction(entry.Item2), HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, "pairing-" + entry.Item1); pages[0].Children.Add(button); basicButtons[entry.Item1] = button;
        }
        pages[0].Children.Add(Empty(T("保留按键事件不等于保留实体菜单操作。第三项仅对 MCCS 2.2 或明确声明该值的设备开放。", "Keeping button events does not keep physical menu operation enabled. The third action requires MCCS 2.2 or explicit support for that value.")));
        var accessible = new CheckBox { Content = Empty(T("我已确认原厂菜单可以打开（软件或实体按键）", "I confirmed that the native menu opens (software or physical button)")) };
        AutomationProperties.SetAutomationId(accessible, "pairing-accessible"); pages[1].Children.Add(accessible);
        pages[1].Children.Add(Empty(T("没有通用的方向键写入码。0x03 是按键事件读取，不能把读到的事件编号直接当成遥控指令。", "There are no universal direction-key write codes. 0x03 reports button events; event numbers are not remote commands.")));
        var candidates = new ComboBox { Header = T("候选指令", "Candidate command"), DisplayMemberPath = "Display", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(candidates, "pairing-candidates"); pages[1].Children.Add(candidates);
        var source = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; pages[1].Children.Add(source);
        var experimental = new CheckBox { Content = Empty(T("确认按所列来源试发此候选；效果尚未验证", "Test this candidate using the listed source; its effect is unverified")) };
        pages[1].Children.Add(experimental);
        var sendCandidate = new Button { Content = T("发送候选指令", "Send candidate command") }; pages[1].Children.Add(sendCandidate);
        var manual = new StackPanel { Spacing = 8 };
        var nameBox = new TextBox { Header = T("指令名称", "Command name"), MaxLength = 120 };
        var codeBox = new TextBox { Header = T("VCP 代码（十六进制）", "VCP code (hex)"), PlaceholderText = "03 / E0–FF", MaxLength = 4 };
        var valueBox = new TextBox { Header = T("值（十六进制）", "Value (hex)"), PlaceholderText = "0000–FFFF", MaxLength = 6 };
        var sourceBox = new TextBox { Header = T("来源及适用型号", "Source and applicable model"), MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = T("添加候选（不发送）", "Add candidate (does not send)") };
        foreach (var control in new FrameworkElement[] { nameBox, codeBox, valueBox, sourceBox, add }) manual.Children.Add(control);
        pages[1].Children.Add(new Expander { Header = T("手工添加实验指令", "Add an experimental command"), Content = manual, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var unknownCodes = caps.Features.Keys.Where(c => c >= 0xE0).Select(c => $"0x{c:X2}");
        pages[1].Children.Add(Empty(T("已声明的私有码（仅供记录，不自动生成指令）：", "Advertised private codes (recorded only, no automatic commands): ") + string.Join(", ", unknownCodes)));
        pages[2].Children.Add(Empty(T("只有本机观察并绑定的操作可以使用。未配齐也可以保存；返回和确认可分别对应摇杆左、右。", "Only locally observed bindings can be used. Partial pairings can be saved; back and confirm may correspond to joystick left and right.")));
        var remoteButtons = new Dictionary<string, Button>();
        var remoteGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (int i = 0; i < 3; i++) { remoteGrid.ColumnDefinitions.Add(new()); remoteGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
        foreach (var pos in new[] { ("up", 0, 1), ("back", 1, 0), ("enter", 1, 2), ("down", 2, 1) })
        {
            var button = new Button { Content = PairingAction(pos.Item1), HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(button, pos.Item2); Grid.SetColumn(button, pos.Item3); remoteGrid.Children.Add(button); remoteButtons[pos.Item1] = button;
            AutomationProperties.SetAutomationId(button, "pairing-remote-" + pos.Item1);
        }
        pages[2].Children.Add(remoteGrid);
        var extras = new StackPanel { Spacing = 8 }; pages[2].Children.Add(extras);
        foreach (var action in OsdPairingProfile.Actions.Except(new[] { "up", "down", "back", "enter" }))
        { var button = new Button { Content = PairingAction(action) }; extras.Children.Add(button); remoteButtons[action] = button; AutomationProperties.SetAutomationId(button, "pairing-remote-" + action); }
        var bindingsText = new TextBlock { TextWrapping = TextWrapping.Wrap }; pages[2].Children.Add(bindingsText);
        var rePair = new Button { Content = T("重新配对（保留记录，清除绑定）", "Pair again (keep history, clear bindings)") }; pages[2].Children.Add(rePair);
        var connection = new TextBox { Header = T("连接方式", "Connection setup"), PlaceholderText = "HDMI / DP / USB-C / Dock", MaxLength = 500 };
        var hdr = new ComboBox { Header = "HDR" }; foreach (var v in new[] { "unknown", "on", "off" }) hdr.Items.Add(new ComboBoxItem { Content = v == "unknown" ? T("未知", "Unknown") : v == "on" ? T("开启", "On") : T("关闭", "Off"), Tag = v }); hdr.SelectedIndex = 0;
        var environment = new TextBox { Header = T("固件、模式与厂商软件备注", "Firmware, modes and vendor software notes"), MaxLength = 1000, TextWrapping = TextWrapping.Wrap };
        pages[3].Children.Add(connection); pages[3].Children.Add(hdr); pages[3].Children.Add(environment);
        var eventLabel = new TextBox { Header = T("本次按下的实体按键", "Physical button pressed in this capture"), MaxLength = 160 };
        var observe = new Button { Content = T("采集 5 秒按键事件（只读）", "Capture button events for 5 seconds (read-only)") };
        var stop = new Button { Content = T("停止采集", "Stop capture"), IsEnabled = false };
        var eventText = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 130 };
        pages[3].Children.Add(Empty(T("读取 0x02 / 0x52 / 0x03，不写入确认值。0x03 读取可能取走一个事件；无事件不代表按键无效。", "Reads 0x02 / 0x52 / 0x03 without acknowledgement writes. Reading 0x03 may consume an event; no event does not mean the button failed.")));
        foreach (var control in new FrameworkElement[] { eventLabel, observe, stop, eventText }) pages[3].Children.Add(control);
        var includeRaw = new CheckBox { Content = Empty(T("导出时附带原始能力串（可能含设备标识）", "Include raw capabilities in export (may contain device identifiers)")) };
        var export = new Button { Content = T("导出配对文件", "Export pairing file") };
        var import = new Button { Content = T("导入候选（需本机重新验证）", "Import candidates (local verification required)") };
        var save = new Button { Content = T("保存配对", "Save pairing") }; AutomationProperties.SetAutomationId(save, "pairing-save");
        foreach (var control in new FrameworkElement[] { includeRaw, export, import, save, Empty(directory) }) pages[3].Children.Add(control);
        var context = new ComboBox { Header = T("发送前菜单所在位置", "Menu context before sending"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in OsdPairingProfile.Contexts) context.Items.Add(new ComboBoxItem { Content = PairingContext(c), Tag = c }); context.SelectedIndex = 0;
        root.Children.Add(context);
        var confirmation = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        confirmation.Children.Add(Empty(T("此指令会禁用 OSD 与按键事件。确定要发送吗？需要恢复时可发送启用 OSD。", "This disables OSD and button events. Send it? Use Enable OSD to attempt recovery.")));
        var confirmSend = new Button { Content = T("确认发送", "Confirm send") }; var cancelSend = new Button { Content = T("取消", "Cancel") };
        AutomationProperties.SetAutomationId(confirmSend, "pairing-confirm-send"); confirmation.Children.Add(confirmSend); confirmation.Children.Add(cancelSend); root.Children.Add(confirmation);
        var history = new ComboBox { Header = T("测试记录", "Test history"), HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(history);
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; AutomationProperties.SetAutomationId(details, "pairing-trial"); root.Children.Add(details);
        var effects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var effectBoxes = new Dictionary<string, CheckBox>();
        // A wrapping-free vertical list avoids clipped labels at 150% scaling.
        effects.Orientation = Orientation.Vertical;
        foreach (var action in OsdPairingProfile.Actions) { var check = new CheckBox { Content = PairingAction(action) }; AutomationProperties.SetAutomationId(check, "pairing-bind-" + action); effects.Children.Add(check); effectBoxes[action] = check; }
        root.Children.Add(new Expander { Header = T("绑定观察到的动作（可多选）", "Bind observed actions (multiple allowed)"), Content = effects, HorizontalAlignment = HorizontalAlignment.Stretch });
        var notes = new TextBox { Header = T("观察备注", "Observation notes"), MaxLength = 1000 }; root.Children.Add(notes);
        var outcomes = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 }; var outcomeButtons = new List<Button>();
        foreach (var item in new[] { ("success", T("成功", "Success")), ("failure", T("失败", "Failure")), ("other", T("其他效果", "Other effect")), ("uncertain", T("不确定", "Uncertain")) })
        { var b = new Button { Content = item.Item2, Tag = item.Item1 }; AutomationProperties.SetAutomationId(b, "pairing-result-" + item.Item1); outcomes.Children.Add(b); outcomeButtons.Add(b); }
        root.Children.Add(outcomes);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; AutomationProperties.SetAutomationId(status, "pairing-status"); root.Children.Add(status);
        var dialog = new ContentDialog { Title = T("本地 OSD 配对", "Local OSD pairing"), XamlRoot = Root.XamlRoot, CloseButtonText = T("关闭", "Close"), Content = new ScrollViewer { Content = root, MaxHeight = 490, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        monitorAdaptationDialog = dialog;
        bool Active() => open && !closed && generation == version && displayDevices.Contains(device) && ReferenceEquals(monitorAdaptationDialog, dialog);
        void SaveLocal()
        {
            session.Connection = connection.Text.Trim(); session.Hdr = (string)((ComboBoxItem)hdr.SelectedItem).Tag; session.Notes = environment.Text.Trim(); store.Save(profile);
        }
        void Update()
        {
            var ready = !busy && baselineReady && Active();
            foreach (var item in basicButtons) item.Value.IsEnabled = ready && (item.Key != "osd-disable-events" || canDisableEvents);
            sendCandidate.IsEnabled = ready && accessible.IsChecked == true && experimental.IsChecked == true && candidates.SelectedItem is OsdCommand candidate && (!candidate.DisablesEvents || canDisableEvents);
            add.IsEnabled = ready && profile.Commands.Count < 64;
            foreach (var item in remoteButtons) { var command = profile.BoundCommand(item.Key); item.Value.IsEnabled = ready && command is not null && (!command.DisablesEvents || canDisableEvents); }
            foreach (var b in outcomeButtons) b.IsEnabled = ready && selectedTrial is not null && selectedTrial.SourceProfileId.Length == 0;
            effects.IsEnabled = ready && selectedTrial?.WriteSucceeded == true; notes.IsEnabled = ready;
            observe.IsEnabled = ready && profile.EventCaptures.Count < 32 && !string.IsNullOrWhiteSpace(eventLabel.Text);
            export.IsEnabled = import.IsEnabled = save.IsEnabled = rePair.IsEnabled = ready;
            confirmSend.IsEnabled = ready && pendingConfirmation is not null; cancelSend.IsEnabled = !busy;
            candidates.IsEnabled = history.IsEnabled = context.IsEnabled = accessible.IsEnabled = experimental.IsEnabled = reuse.IsEnabled = !busy;
            bindingsText.Text = string.Join("\n", profile.Bindings.Select(b => PairingAction(b.Action) + " → " + profile.Commands.Single(c => c.Id == b.CommandId).Display + " · " + PairingContext(b.Context)));
        }
        void ShowTrial(OsdTrial? trial)
        {
            selectedTrial = trial; foreach (var item in effectBoxes) item.Value.IsChecked = trial?.Effects.Contains(item.Key) == true; notes.Text = trial?.Notes ?? "";
            details.Text = trial is null ? "" : trial.Command.Display + "\n" + (trial.WriteSucceeded ? T("已发送；请观察实际效果", "Sent; check the actual effect") : T("发送失败", "Send failed")) +
                (trial.SentValue is uint sent ? $" · 0x{sent:X4}" : "") + "\n" + trial.Error + "\n" + trial.Before?.Display + " → " + trial.After?.Display + "\n" + PairingOutcome(trial.Outcome);
            Update();
        }
        void ReloadLists()
        {
            var selected = candidates.SelectedItem as OsdCommand;
            candidates.ItemsSource = profile.Commands.ToArray(); candidates.SelectedItem = selected ?? profile.Commands.FirstOrDefault();
            history.Items.Clear();
            foreach (var trial in profile.Trials.AsEnumerable().Reverse()) history.Items.Add(new ComboBoxItem { Content = trial.At.ToLocalTime().ToString("HH:mm:ss") + " · " + trial.Command.Display, Tag = trial });
            if (history.Items.Count > 0) history.SelectedIndex = 0;
            Update();
        }
        async Task Run(Func<CancellationToken, Task> operation, bool hardware = true)
        {
            if (!Active() || busy) return; busy = true; Update();
            try
            {
                if (hardware)
                {
                    await gate.WaitAsync(lifetime.Token);
                    try { if (Active()) await operation(lifetime.Token); }
                    finally { gate.Release(); }
                }
                else await operation(lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Active()) status.Text = ex.Message; else StartupLog.Write("OSD pairing: " + ex.Message); }
            finally { busy = false; if (Active()) Update(); }
        }
        void Start(Func<CancellationToken, Task> operation, bool hardware = true) { if (!busy && Active()) osdPairingOperation = Run(operation, hardware); }
        async Task Send(OsdCommand command, CancellationToken token)
        {
            if (profile.Trials.Count >= 512) throw new InvalidOperationException(T("配对记录已满，请先导出记录再重新配对。", "Pairing history is full. Export it before pairing again."));
            if (command.DisablesEvents && !canDisableEvents) throw new InvalidOperationException("MCCS 2.2 required.");
            var menuContext = (string)((ComboBoxItem)context.SelectedItem).Tag; var allowReuse = reuse.IsChecked == true;
            var trial = await Task.Run(() => engine.Send(command, session.Id, menuContext, allowReuse, token));
            profile.Trials.Add(trial); SaveLocal(); // Keep a completed write even when the window closes.
            if (Active()) { ReloadLists(); ShowTrial(trial); details.StartBringIntoView(); status.Text = T("已记录发送结果。勾选实际动作，再选择成功或失败。", "Send result recorded. Select the observed actions, then choose success or failure."); }
        }
        void RequestSend(OsdCommand command)
        {
            if (!Active() || busy || !baselineReady) return;
            if (command.DisablesEvents)
            { pendingConfirmation = command; confirmation.Visibility = Visibility.Visible; confirmation.StartBringIntoView(); Update(); return; }
            pendingConfirmation = null; confirmation.Visibility = Visibility.Collapsed; Start(t => Send(command, t));
        }
        foreach (var entry in basicButtons) { var id = entry.Key; entry.Value.Click += (_, _) => RequestSend(profile.Commands.Single(c => c.Id == id)); }
        foreach (var entry in remoteButtons) { var action = entry.Key; entry.Value.Click += (_, _) => { if (profile.BoundCommand(action) is { } c) RequestSend(c); }; }
        confirmSend.Click += (_, _) => { if (pendingConfirmation is not { } command || busy) return; pendingConfirmation = null; confirmation.Visibility = Visibility.Collapsed; Start(t => Send(command, t)); };
        cancelSend.Click += (_, _) => { pendingConfirmation = null; confirmation.Visibility = Visibility.Collapsed; Update(); };
        candidates.SelectionChanged += (_, _) => { experimental.IsChecked = false; source.Text = (candidates.SelectedItem as OsdCommand)?.Source ?? ""; Update(); };
        accessible.Checked += (_, _) => Update(); accessible.Unchecked += (_, _) => Update(); experimental.Checked += (_, _) => Update(); experimental.Unchecked += (_, _) => Update(); eventLabel.TextChanged += (_, _) => Update();
        sendCandidate.Click += (_, _) => { if (candidates.SelectedItem is OsdCommand c && sendCandidate.IsEnabled) { RequestSend(c); experimental.IsChecked = false; } };
        add.Click += (_, _) =>
        {
            try
            {
                if (!OsdPairingStore.TryHex(codeBox.Text, 255, out var code) || !OsdPairingStore.TryHex(valueBox.Text, 65535, out var value)) throw new InvalidDataException(T("请输入有效的十六进制代码和值。", "Enter a valid hexadecimal code and value."));
                var command = new OsdCommand { Name = nameBox.Text.Trim(), Code = (byte)code, Value = value, Source = sourceBox.Text.Trim(), SourceModel = identity.ModelId }; command.Validate();
                profile.Commands.Add(command); SaveLocal(); ReloadLists(); candidates.SelectedItem = command;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        history.SelectionChanged += (_, _) => ShowTrial((history.SelectedItem as ComboBoxItem)?.Tag as OsdTrial);
        foreach (var b in outcomeButtons) b.Click += (_, _) =>
        {
            if (selectedTrial is null || busy) return;
            try
            {
                var outcome = (string)b.Tag;
                profile.Confirm(selectedTrial, outcome, outcome is "success" or "other" ? effectBoxes.Where(x => x.Value.IsChecked == true).Select(x => x.Key) : Array.Empty<string>(), notes.Text.Trim());
                SaveLocal(); ShowTrial(selectedTrial); status.Text = T("观察与绑定已保存到本机。", "Observation and bindings saved locally.");
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        observe.Click += (_, _) => Start(async token =>
        {
            using var capture = CancellationTokenSource.CreateLinkedTokenSource(token); observationCancellation = capture; stop.IsEnabled = true;
            try
            {
                var label = eventLabel.Text.Trim(); var result = await Task.Run(() => engine.Observe(session.Id, label, capture.Token));
                profile.EventCaptures.Add(result); SaveLocal();
                if (Active()) eventText.Text = string.Join("\n", result.Samples.Select(s => s.At.ToLocalTime().ToString("HH:mm:ss.fff") + " · " + s.Display));
            }
            finally { observationCancellation = null; if (Active()) stop.IsEnabled = false; }
        });
        stop.Click += (_, _) => observationCancellation?.Cancel();
        save.Click += (_, _) => { try { profile.Revision++; SaveLocal(); status.Text = T("配对已保存在本机，可离线使用。", "Pairing saved locally and available offline."); } catch (Exception ex) { status.Text = ex.Message; } };
        rePair.Click += (_, _) =>
        {
            try
            {
                SaveLocal();
                File.WriteAllText(Path.Combine(directory, "archive-" + Guid.NewGuid().ToString("N") + ".json"), OsdPairingStore.Serialize(profile));
                var commands = profile.Commands.Select(c => c.Copy()).ToList();
                profile = OsdPairingProfile.Create(device, uiTest); profile.Commands = commands; profile.Sessions.Add(session);
                selectedTrial = null; pendingConfirmation = null; confirmation.Visibility = Visibility.Collapsed;
                SaveLocal(); ReloadLists(); ShowTrial(null); navigation.SelectedIndex = 0;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        export.Click += (_, _) => Start(async _ =>
        {
            SaveLocal(); var json = OsdPairingStore.Serialize(profile, includeRaw.IsChecked == true);
            var picker = new FileSavePicker { SuggestedFileName = "FluentControl-" + device.ModelId + "-osd-pairing" };
            picker.FileTypeChoices.Add("OSD pairing JSON", new List<string> { ".json" }); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file is not null && Active()) { await FileIO.WriteTextAsync(file, json); status.Text = T("配对文件已导出，没有上传。", "Pairing file exported. Nothing was uploaded."); }
        }, false);
        import.Click += (_, _) => Start(async _ =>
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null || !Active()) return;
            if ((await file.GetBasicPropertiesAsync()).Size > OsdPairingStore.MaxBytes) throw new InvalidDataException("OSD pairing file is too large.");
            var json = await FileIO.ReadTextAsync(file); if (!Active()) return;
            profile.ImportCandidates(OsdPairingStore.Parse(json)); SaveLocal(); ReloadLists(); status.Text = T("已导入候选与记录。请在本机测试后绑定。", "Candidates and history imported. Test locally before binding.");
        }, false);
        dialog.Opened += (_, _) => Start(async token =>
        {
            status.Text = T("正在读取原始参数…", "Reading raw parameters…");
            session.Baseline = await Task.Run(() => uiTest ? UiTestData.CaptureDiagnostics(device, null, token, null) : monitors!.CaptureDiagnostics(device, null, token));
            // Prime live button fields, never from saved diagnostics.
            await Task.Run(() => engine.Read(0xCA));
            baselineReady = true; SaveLocal(); if (Active()) { ReloadLists(); status.Text = T("基线已保存在本机，可以开始测试。", "Baseline saved locally. Ready to test."); }
        });
        dialog.Closing += (_, _) => { open = false; lifetime.Cancel(); };
        Update();
        try { await dialog.ShowAsync(); }
        catch (Exception ex) { if (!closed) ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            open = false; lifetime.Cancel(); await osdPairingOperation;
            try { if (baselineReady) SaveLocal(); } catch (Exception ex) { StartupLog.Write("OSD pairing save: " + ex.Message); }
            if (ReferenceEquals(monitorAdaptationDialog, dialog)) monitorAdaptationDialog = null;
        }
    }
}
