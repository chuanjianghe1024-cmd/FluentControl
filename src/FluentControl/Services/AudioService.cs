using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;
namespace FluentControl.Services;

internal sealed class AudioService : ISystemAudioBackend, IMMNotificationClient
{
    private sealed record Endpoint(string Id, MMDevice Device, AudioEndpointVolume Volume);
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Dictionary<SystemAudioTarget, Endpoint> endpoints = new();
    private readonly object lifecycle = new();
    private volatile bool disposed;
    public event Action? Changed;

    internal AudioService()
    {
        try { Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(this)); }
        catch { enumerator.Dispose(); throw; }
    }

    // Resolve the current system default for every action, even when its change
    // notification has not reached the UI yet. Never change Windows routing.
    private Endpoint Current(SystemAudioTarget target)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        MMDevice device;
        try
        {
            device = enumerator.GetDefaultAudioEndpoint(
                target == SystemAudioTarget.Output ? DataFlow.Render : DataFlow.Capture, Role.Console);
        }
        catch { Release(target); throw; }
        try
        {
            var id = device.ID;
            if (endpoints.TryGetValue(target, out var current) && current.Id == id)
            { device.Dispose(); return current; }
            Release(target);
            var endpoint = new Endpoint(id, device, device.AudioEndpointVolume);
            endpoint.Volume.OnVolumeNotification += OnVolumeChanged;
            endpoints[target] = endpoint;
            return endpoint;
        }
        catch { device.Dispose(); throw; }
    }

    private static SystemAudioValue Snapshot(Endpoint endpoint) =>
        new(endpoint.Volume.MasterVolumeLevelScalar * 100d, endpoint.Volume.Mute);

    public SystemAudioValue Read(SystemAudioTarget target)
    {
        lock (lifecycle)
        {
            try { return Snapshot(Current(target)); }
            catch
            {
                // A driver may invalidate a handle without changing its ID.
                // Reopen and retry this read once; writes are never replayed.
                Release(target);
                return Snapshot(Current(target));
            }
        }
    }
    public SystemAudioValue SetVolume(SystemAudioTarget target, double volume)
    {
        if (!double.IsFinite(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        lock (lifecycle)
        {
            var endpoint = Current(target);
            try
            {
                endpoint.Volume.MasterVolumeLevelScalar = (float)(Math.Clamp(volume, 0, 100) / 100);
                return Snapshot(endpoint);
            }
            catch { Release(target); Notify(); throw; }
        }
    }
    public SystemAudioValue SetMute(SystemAudioTarget target, bool muted)
    {
        lock (lifecycle)
        {
            var endpoint = Current(target);
            try { endpoint.Volume.Mute = muted; return Snapshot(endpoint); }
            catch { Release(target); Notify(); throw; }
        }
    }

    private void Release(SystemAudioTarget target)
    {
        if (!endpoints.Remove(target, out var endpoint)) return;
        endpoint.Volume.OnVolumeNotification -= OnVolumeChanged;
        try { endpoint.Device.Dispose(); }
        catch (Exception ex) { StartupLog.Write("Audio endpoint release: " + ex.Message); }
    }

    private void Notify()
    {
        // Subscribers only enqueue UI work. Do not lock or call Core Audio here.
        if (!disposed)
        {
            try { Changed?.Invoke(); }
            catch { /* The UI dispatcher can be gone during shutdown. */ }
        }
    }
    private void OnVolumeChanged(AudioVolumeNotificationData _) => Notify();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    { if (role == Role.Console && flow is DataFlow.Render or DataFlow.Capture) Notify(); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify();
    public void OnDeviceAdded(string deviceId) => Notify();
    public void OnDeviceRemoved(string deviceId) => Notify();
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            Changed = null;
            try { Marshal.ThrowExceptionForHR(enumerator.UnregisterEndpointNotificationCallback(this)); }
            catch (Exception ex) { StartupLog.Write("Audio notification release: " + ex.Message); }
            foreach (var target in endpoints.Keys.ToArray()) Release(target);
            enumerator.Dispose();
        }
    }
}
