using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private int lightSourceListRevision;

    private void BuildSavedLightSources(StackPanel section)
    {
        var settings = state.Settings.Adaptive;
        section.Children.Add(new TextBlock { Text = F("已添加的设备与来源（{0}）", "Saved devices and sources ({0})", settings.LightSources.Count), TextWrapping = TextWrapping.Wrap });
        foreach (var source in settings.LightSources)
        {
            var row = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 12) };
            AutomationProperties.SetAutomationId(row, "light-source-row-" + source.Id);
            row.Children.Add(new TextBlock { Text = source.Name, FontSize = 16, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(source.DeviceId))
                row.Children.Add(new TextBlock { Text = "ID: " + source.DeviceId, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = source.Url, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var use = new Button { Content = source.Id == settings.ActiveSourceId ? T("当前来源", "Current source") : T("使用此来源", "Use this source"), IsEnabled = source.Id != settings.ActiveSourceId };
            use.Click += (_, _) =>
            {
                if (!settings.LightSources.Contains(source)) return;
                settings.ActiveSourceId = source.Id; settings.Enabled = false;
                SaveAdaptiveSettings(); BuildSettings();
            };
            actions.Children.Add(use);
            var rename = new Button { Content = T("重命名", "Rename") };
            AutomationProperties.SetAutomationId(rename, "light-source-rename-" + source.Id);
            var renameFlyout = new Flyout();
            var edit = new StackPanel { Spacing = 8, Width = 280 };
            var name = new TextBox { Header = T("名称", "Name"), Text = source.Name, MaxLength = 100 };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var save = new Button { Content = T("保存", "Save") };
            AutomationProperties.SetAutomationId(save, "light-source-save-name");
            save.Click += (_, _) =>
            {
                if (!RenameLightSource(source, name.Text))
                { error.Text = T("请输入 1–100 个字符的名称。", "Enter a name of 1–100 characters."); return; }
                renameFlyout.Hide(); BuildSettings();
            };
            edit.Children.Add(name); edit.Children.Add(error); edit.Children.Add(save);
            renameFlyout.Opening += (_, _) => { name.Text = source.Name; error.Text = ""; };
            renameFlyout.Content = edit; rename.Flyout = renameFlyout; actions.Children.Add(rename);
            var remove = new Button { Content = T("删除", "Delete") };
            AutomationProperties.SetAutomationId(remove, "light-source-delete-" + source.Id);
            var deleteFlyout = new Flyout();
            var confirmation = new StackPanel { Spacing = 8, Width = 300 };
            confirmation.Children.Add(new TextBlock { Text = F("从 FC 删除“{0}”？", "Remove “{0}” from FC?", source.Name), TextWrapping = TextWrapping.Wrap });
            confirmation.Children.Add(new TextBlock { Text = T("仅移除这条订阅，保留历史和无线绑定，不重置硬件。删除当前来源会关闭自动调节。", "Removes this subscription only; history and wireless binding are kept, and hardware is not reset. Removing the current source turns off automatic adjustment."), TextWrapping = TextWrapping.Wrap });
            var confirm = new Button { Content = T("删除", "Delete") };
            AutomationProperties.SetAutomationId(confirm, "light-source-confirm-delete");
            confirm.Click += (_, _) => { deleteFlyout.Hide(); if (RemoveLightSource(source)) BuildSettings(); };
            var cancel = new Button { Content = T("取消", "Cancel") };
            cancel.Click += (_, _) => deleteFlyout.Hide();
            confirmation.Children.Add(confirm); confirmation.Children.Add(cancel);
            deleteFlyout.Content = confirmation; remove.Flyout = deleteFlyout; actions.Children.Add(remove);
            row.Children.Add(actions); section.Children.Add(row);
        }
    }

    private bool RenameLightSource(LightSourceSubscription source, string value)
    {
        var name = value.Trim();
        if (!state.Settings.Adaptive.LightSources.Contains(source) || name.Length is < 1 or > 100 || name.Any(char.IsControl)) return false;
        source.Name = name;
        // Do not reset sampling/automation merely because a display label changed.
        SaveState(); return true;
    }

    private bool RemoveLightSource(LightSourceSubscription source)
    {
        var settings = state.Settings.Adaptive;
        if (!settings.LightSources.Remove(source)) return false;
        localDiscovery?.Cancel(); lightSourceListRevision++;
        if (settings.ActiveSourceId == source.Id)
        {
            settings.ActiveSourceId = "simulation"; settings.Enabled = false;
            SaveAdaptiveSettings(); // Cancel/invalidate the removed source's in-flight sample.
        }
        else SaveState(); // Removing an inactive source must not reset the active source's output faults/filter.
        return true;
    }
}
