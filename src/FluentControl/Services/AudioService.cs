using NAudio.CoreAudioApi;
namespace FluentControl.Services;

public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly List<MMDevice> devices = new();

    public List<ControlChannel> Enumerate()
    {
        var channels = new List<ControlChannel>();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var console = DefaultId(flow, Role.Console);
            var multimedia = DefaultId(flow, Role.Multimedia);
            var communications = DefaultId(flow, Role.Communications);
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                devices.Add(device);
                try
                {
                    var volume = device.AudioEndpointVolume;
                    var isDefault = device.ID == console || device.ID == multimedia;
                    var isCommunication = device.ID == communications;
                    var labels = new List<string> { flow == DataFlow.Render ? "声音输出" : "麦克风输入" };
                    if (isDefault) labels.Add("默认设备");
                    if (isCommunication) labels.Add("默认通话");
                    channels.Add(new ControlChannel
                    {
                        Name = device.FriendlyName, DeviceId = device.ID,
                        Detail = string.Join(" · ", labels),
                        IsDefaultAudio = isDefault || isCommunication,
                        Glyph = flow == DataFlow.Render ? "\uE767" : "\uE720",
                        Value = volume.MasterVolumeLevelScalar * 100,
                        IsMuted = volume.Mute,
                        Read = () => volume.MasterVolumeLevelScalar * 100,
                        Write = value => volume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100, 0, 1),
                        WriteMute = value => volume.Mute = value
                    });
                }
                catch (Exception ex) { StartupLog.Write("Audio endpoint unavailable: " + ex.Message); }
            }
        }
        return channels;
    }

    private string? DefaultId(DataFlow flow, Role role)
    {
        try { using var device = enumerator.GetDefaultAudioEndpoint(flow, role); return device.ID; }
        catch { return null; } // A PC can legitimately have no default endpoint for a role.
    }

    public void Dispose()
    {
        foreach (var device in devices) device.Dispose();
        devices.Clear();
        enumerator.Dispose();
    }
}
