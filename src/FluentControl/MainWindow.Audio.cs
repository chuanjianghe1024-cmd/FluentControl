using FluentControl.Services;
using Microsoft.UI.Xaml;

namespace FluentControl;

public sealed partial class MainWindow
{
    private SystemAudioControls? systemAudio;
    private readonly DispatcherTimer audioRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private List<ControlChannel> LoadSystemAudio()
    {
        audio = uiTest ? new UiTestData.AudioBackend() : new AudioService();
        audio.Changed += QueueSystemAudioRefresh;
        systemAudio = new SystemAudioControls(audio);
        return systemAudio.Channels;
    }

    // Native callbacks only post work; COM reads, unregister and disposal never
    // run inside a Core Audio notification callback.
    private void QueueSystemAudioRefresh() => DispatcherQueue.TryEnqueue(() =>
    {
        if (closed) return;
        audioRefreshTimer.Stop(); audioRefreshTimer.Start();
    });

    private async Task RefreshSystemAudioAsync()
    {
        audioRefreshTimer.Stop();
        if (closed) return;
        if (refreshing || pendingWrites > 0 || desktopPanel?.HasPendingChanges == true)
        { audioRefreshTimer.Start(); return; }
        await gate.WaitAsync();
        try
        {
            if (closed || refreshing || systemAudio is null) return;
            if (await Task.Run(systemAudio.Refresh)) MarkProfileModified();
            if (!closed) SynchronizeValues();
        }
        finally { gate.Release(); }
    }

    // Caller holds gate. Saving captures current Windows levels, including
    // changes made outside FC before a queued notification has been processed.
    private async Task ReadAudioForProfileAsync()
    {
        if (systemAudio is not null) await Task.Run(systemAudio.Refresh);
        if (!closed) SynchronizeValues();
    }
}
