using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
namespace FluentControl.Services;
internal static class DeviceLinkActivation
{
    private static string SessionPipe => "FluentControl.DeviceLink." + System.Diagnostics.Process.GetCurrentProcess().SessionId + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
    internal static string? Argument() => Environment.GetCommandLineArgs().FirstOrDefault(x => DeviceSignals.LinkIdentity(x) is not null);
    internal static async Task SendAsync(string link)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", SessionPipe, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var data = Encoding.UTF8.GetBytes(link + "\n"); await pipe.WriteAsync(data, timeout.Token);
    }
    internal static async Task ListenAsync(Action<string> accepted, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(SessionPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var data = new byte[513]; int count = 0;
                while (count < data.Length)
                {
                    int got = await pipe.ReadAsync(data.AsMemory(count, 1), timeout.Token);
                    if (got == 0 || data[count] == '\n') break;
                    count++;
                }
                if (count <= 512)
                {
                    var link = Encoding.UTF8.GetString(data, 0, count);
                    if (DeviceSignals.LinkIdentity(link) is not null) accepted(link);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(300, cancellation); }
        }
    }
}
