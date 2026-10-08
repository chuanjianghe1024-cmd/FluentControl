using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FluentControl.Services;

public static class MouseService
{
    private const uint SaveAndNotify = 3;
    // Windows' cursor-size SPI action is not in the public SDK contract. If an OS
    // rejects it, restore the previous settings and expose the native Settings link.
    private const uint SetCursorBaseSize = 0x2029;
    private const string CursorKey = @"Control Panel\Cursors";
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool GetParameter(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SetParameter(uint action, uint parameter, nint value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendNotifyMessageW")]
    private static extern bool Notify(nint window, uint message, nint parameter, string section);

    public static int GetSpeed()
    {
        if (!GetParameter(0x70, 0, out var value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }
    public static void SetSpeed(double value)
    {
        if (!SetParameter(0x71, 0, (nint)(int)Math.Clamp(Math.Round(value), 1, 20), SaveAndNotify))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置鼠标速度。");
    }
    public static int GetPointerBaseSize()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorKey);
        return key?.GetValue("CursorBaseSize") is int value ? value : 32;
    }
    public static double GetPointerSize() => Math.Clamp((GetPointerBaseSize() - 32) / 16d + 1, 1, 15);

    public static void SetPointerSize(double value)
    {
        var level = (int)Math.Clamp(Math.Round(value), 1, 15);
        var size = 32 + (level - 1) * 16;
        using var cursors = Registry.CurrentUser.CreateSubKey(CursorKey);
        using var accessibility = Registry.CurrentUser.CreateSubKey(AccessibilityKey);
        var oldBase = cursors.GetValue("CursorBaseSize");
        var oldLevel = accessibility.GetValue("CursorSize");
        var oldBaseKind = oldBase is null ? RegistryValueKind.DWord : cursors.GetValueKind("CursorBaseSize");
        var oldLevelKind = oldLevel is null ? RegistryValueKind.DWord : accessibility.GetValueKind("CursorSize");
        try
        {
            cursors.SetValue("CursorBaseSize", size, RegistryValueKind.DWord);
            accessibility.SetValue("CursorSize", level, RegistryValueKind.DWord);
            if (!SetParameter(SetCursorBaseSize, 0, (nint)size, SaveAndNotify))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "此系统未接受指针大小设置，可点击“Windows 指针设置”调整。");
            NotifySettings();
        }
        catch
        {
            SetParameter(SetCursorBaseSize, 0, (nint)(oldBase is int previous ? previous : 32), SaveAndNotify);
            Restore(cursors, "CursorBaseSize", oldBase, oldBaseKind);
            Restore(accessibility, "CursorSize", oldLevel, oldLevelKind);
            NotifySettings();
            throw;
        }
    }
    private static void Restore(RegistryKey key, string name, object? value, RegistryValueKind kind)
    {
        if (value is null) key.DeleteValue(name, false); else key.SetValue(name, value, kind);
    }
    private static void NotifySettings()
    {
        Notify((nint)0xffff, 0x1a, 0, CursorKey);
        Notify((nint)0xffff, 0x1a, 0, AccessibilityKey);
    }
    public static List<ControlChannel> Enumerate() => new()
    {
        new() { Name = "鼠标速度", Detail = "慢 1 — 快 20 · Windows 系统速度", Glyph = "\uE962", PropertyKey = "mouse-speed", Minimum = 1, Maximum = 20, Unit = "", Value = GetSpeed(), Read = () => GetSpeed(), Write = SetSpeed },
        new() { Name = "指针大小", Detail = "小 1 — 大 15 · 保留指针颜色与主题", Glyph = "\uE8B0", PropertyKey = "pointer-size", Minimum = 1, Maximum = 15, Unit = "", Value = GetPointerSize(), Read = GetPointerSize, Write = SetPointerSize }
    };
}
