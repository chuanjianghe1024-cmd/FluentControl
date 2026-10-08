using System.Runtime.InteropServices;

namespace FluentControl;

internal static class StartupLog
{
    private static readonly object Sync = new();
    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluentControl", "Logs", "startup.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:O} [PID {Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch { /* A logging failure must not prevent the window from opening. */ }
    }

    internal static void ReportFailure(Exception exception)
    {
        Write(exception.ToString());
        MessageBoxW(IntPtr.Zero,
            $"FluentControl 启动失败：\n{exception.Message}\n\n启动日志：\n{LogPath}",
            "FluentControl", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
