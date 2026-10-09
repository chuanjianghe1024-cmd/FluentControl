using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private int monitorPlaceholderCount;
    private readonly List<Action> monitorSynchronizers = new();
    private void HideUnavailable_Click(object sender, RoutedEventArgs e)
    {
        state.Settings.HideUnavailableMonitorControls = HideUnavailable.IsChecked == true;
        SaveState(); RenderMonitorControls(generation);
    }
    private void RenderMonitorControls(int version)
    {
        foreach (var sync in monitorSynchronizers) refreshRows.Remove(sync);
        monitorSynchronizers.Clear(); monitorPlaceholderCount = 0;
        var start = refreshRows.Count;
        MonitorRows.Children.Clear(); CombinedRows.Children.Clear();
        foreach (var display in displayDevices) MonitorRows.Children.Add(CreateMonitorCard(display, version));
        CombinedRows.Children.Add(Empty(T("所有已连接的显示器一起调节；仅向支持该功能或选项的屏幕发送命令。", "Adjust all connected displays together. Commands are sent only to displays supporting the feature or option.")));
        if (displayDevices.Count == 1) CombinedRows.Children.Add(CreateMonitorCard(displayDevices[0], version, true));
        else if (displayDevices.Count > 1)
        {
            var devices = displayDevices.ToArray();
            var body = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
            body.Children.Add(new TextBlock { Text = T("整体控制", "Overall control"), FontSize = 20, TextWrapping = TextWrapping.Wrap });
            var sameModel = devices.All(d => ModelIdentity.IsValid(d.ModelId)) && devices.Select(d => d.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            body.Children.Add(ModelBadge(sameModel ? F("同型号 · {0} 台联动", "Same model · {0} linked displays", devices.Length) : F("跨型号 · {0} 台联动", "Across models · {0} linked displays", devices.Length)));
            foreach (var device in devices)
            {
                var menu = new Button { Content = MonitorTitle(device) + " · " + device.Model + " · " + T("屏幕菜单", "On-screen menu") };
                menu.Click += (_, _) => ShowMonitorOsd(device); body.Children.Add(menu);
            }
            AddFeatureSections(body, devices, version, true); CombinedRows.Children.Add(Card(body));
        }
        var mappings = new StackPanel { Spacing = 8 };
        foreach (var display in displayDevices) mappings.Children.Add(CreateMappingRow(display));
        CombinedRows.Children.Add(new Expander { Header = T("多屏亮度匹配", "Match display brightness"), Content = mappings, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        monitorSynchronizers.AddRange(refreshRows.Skip(start));
    }
    private void AddFeatureSections(StackPanel body, IReadOnlyList<MonitorDevice> devices, int version, bool linked)
    {
        foreach (var category in VcpCatalog.All.Select(x => x.Category).Append("extensions").Distinct())
        {
            var rows = new StackPanel { Spacing = 6 };
            var definitions = category == "extensions" ? devices.SelectMany(x => x.Features).Where(x => x.Definition.Category == category).Select(x => x.Definition).DistinctBy(x => x.Code) : VcpCatalog.All.Where(x => x.Category == category);
            foreach (var definition in definitions)
            {
                var entries = devices.Select(d => (Device: d, Feature: d.Features.FirstOrDefault(f => f.Definition.Code == definition.Code) ?? new MonitorFeature { Definition = definition, Channel = d.Channels.FirstOrDefault(c => c.PropertyKey == definition.Key), Reason = T("不支持或当前无法读取", "Unsupported or currently unreadable") })).ToArray();
                var targets = entries.Where(x => x.Feature.Channel is not null).Select(x => x.Feature.Channel!).ToArray();
                if (state.Settings.HideUnavailableMonitorControls && targets.Length == 0) continue;
                if (!linked || definition.Confirm || definition.Kind == VcpKind.ReadOnly || targets.Length == 0)
                {
                    foreach (var entry in entries)
                    {
                        if (state.Settings.HideUnavailableMonitorControls && entry.Feature.Channel is null) continue;
                        rows.Children.Add(FeatureRow(entry.Device, entry.Feature, version, linked));
                    }
                }
                else
                {
                    var first = targets[0];
                    var options = MonitorLinking.Options(targets)?.Select(option =>
                    {
                        var supported = devices.Where(d => d.Channels.Any(c => c.PropertyKey == definition.Key && c.Options?.Any(o => o.Value == option.Value) == true)).ToArray();
                        return supported.Length == devices.Count ? option : new ControlOption(option.Value, option.Label + " · " + F("仅 {0}", "Only {0}", string.Join(", ", supported.Select(MonitorTitle))));
                    }).ToArray();
                    var aggregate = new ControlChannel { Name = definition.Name, Detail = SupportSummary(definition.Key, devices) + $" · VCP 0x{definition.Code:X2}", Glyph = first.Glyph, PropertyKey = first.PropertyKey, Minimum = first.Minimum, Maximum = first.Maximum, Unit = first.Unit, Options = options, Write = _ => { } };
                    var aggregateRow = CreateRow(aggregate, version, targets);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(aggregateRow, "linked-" + definition.Key);
                    rows.Children.Add(aggregateRow);
                }
            }
            if (category == "color")
                foreach (var device in devices)
                    foreach (var channel in device.Channels.Where(x => x.PropertyKey == "temperature" && !x.CompatibilityOnly))
                    {
                        if (linked) rows.Children.Add(new TextBlock { Text = device.DisplayName, FontSize = 12 });
                        rows.Children.Add(WithPartialSupport(CreateRow(channel, version), channel.PropertyKey));
                    }
            if (category == "extensions" && !state.Settings.HideUnavailableMonitorControls)
                rows.Children.Add(SettingsRow(T("厂商 SDK", "Vendor SDK"), T("硬件准星、FPS、私有游戏功能：预留，尚未接入", "Hardware crosshairs, FPS and private game features: reserved, not connected"), new Button { Content = T("待接入", "Not connected"), IsEnabled = false }));
            if (rows.Children.Count > 0)
                body.Children.Add(new Expander { Header = VcpCatalog.Category(category), IsExpanded = category == "picture", Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }
    private static Border ModelBadge(string text) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4),
        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }
    };
    private string SingleModelLabel(MonitorDevice device) => !ModelIdentity.IsValid(device.ModelId) ? T("型号未识别 · 独立调节", "Unknown model · independent controls") :
        displayDevices.Count == 1 ? T("单台显示器", "Single display") :
        displayDevices.Any(d => d.Id != device.Id && d.ModelId != device.ModelId) ? T("不同型号 · 独立调节", "Different model · independent controls") : T("单独控制", "Individual");
    private string SupportSummary(string key, IReadOnlyList<MonitorDevice> devices)
    {
        var supported = devices.Where(d => d.Channels.Any(c => c.PropertyKey == key)).ToArray();
        var names = string.Join(", ", supported.Select(MonitorTitle));
        if (supported.Length == devices.Count) return F("全部支持：{0}", "Supported by all: {0}", names);
        var unavailable = string.Join(", ", devices.Where(d => !supported.Contains(d)).Select(MonitorTitle));
        return F("仅 {0} 支持（{1}/{2}）；{3} 不支持或当前不可用", "Only {0} supported ({1}/{2}); {3} unsupported or unavailable", names, supported.Length, devices.Count, unavailable);
    }
    private string PartialSupport(string key)
    {
        var count = displayDevices.Count(d => d.Channels.Any(c => c.PropertyKey == key));
        return count > 0 && count < displayDevices.Count ? SupportSummary(key, displayDevices) : "";
    }
    private FrameworkElement WithPartialSupport(FrameworkElement row, string key)
    {
        var support = PartialSupport(key); if (support.Length == 0) return row;
        var body = new StackPanel { Spacing = 4 }; body.Children.Add(row);
        body.Children.Add(new TextBlock { Text = support, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = .8, Margin = new Thickness(8, 0, 8, 4) });
        return body;
    }
    private FrameworkElement FeatureRow(MonitorDevice device, MonitorFeature feature, int version, bool showDevice)
    {
        var definition = feature.Definition;
        var title = (showDevice ? device.DisplayName + " · " : "") + definition.Name;
        if (feature.Channel is not ControlChannel channel)
        {
            monitorPlaceholderCount++;
            return SettingsRow(title, $"VCP 0x{definition.Code:X2} · " + (feature.Reason.Length > 0 ? feature.Reason : T("只读", "Read only")), new TextBlock { Text = feature.Information.Length > 0 ? feature.Information : "—", Opacity = .6, VerticalAlignment = VerticalAlignment.Center });
        }
        if (!channel.IsAction)
        {
            var row = WithPartialSupport(CreateRow(channel, version), channel.PropertyKey);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(row, "individual-" + device.Id + "-" + channel.PropertyKey);
            if (!showDevice) return row;
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = device.DisplayName, FontSize = 12, Margin = new Thickness(8, 4, 0, 0) }); panel.Children.Add(row); return panel;
        }
        var run = new Button { Content = T("执行…", "Run…") };
        run.Click += async (_, _) =>
        {
            if (!await ConfirmMonitorChangeAsync(channel, new[] { channel })) return;
            run.IsEnabled = false; await gate.WaitAsync();
            try
            {
                if (closed || version != generation) return;
                var errors = await Task.Run(() => ControlOperations.Apply(new[] { channel }, 1));
                ShowStatus(errors.Count == 0 ? T("命令已发送，请刷新读取设置。", "Command sent. Refresh to read the new settings.") : string.Join("; ", errors), errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
            finally { gate.Release(); run.IsEnabled = true; }
        };
        return WithPartialSupport(SettingsRow(title, $"VCP 0x{definition.Code:X2}", run), definition.Key);
    }
    private async Task<bool> ConfirmMonitorChangeAsync(ControlChannel channel, IReadOnlyList<ControlChannel> targets, XamlRoot? dialogRoot = null)
    {
        var message = channel.PropertyKey switch
        {
            "factory-reset" => T("将恢复显示器的出厂设置，当前硬件设置可能丢失。此操作不会保存到场景配置。", "Restore factory settings on the display? Current hardware settings may be lost. This action is never saved in profiles."),
            "osd" => T("此操作可能禁用显示器的实体菜单按键。", "This may disable the display's physical menu buttons."),
            _ => T("切换输入源或电源状态可能断开当前画面与 DDC/CI 连接；之后可能需要用显示器按键切回。", "Changing input or power may disconnect the picture and DDC/CI. You may need the monitor's buttons to switch back.")
        };
        var names = string.Join(", ", displayDevices.Where(d => targets.Any(c => d.Channels.Contains(c))).Select(d => d.DisplayName));
        try { return await new ContentDialog { Title = Channel(channel), Content = names + "\n\n" + message, PrimaryButtonText = T("继续", "Continue"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Close, XamlRoot = dialogRoot ?? Root.XamlRoot }.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); return false; }
    }
    private Task<bool> ConfirmProfileInputAsync(ControlProfile profile)
    {
        var available = AllChannels();
        var inputs = profile.Values.Where(x => available.TryGetValue(x.Key, out var c) && c.PropertyKey == "input" && c.Value != x.Value.Value).Select(x => available[x.Key]).ToArray();
        return inputs.Length == 0 ? Task.FromResult(true) : ConfirmMonitorChangeAsync(inputs[0], inputs);
    }
    private static void BindBrightnessMapping(MonitorDevice device)
    {
        device.Preference.Brightness ??= new();
        try { device.Preference.Brightness.Validate(); } catch { device.Preference.Brightness = new(); }
        foreach (var c in device.Channels.Where(x => x.PropertyKey == "brightness"))
        { c.LinkedToDevice = value => device.Preference.Brightness.ToDevice(value); c.DeviceToLinked = value => device.Preference.Brightness.ToLinked(value); }
    }
    private Dictionary<string, BrightnessMapping> CaptureMappings() => displayDevices.ToDictionary(x => x.Id, x => x.Preference.Brightness.Copy());
    private FrameworkElement CreateMappingRow(MonitorDevice device)
    {
        var edit = new Button { Content = T("调整映射", "Edit mapping"), IsEnabled = preferences is not null };
        edit.Click += async (_, _) =>
        {
            var source = device.Preference.Brightness;
            var controls = new StackPanel { Spacing = 10, MinWidth = 300 };
            controls.Children.Add(Empty(T("仅影响统一亮度。映射用于手动匹配观感，不代替仪器校色。", "Affects linked brightness only. Match perceived brightness by eye; this is not instrument calibration.")));
            var enabled = new ToggleSwitch { Header = T("启用映射", "Enable mapping"), IsOn = source.Enabled }; controls.Children.Add(enabled);
            NumberBox Number(string header, double value, double min, double max, double step = 1) { var box = new NumberBox { Header = header, Value = value, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact }; controls.Children.Add(box); return box; }
            var min = Number(T("最低亮度", "Minimum brightness"), source.Minimum, 0, 99);
            var max = Number(T("最高亮度", "Maximum brightness"), source.Maximum, 1, 100);
            var offset = Number(T("亮度偏移", "Brightness offset"), source.Offset, -50, 50);
            var curve = Number(T("映射曲线（1 为线性）", "Mapping curve (1 is linear)"), source.Curve, .2, 5, .1);
            var dialog = new ContentDialog { Title = device.DisplayName + " · " + T("亮度映射", "Brightness mapping"), Content = controls, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), XamlRoot = Root.XamlRoot };
            try
            {
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                var mapping = new BrightnessMapping { Enabled = enabled.IsOn, Minimum = min.Value, Maximum = max.Value, Offset = offset.Value, Curve = curve.Value }; mapping.Validate();
                device.Preference.Brightness = mapping;
                try { preferences!.Save(); } catch { device.Preference.Brightness = source; throw; }
                BindBrightnessMapping(device); MarkProfileModified(); SynchronizeValues(); RefreshDesktopPanel();
                ShowStatus(T("亮度映射已保存，下次统一调节时生效。", "Mapping saved. It will be used on the next linked adjustment."), InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        };
        return SettingsRow(device.DisplayName, T("亮度映射", "Brightness mapping"), edit);
    }
}
