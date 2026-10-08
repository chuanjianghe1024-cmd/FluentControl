using Microsoft.Win32;
namespace FluentControl.Services;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FluentControl";
    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command(), StringComparison.OrdinalIgnoreCase);
    }
    private static string Command() => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.")}\" --background";
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command(), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
