namespace FluentControl.Services;

internal enum SystemAudioTarget { Output, Input }
internal sealed record SystemAudioValue(double Volume, bool Muted);

// The UI and profiles know only these two roles, never endpoint names or IDs.
internal interface ISystemAudioBackend : IDisposable
{
    event Action? Changed;
    SystemAudioValue Read(SystemAudioTarget target);
    SystemAudioValue SetVolume(SystemAudioTarget target, double volume);
    SystemAudioValue SetMute(SystemAudioTarget target, bool muted);
}

internal sealed class SystemAudioControls
{
    internal const string OutputId = "system-output";
    internal const string InputId = "system-input";
    internal const string OutputKey = "audio/system-output/volume";
    internal const string InputKey = "audio/system-input/volume";
    private readonly ISystemAudioBackend backend;
    internal List<ControlChannel> Channels { get; }

    internal SystemAudioControls(ISystemAudioBackend backend)
    {
        this.backend = backend;
        Channels = new() { Create(SystemAudioTarget.Output), Create(SystemAudioTarget.Input) };
        Refresh();
    }

    private ControlChannel Create(SystemAudioTarget target)
    {
        ControlChannel channel = null!;
        SystemAudioValue Apply(Func<SystemAudioValue> operation)
        {
            try
            {
                var state = operation();
                if (!double.IsFinite(state.Volume) || state.Volume < 0 || state.Volume > 100)
                    throw new InvalidDataException("Invalid system audio volume");
                channel.Value = state.Volume; channel.IsMuted = state.Muted; channel.IsAvailable = true;
                return state;
            }
            catch (Exception ex)
            {
                channel.IsAvailable = false;
                throw new InvalidOperationException(target == SystemAudioTarget.Output
                    ? Strings.T("音量不可用，请检查 Windows 声音设置。", "Volume is unavailable. Check Windows sound settings.")
                    : Strings.T("输入不可用，请检查 Windows 声音设置。", "Input is unavailable. Check Windows sound settings."), ex);
            }
        }
        channel = new()
        {
            Name = target == SystemAudioTarget.Output ? Strings.T("音量", "Volume") : Strings.T("输入", "Input"),
            DeviceId = target == SystemAudioTarget.Output ? OutputId : InputId,
            Detail = "", Glyph = target == SystemAudioTarget.Output ? "\uE767" : "\uE720",
            Read = () => Apply(() => backend.Read(target)).Volume,
            ReadMute = () => Apply(() => backend.Read(target)).Muted,
            Write = value => Apply(() => backend.SetVolume(target, Math.Clamp(value, 0, 100))),
            WriteMute = value => Apply(() => backend.SetMute(target, value))
        };
        return channel;
    }

    // A missing input must not prevent output control, or the reverse. Refresh
    // never applies a saved value when a new default endpoint appears.
    internal bool Refresh()
    {
        var changed = false;
        foreach (var channel in Channels)
        {
            var before = (channel.Value, channel.IsMuted, channel.IsAvailable);
            try { channel.Read!(); } catch (InvalidOperationException) { }
            changed |= before != (channel.Value, channel.IsMuted, channel.IsAvailable);
        }
        return changed;
    }

    internal static bool IsLegacyKey(string key) => key.StartsWith("audio/", StringComparison.Ordinal)
        && key != OutputKey && key != InputKey;

    internal static bool NeedsProfileUpdate(ControlProfile profile) => profile.Values.Keys.Any(IsLegacyKey)
        && (!profile.Values.ContainsKey(OutputKey) || !profile.Values.ContainsKey(InputKey));

    internal static void MigrateDesktopRows(AppSettings settings)
    {
        // Legacy endpoint keys do not encode a documented input/output role.
        // Keep both system controls visible instead of guessing from device IDs.
        List<string> Normalize(List<string> rows) => rows.SelectMany(key => IsLegacyKey(key)
            ? new[] { OutputKey, InputKey } : new[] { key }).Distinct().ToList();
        settings.DesktopRows = Normalize(settings.DesktopRows ?? new());
        if (settings.DesktopIndividualRows is not null) settings.DesktopIndividualRows = Normalize(settings.DesktopIndividualRows);
        if (settings.DesktopLinkedRows is not null) settings.DesktopLinkedRows = Normalize(settings.DesktopLinkedRows);
    }
}
