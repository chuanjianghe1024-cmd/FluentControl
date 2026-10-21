using System.Net;
using System.Net.Http;
using System.Text;
using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
namespace FluentControl;

public sealed partial class MainWindow
{
    private sealed class UpdateUiHandler : HttpMessageHandler
    {
        internal int Requests;
        internal TaskCompletionSource<HttpResponseMessage> Response = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Response.Task.WaitAsync(cancellationToken);
        }
    }
    private async Task CheckSettingsNavigationAsync()
    {
        var before = selectedSettingsSection;
        var previousService = releaseUpdates;
        using var handler = new UpdateUiHandler();
        using var client = new HttpClient(handler);
        releaseUpdates = new ReleaseUpdates(client);
        try
        {
            Navigation.SelectedItem = Navigation.SettingsItem; BuildSettings();
            if (settingsSections.Count != 6 || handler.Requests != 0 || updateCancellation is not null) throw new InvalidOperationException("Opening settings must expose six sections without checking updates.");
            foreach (var id in settingsSections.Keys.ToArray())
            {
                SelectSettingsSection(id); SettingsPanel.UpdateLayout();
                if (settingsSections.Count(x => x.Value.Visibility == Visibility.Visible) != 1 || settingsSections[id].Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Settings sub-navigation did not isolate the selected section: " + id);
            }
            SelectSettingsSection("desktop"); BuildSettings();
            if (selectedSettingsSection != "desktop" || settingsSections["desktop"].Visibility != Visibility.Visible) throw new InvalidOperationException("Rebuilding settings lost the selected section.");
            SelectSettingsSection("about"); SettingsPanel.UpdateLayout();
            if (settingsNavigation!.ActualHeight <= 0 || settingsScroll!.ActualHeight <= 0) throw new InvalidOperationException("Settings navigation or content has no layout space.");
            var peer = new ButtonAutomationPeer(checkUpdateButton!);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            for (var i = 0; i < 40 && handler.Requests == 0; i++) await Task.Delay(25);
            if (handler.Requests != 1 || checkUpdateButton!.IsEnabled || updateStage != UpdateStage.Checking) throw new InvalidOperationException("Manual update button did not start/disable its request.");
            await CheckForUpdatesAsync();
            if (handler.Requests != 1) throw new InvalidOperationException("Duplicate update checks are not blocked.");
            // Refresh/localization can rebuild settings while the request is running.
            SelectSettingsSection("general"); BuildSettings(); SelectSettingsSection("about");
            if (checkUpdateButton!.IsEnabled) throw new InvalidOperationException("Rebuilding settings lost in-flight update state.");
            var json = "{\"tag_name\":\"v99.0.0\",\"html_url\":\"" + ReleaseUpdates.Repository + "/releases/tag/v99.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[]}";
            handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            for (var i = 0; i < 40 && updateCancellation is not null; i++) await Task.Delay(25);
            if (updateStage != UpdateStage.Available || !checkUpdateButton.IsEnabled || releaseNotesLink!.Visibility != Visibility.Visible || downloadUpdateButton!.Visibility != Visibility.Collapsed || installUpdateButton!.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Release UI must retain the notes but not download or install an unverifiable asset.");
            handler.Response = new();
            var cancelled = CheckForUpdatesAsync(); updateCancellation!.Cancel(); await cancelled;
            if (updateStage != UpdateStage.Cancelled || !checkUpdateButton.IsEnabled) throw new InvalidOperationException("Cancelled update check cannot be retried.");
            handler.Response = new();
            var retry = CheckForUpdatesAsync(); handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); await retry;
            if (updateStage != UpdateStage.Failed || updateFailure != UpdateFailure.RateLimited || !checkUpdateButton.IsEnabled) throw new InvalidOperationException("Rate limit failure did not restore manual retry.");
            StartupLog.Write("PASS: six settings sections, selection/layout preservation, manual-only update UI, duplicate prevention, navigation during check, cancellation and retry.");
        }
        finally
        {
            releaseUpdates = previousService; availableUpdate = null; downloadedUpdate = null; updateStage = UpdateStage.Idle; updateFailure = null;
            selectedSettingsSection = before; BuildSettings();
            Navigation.SelectedItem = Navigation.MenuItems[0];
        }
    }
}
