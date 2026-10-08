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
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            devices.Add(device);
            try
            {
                var volume = device.AudioEndpointVolume;
                channels.Add(new ControlChannel
                {
                    Name = device.FriendlyName,
                    Detail = flow == DataFlow.Render ? "声音输出 · 设备音量" : "麦克风 · 输入音量",
                    Glyph = flow == DataFlow.Render ? "\uE767" : "\uE720",
                    Value = volume.MasterVolumeLevelScalar * 100,
                    Read = () => volume.MasterVolumeLevelScalar * 100,
                    Write = value => volume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100, 0, 1),
                    ReadMute = () => volume.Mute,
                    WriteMute = value => volume.Mute = value
                });
            }
            catch { /* Endpoints without volume control are omitted. */ }
        }
        return channels;
    }
    public void Dispose()
    {
        foreach (var device in devices) device.Dispose();
        devices.Clear();
        enumerator.Dispose();
    }
}
