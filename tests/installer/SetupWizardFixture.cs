// Real Burn bundle / WPF UI automation. Disposable Windows CI only.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Automation;

internal static class SetupWizardFixture
{
    private static string output;
    private static Process process;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 7) return 87;
        output = args[2];
        var mode = args[3];
        try
        {
            process = Process.Start(new ProcessStartInfo(args[0], "/norestart /log \"" + output + ".log\"") { UseShellExecute = false });
            var primary = Wait("Primary", true, 60);
            Select("Language", 2); // assertions use English; controls use stable automation IDs
            var folder = Wait("InstallFolder", false, 10);
            var path = (ValuePattern)folder.GetCurrentPattern(ValuePattern.Pattern);
            var locked = mode == "upgrade" || mode == "maintenance";
            Assert(path.Current.IsReadOnly == locked, "Existing directory must be locked; fresh directory must be editable.");
            if (locked) Assert(SamePath(path.Current.Value, args[1]), "Previous installation path not preserved.");
            else path.SetValue(args[1]);
            var startup = (TogglePattern)Wait("StartWithWindows", true, 10).GetCurrentPattern(TogglePattern.Pattern);
            Assert((startup.Current.ToggleState == ToggleState.On) == (args[4] == "1"), "Startup must reflect current link, not stale registry flags.");
            if ((startup.Current.ToggleState == ToggleState.On) != (args[5] == "1")) startup.Toggle();
            Select("Theme", 1); Capture("light");
            Select("Theme", 2); Capture("dark");
            Select("Language", 5); Capture("german");
            Select("Language", 0); Capture("chinese");
            Select("Language", 2);
            Assert(SamePath(((ValuePattern)Wait("InstallFolder", false, 10).GetCurrentPattern(ValuePattern.Pattern)).Current.Value, args[1]), "Theme/language changes lost the selected directory.");
            if (mode == "cancel")
            {
                Invoke(Wait("Close", true, 10));
                Assert(process.WaitForExit(30000) && process.ExitCode == 1602, "Cancel must exit with 1602.");
                return 0;
            }
            if (mode == "maintenance")
            {
                Invoke(Wait("Uninstall", true, 10));
                Assert(Wait("Heading", false, 10).Current.Name == "Uninstall FluentControl?", "Uninstall needs a confirmation page.");
            }
            Invoke(Wait("Primary", true, 10));
            var deadline = DateTime.UtcNow.AddSeconds(120);
            bool done = false;
            while (DateTime.UtcNow < deadline)
            {
                var heading = Wait("Heading", false, 10).Current.Name;
                if (heading == "All set" || heading == "Uninstalled") { done = true; break; }
                if (heading == "Installation did not finish") throw new InvalidOperationException("Setup reported failure. See Burn log.");
                Thread.Sleep(200);
            }
            Assert(done, "Setup did not complete.");
            Assert(Process.GetProcessesByName("FluentControl").Length == 0, "App must not launch before clicking Launch.");
            Capture("complete");
            if (args[6] == "1") Invoke(Wait("Primary", true, 10));
            else Invoke(Wait("Close", true, 10));
            Assert(process.WaitForExit(30000) && process.ExitCode == 0, "Bundle exit code must be successful.");
            if (args[6] == "1")
            {
                deadline = DateTime.UtcNow.AddSeconds(30);
                bool visible = false;
                while (DateTime.UtcNow < deadline)
                {
                    foreach (var app in Process.GetProcessesByName("FluentControl"))
                        if (app.MainWindowHandle != IntPtr.Zero && SamePath(app.MainModule.FileName, Path.Combine(args[1], "FluentControl.exe"))) visible = true;
                    if (visible) break;
                    Thread.Sleep(200);
                }
                Assert(visible, "Launch must open the installed application.");
            }
            else Assert(Process.GetProcessesByName("FluentControl").Length == 0, "Close/uninstall must not launch the application.");
            Console.WriteLine("PASS: Fluent setup " + mode);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Capture("failed"); return 1; }
    }
    private static AutomationElement Wait(string id, bool enabled, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try
                {
                    if (window.Current.AutomationId != "SetupWindow") continue;
                    var item = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                    if (item != null && !item.Current.IsOffscreen && (!enabled || item.Current.IsEnabled)) return item;
                }
                catch (ElementNotAvailableException) { }
            }
            Thread.Sleep(150);
        }
        throw new TimeoutException("Setup control: " + id);
    }
    private static void Invoke(AutomationElement item) { ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Thread.Sleep(120); }
    private static void Select(string id, int index)
    {
        var box = Wait(id, true, 10);
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Thread.Sleep(200);
        var items = box.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        ((SelectionItemPattern)items[index].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Thread.Sleep(200);
    }
    private static bool SamePath(string a, string b) { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Capture(string name)
    {
        try
        {
            Thread.Sleep(250);
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using (var image = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                image.Save(output + "-" + name + ".png", ImageFormat.Png);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
    }
}
