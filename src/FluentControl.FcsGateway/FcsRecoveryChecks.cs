using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
internal static class FcsRecoveryChecks
{
    public static void Run()
    {
        var origin = "http://127.0.0.1:12345";
        if (!FcsManager.Authorized("127.0.0.1:12345", origin, "Bearer test", origin, "test") ||
            FcsManager.Authorized("evil.example:12345", origin, "Bearer test", origin, "test") ||
            FcsManager.Authorized("127.0.0.1:12345", "https://evil.example", "Bearer test", origin, "test") ||
            FcsManager.Authorized("127.0.0.1:12345", origin, null, origin, "test"))
            throw new Exception("Manager origin/authorization boundary failed.");
        var path = Path.Combine(Path.GetTempPath(), "fcs-package-test-" + Guid.NewGuid().ToString("N") + ".zip");
        var data = new byte[65536]; data[0] = 0xe9;
        void Write(string chip = "esp32c3", string? hash = null, bool traversal = false)
        {
            File.Delete(path); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            void Entry(string name, byte[] value) { using var s = zip.CreateEntry(name).Open(); s.Write(value); }
            Entry("firmware.bin", data); Entry("LICENSE-MicroPython.txt", Array.Empty<byte>()); Entry("README.txt", Array.Empty<byte>());
            Entry("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { format = "fcs.recovery/2", identityFormat = "FCSID001", identityOffset = 0x3ff000, identitySize = 4096, chip, board = "fcs-c3-bh1750-gpio4-3",
                flashBytes = 4194304, offset = 0, core = "frozen-mpy", size = data.Length, sha256 = hash ?? Convert.ToHexString(SHA256.HashData(data)) }));
            if (traversal) Entry("../outside", data);
        }
        void Reject() { try { using var p = FcsRecovery.ReadPackage(path); } catch (InvalidDataException) { return; } throw new Exception("Invalid package accepted."); }
        try
        {
            Write(); using var p = FcsRecovery.ReadPackage(path);
            if (!p.Firmware.SequenceEqual(data)) throw new Exception("Recovery image changed.");
            Write(chip: "esp32s3"); Reject(); Write(hash: new string('0',64)); Reject(); Write(traversal: true); Reject();
        }
        finally { File.Delete(path); }
    }
}
