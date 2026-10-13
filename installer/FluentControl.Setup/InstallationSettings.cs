using System.IO;
using Microsoft.Win32;
namespace FluentControl.Setup;

internal sealed record InstallationSettings(string Directory, bool ExistingDirectory, bool Startup)
{
    internal static InstallationSettings Read()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var registration = hive.OpenSubKey(@"Software\FluentControl\Installer");
        var oldDirectory = registration?.GetValue("InstallDirectory") as string;
        using var run = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var legacy = run?.GetValue("FluentControl") as string;
        var startup = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "FluentControl.lnk")) ||
                      legacy?.Contains(" --background", StringComparison.OrdinalIgnoreCase) == true;
        return new(oldDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FluentControl"),
            !string.IsNullOrWhiteSpace(oldDirectory), startup);
    }

    internal static string ValidateDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
            value.StartsWith(@"\\", StringComparison.Ordinal) || value.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new IOException(Strings.T("Choose a writable local folder."));
        var full = Path.GetFullPath(value.Trim());
        if (full.TrimEnd('\\').Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new IOException(Strings.T("Choose a writable local folder."));
        if (File.Exists(full)) throw new IOException(Strings.T("Choose a writable local folder."));
        // Probe the nearest existing parent. Do not leave a selected directory
        // behind when the user cancels before the MSI transaction begins.
        var parent = full;
        while (!System.IO.Directory.Exists(parent))
            parent = Path.GetDirectoryName(parent) ?? throw new IOException(Strings.T("Choose a writable local folder."));
        var probe = Path.Combine(parent, ".fluentcontrol-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return full;
    }
}
