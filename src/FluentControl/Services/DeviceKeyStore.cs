using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace FluentControl.Services;

// Per-Windows-user DPAPI; keys never enter subscription links, history, or exported profiles.
public static class DeviceKeyStore
{
    private static string FileFor(string id) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "DeviceKeys", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".key");
    public static void Save(string id, string key)
    {
        if (key.Length != 64 || !key.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid device key.");
        var path = FileFor(id); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp"; File.WriteAllBytes(temp, Protect(Encoding.UTF8.GetBytes(key), true)); File.Move(temp, path, true);
    }
    public static string Read(string id) => Encoding.UTF8.GetString(Protect(File.ReadAllBytes(FileFor(id)), false));
    public static void Remove(string id) => File.Delete(FileFor(id));
    private static byte[] Protect(byte[] data, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = encrypt ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new CryptographicException("Device key storage failed.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != 0) LocalFree(output.Data); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint value);
}
