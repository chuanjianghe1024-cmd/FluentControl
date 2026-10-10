using System.Diagnostics;
using System.Net.Http;
using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private static readonly HttpClient updateHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private ReleaseUpdates releaseUpdates = new(updateHttp);
    private CancellationTokenSource? updateCancellation;
    private ReleaseUpdate? availableUpdate;
    private string? downloadedUpdate;
    private enum UpdateStage { Idle, Checking, Current, Available, None, Downloading, Ready, Cancelled, Failed, Installing }
    private UpdateStage updateStage;
    private UpdateFailure? updateFailure;
    private double updatePercent;
    private Button? checkUpdateButton, downloadUpdateButton, installUpdateButton, cancelUpdateButton;
    private TextBlock? updateStatus;
    private ProgressBar? updateProgress;
    private HyperlinkButton? releaseNotesLink;

    private void BuildUpdateSettings(StackPanel section)
    {
        section.Children.Add(new TextBlock { Text = "FluentControl · " + ReleaseUpdates.CurrentVersion.ToString(3), FontSize = 24, TextWrapping = TextWrapping.Wrap });
        section.Children.Add(Empty(T("手动检查 GitHub 最新正式版，下载后由你选择安装。", "Check GitHub for the latest stable release, then choose when to install.")));
        checkUpdateButton = new Button { Content = T("检查更新", "Check for updates") };
        downloadUpdateButton = new Button { Content = T("下载更新", "Download update") };
        installUpdateButton = new Button { Content = T("退出并安装", "Exit and install") };
        cancelUpdateButton = new Button { Content = T("取消", "Cancel") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(checkUpdateButton, "check-updates");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(downloadUpdateButton, "download-update");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(installUpdateButton, "install-update");
        checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        downloadUpdateButton.Click += async (_, _) => await DownloadUpdateAsync();
        installUpdateButton.Click += async (_, _) => await InstallUpdateAsync();
        cancelUpdateButton.Click += (_, _) => updateCancellation?.Cancel();
        // Vertical actions also fit high DPI/narrow windows and longer translations.
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var button in new[] { checkUpdateButton, downloadUpdateButton, installUpdateButton, cancelUpdateButton }) actions.Children.Add(button);
        section.Children.Add(actions);
        updateProgress = new ProgressBar { Minimum = 0, Maximum = 100 };
        updateStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(updateStatus, "update-status");
        section.Children.Add(updateProgress); section.Children.Add(updateStatus);
        releaseNotesLink = new HyperlinkButton { Content = T("版本说明", "Release notes"), HorizontalAlignment = HorizontalAlignment.Left };
        section.Children.Add(releaseNotesLink);
        section.Children.Add(new HyperlinkButton { Content = T("GitHub 项目与反馈", "GitHub project & feedback"), NavigateUri = new Uri(ReleaseUpdates.Repository) });
        RenderUpdateState();
    }
    private void RenderUpdateState()
    {
        if (closed || checkUpdateButton is null) return;
        var busy = updateCancellation is not null;
        var newer = availableUpdate is not null && ReleaseUpdates.IsNewer(availableUpdate.Version, ReleaseUpdates.CurrentVersion);
        checkUpdateButton.IsEnabled = !busy;
        downloadUpdateButton!.Visibility = newer && availableUpdate!.Installer is not null && downloadedUpdate is null ? Visibility.Visible : Visibility.Collapsed;
        downloadUpdateButton.IsEnabled = !busy;
        installUpdateButton!.Visibility = downloadedUpdate is not null ? Visibility.Visible : Visibility.Collapsed;
        installUpdateButton.IsEnabled = !busy;
        cancelUpdateButton!.Visibility = busy && updateStage != UpdateStage.Installing ? Visibility.Visible : Visibility.Collapsed;
        updateProgress!.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        updateProgress.IsIndeterminate = updateStage != UpdateStage.Downloading;
        updateProgress.Value = updatePercent;
        releaseNotesLink!.Visibility = availableUpdate is not null ? Visibility.Visible : Visibility.Collapsed;
        releaseNotesLink.NavigateUri = availableUpdate?.Page;
        updateStatus!.Text = updateStage switch
        {
            UpdateStage.Checking => T("正在检查更新…", "Checking for updates…"),
            UpdateStage.Current => T("没有更新的正式版本。", "No newer stable release is available."),
            UpdateStage.Available => F("发现新版本 {0}", "New version available: {0}", availableUpdate!.Tag) +
                (availableUpdate.Installer is null ? "\n" + T("此版本没有可校验的 x64 安装包，请查看版本说明。", "This release has no verifiable x64 installer. See the release notes.") : ""),
            UpdateStage.None => T("尚未找到正式版本。", "No stable release was found."),
            UpdateStage.Downloading => F("正在下载：{0}%", "Downloading: {0}%", Math.Round(updatePercent)),
            UpdateStage.Ready => T("下载及校验完成。安装时将退出应用，个人配置会保留。", "Download verified. Installation will close the app and preserve personal settings."),
            UpdateStage.Cancelled => T("已取消，可重新尝试。", "Cancelled. You can try again."),
            UpdateStage.Installing => T("正在准备安装…", "Preparing installation…"),
            UpdateStage.Failed => updateFailure switch
            {
                UpdateFailure.RateLimited => T("GitHub 请求受限，请稍后重试。", "GitHub requests are limited. Please try again later."),
                UpdateFailure.InvalidRelease => T("版本信息无法验证，请稍后重试。", "Release information could not be verified. Try again later."),
                UpdateFailure.Integrity => T("安装包校验失败，请重新下载。", "Installer verification failed. Please download again."),
                _ => T("更新操作失败，请检查网络或下载目录后重试。", "Update failed. Check your connection or download folder and try again.")
            },
            _ => T("仅在点击时检查更新。", "Updates are checked only when you click.")
        };
    }
    private async Task CheckForUpdatesAsync()
    {
        if (closed || updateCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(); updateCancellation = cancellation;
        updateStage = UpdateStage.Checking; updateFailure = null; availableUpdate = null; downloadedUpdate = null; RenderUpdateState();
        try
        {
            availableUpdate = await releaseUpdates.CheckAsync(cancellation.Token);
            updateStage = availableUpdate is null ? UpdateStage.None : ReleaseUpdates.IsNewer(availableUpdate.Version, ReleaseUpdates.CurrentVersion) ? UpdateStage.Available : UpdateStage.Current;
        }
        catch (OperationCanceledException) { updateStage = UpdateStage.Cancelled; }
        catch (Exception ex) { UpdateFailed(ex); }
        finally { updateCancellation = null; RenderUpdateState(); }
    }
    private async Task DownloadUpdateAsync()
    {
        if (closed || updateCancellation is not null || availableUpdate?.Installer is null || !ReleaseUpdates.IsNewer(availableUpdate.Version, ReleaseUpdates.CurrentVersion)) return;
        using var cancellation = new CancellationTokenSource(); updateCancellation = cancellation;
        updateStage = UpdateStage.Downloading; updatePercent = 0; downloadedUpdate = null; RenderUpdateState();
        try
        {
            var progress = new Progress<double>(value => { if (!closed && updateCancellation == cancellation) { updatePercent = value; RenderUpdateState(); } });
            downloadedUpdate = await releaseUpdates.DownloadAsync(availableUpdate, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "Updates"), progress, cancellation.Token);
            updateStage = UpdateStage.Ready;
        }
        catch (OperationCanceledException) { updateStage = UpdateStage.Cancelled; }
        catch (Exception ex) { UpdateFailed(ex); }
        finally { updateCancellation = null; RenderUpdateState(); }
    }
    private async Task InstallUpdateAsync()
    {
        if (closed || uiTest || updateCancellation is not null || downloadedUpdate is null || availableUpdate?.Installer is null) return;
        using var cancellation = new CancellationTokenSource(); updateCancellation = cancellation;
        updateStage = UpdateStage.Installing; RenderUpdateState();
        try
        {
            await WaitForWritesAsync();
            if (closed || !SaveState()) { updateStage = UpdateStage.Ready; return; }
            await ReleaseUpdates.VerifyAsync(downloadedUpdate, availableUpdate.Installer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(downloadedUpdate) { UseShellExecute = true });
            if (process is null) throw new IOException("Installer did not start.");
            exitRequested = true; Close();
        }
        catch (OperationCanceledException) { updateStage = UpdateStage.Cancelled; }
        catch (Exception ex) { downloadedUpdate = null; UpdateFailed(ex); }
        finally { updateCancellation = null; RenderUpdateState(); }
    }
    private void UpdateFailed(Exception exception)
    {
        updateStage = UpdateStage.Failed;
        updateFailure = (exception as UpdateException)?.Failure;
        StartupLog.Write("Manual update failed: " + exception);
    }
}
