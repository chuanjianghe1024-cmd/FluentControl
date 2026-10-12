using System.Diagnostics;
using System.Text.Json;

namespace FluentControl.Services;

public sealed class DdcCommunicationException : IOException
{
    public bool WriteMayHaveExecuted { get; }
    public DdcCommunicationException(string message, bool writeMayHaveExecuted, Exception? inner = null) : base(message, inner)
    { WriteMayHaveExecuted = writeMayHaveExecuted; }
}

// One small native host per logical display connection. A blocked display cannot
// stall another host or leave native handles owned by the UI process.
public sealed class DdcProcessClient : IDisposable
{
    private readonly Process process;
    private readonly object sync = new();
    private readonly TimeSpan timeout;
    private bool stopped;
    public bool IsStopped => stopped;
    public DdcProcessClient(ProcessStartInfo? start = null, TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(8);
        if (start is null)
        {
            start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FluentControl.DdcHost.exe"));
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var parent = Process.GetCurrentProcess();
            start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true;
        process = Process.Start(start) ?? throw new IOException("Cannot start DDC host.");
    }

    public DdcResponse Request(DdcRequest request)
    {
        lock (sync)
        {
            if (stopped) throw new IOException(Strings.T("显示器通信已中止，请重新检测。", "Display communication stopped. Detect displays again."));
            try
            {
                return ExchangeAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or InvalidDataException)
            {
                Stop(); // Never restart/replay a request, including an ambiguously completed write.
                throw new DdcCommunicationException(Strings.T("显示器通信超时或中断，请重新检测；刚才的指令不会自动重发。", "Display communication timed out or ended. Detect again; the last command will not be resent."),
                    request.Operation is "write" or "temperature-write", ex);
            }
        }
    }

    private async Task<DdcResponse> ExchangeAsync(DdcRequest request)
    {
        using var deadline = new CancellationTokenSource(timeout);
        var token = deadline.Token;
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        // Bounded framing even if a damaged worker produces no newline.
        var line = new System.Text.StringBuilder();
        var buffer = new char[1];
        while (line.Length <= 256 * 1024)
        {
            if (await process.StandardOutput.ReadAsync(buffer.AsMemory(), token) == 0) throw new EndOfStreamException();
            if (buffer[0] == '\n') return JsonSerializer.Deserialize<DdcResponse>(line.ToString()) ?? throw new InvalidDataException();
            line.Append(buffer[0]);
        }
        throw new InvalidDataException("DDC response is too large.");
    }

    private void Stop()
    {
        if (stopped) return;
        stopped = true;
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(150)) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { }
        // No parent-owned physical handle is destroyed while a native call is running.
        // A driver may delay process termination; no subsequent request uses this host.
        process.Dispose();
    }
    public void Dispose() { lock (sync) Stop(); }
}
