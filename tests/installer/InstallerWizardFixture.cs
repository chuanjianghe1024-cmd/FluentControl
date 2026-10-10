// Disposable Windows CI driver. Uses the real MSI UI and installed executable;
// it is never shipped in the application. Run only in a disposable user profile.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

internal static class InstallerWizardFixture
{
    private static string target;
    private static string output;
    private static Process installer;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 7) return 87;
        target = Path.GetFullPath(args[1]).TrimEnd('\\');
        output = args[2];
        bool upgrade = args[3] == "upgrade";
        bool initialStartup = args[4] == "1";
        bool finalStartup = args[5] == "1";
        bool launch = args[6] == "1";
        try
        {
            installer = Process.Start(new ProcessStartInfo("msiexec.exe", "/i \"" + args[0] +
                "\" /norestart /l*v \"" + output + ".log\"") { UseShellExecute = false });
            Invoke(Wait(ControlType.Button, "下一步", 30));
            var startup = Wait(ControlType.CheckBox, "随 Windows 启动", 15);
            Assert(IsChecked(startup) == initialStartup, "The wizard must reflect actual startup state.");
            var folder = Wait(ControlType.Edit, null, 10);
            if (upgrade)
            {
                Assert(!folder.Current.IsEnabled, "Upgrade directory must be locked.");
                Assert(SamePath(((ValuePattern)folder.GetCurrentPattern(ValuePattern.Pattern)).Current.Value, target),
                    "Upgrade must display the previous installation directory.");
            }
            else
            {
                Assert(folder.Current.IsEnabled, "Fresh installation directory must be editable.");
                Invoke(Wait(ControlType.Button, "浏览", 10));
                var browserEdit = Wait(ControlType.Edit, null, 10);
                ((ValuePattern)browserEdit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(target);
                Invoke(Wait(ControlType.Button, "确定", 10));
                // The directory picker and the inline path editor must agree.
                folder = Wait(ControlType.Edit, null, 10);
                Assert(SamePath(((ValuePattern)folder.GetCurrentPattern(ValuePattern.Pattern)).Current.Value, target),
                    "Browse must update the installation path.");
            }
            SetChecked(Wait(ControlType.CheckBox, "随 Windows 启动", 10), finalStartup);
            Capture("options");
            Invoke(Wait(ControlType.Button, "上一步", 10));
            Invoke(Wait(ControlType.Button, "下一步", 10));
            Assert(IsChecked(Wait(ControlType.CheckBox, "随 Windows 启动", 10)) == finalStartup,
                "Back/Next must preserve the chosen startup state.");
            Assert(SamePath(((ValuePattern)Wait(ControlType.Edit, null, 10).GetCurrentPattern(ValuePattern.Pattern)).Current.Value, target),
                "Back/Next must preserve the installation path.");
            Invoke(Wait(ControlType.Button, "下一步", 10));
            Invoke(Wait(ControlType.Button, "安装", 10));
            var finishOption = Wait(ControlType.CheckBox, "立即启动 FluentControl", 90);
            Assert(IsChecked(finishOption), "Launch on Finish should initially be selected.");
            Assert(FindApp() == null, "The application must not start before Finish.");
            SetChecked(finishOption, launch);
            Capture("complete");
            Invoke(Wait(ControlType.Button, "完成", 10));
            Assert(installer.WaitForExit(30000) && installer.ExitCode == 0, "MSI wizard did not succeed.");
            if (launch)
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                bool ready = false;
                while (DateTime.UtcNow < deadline)
                {
                    var app = FindApp();
                    if (app != null)
                    {
                        app.Refresh();
                        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentControl", "Logs", "startup.log");
                        if (app.MainWindowHandle != IntPtr.Zero && File.Exists(log) &&
                            File.ReadAllText(log).Contains("[PID " + app.Id + "] Main window activated")) { ready = true; break; }
                    }
                    Thread.Sleep(200);
                }
                Assert(ready, "Finish must launch the app from the selected path with a visible window.");
            }
            else
            {
                Thread.Sleep(2000);
                Assert(FindApp() == null, "Unchecked Finish must not launch the app.");
            }
            Console.WriteLine("PASS: real MSI wizard ({0}), path={1}, startup={2}, launch={3}.",
                upgrade ? "upgrade" : "fresh", target, finalStartup, launch);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Capture("failed");
            DumpWindows();
            return 1;
        }
        finally
        {
            // Only this fixture's installed app may be stopped. The MSI's
            // unrelated-process and real-file-lock checks run separately.
            var app = FindApp();
            if (app != null) { app.Kill(); app.WaitForExit(5000); }
            if (installer != null && !installer.HasExited) installer.Kill();
        }
    }

    private static bool SamePath(string left, string right)
    { return string.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }

    private static Process FindApp()
    {
        foreach (var app in Process.GetProcessesByName("FluentControl"))
        {
            try { if (SamePath(app.MainModule.FileName, Path.Combine(target, "FluentControl.exe"))) return app; }
            catch (InvalidOperationException) { }
        }
        return null;
    }

    private static AutomationElement Wait(ControlType type, string name, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            // Modal folder browsers are usually the last root window. Ignore
            // disabled background wizard controls while a child dialog is open.
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!windows[i].Current.Name.Contains("FluentControl") || !windows[i].Current.IsEnabled) continue;
                    var controls = windows[i].FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, type));
                    for (int j = controls.Count - 1; j >= 0; j--)
                    {
                        var value = controls[j].Current;
                        if (!value.IsOffscreen && (name == null || value.Name.Replace("&", "").StartsWith(name, StringComparison.Ordinal)) &&
                            (value.IsEnabled || type == ControlType.Edit)) return controls[j];
                    }
                }
                catch (ElementNotAvailableException) { }
            }
            Thread.Sleep(150);
        }
        throw new TimeoutException("MSI control was not found: " + type.ProgrammaticName + " / " + name);
    }

    private static bool IsChecked(AutomationElement element)
    { return ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On; }

    private static void SetChecked(AutomationElement element, bool value)
    { if (IsChecked(element) != value) ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle(); }

    private static void Invoke(AutomationElement element)
    {
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(300);
    }

    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Capture(string stage)
    {
        try
        {
            var size = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using (var bitmap = new Bitmap(size.Width, size.Height))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(size.Location, Point.Empty, size.Size);
                bitmap.Save(output + "-" + stage + ".png", ImageFormat.Png);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("Screenshot: " + ex.Message); }
    }

    private static void DumpWindows()
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement window in windows)
        {
            try
            {
                Console.Error.WriteLine("Window: " + window.Current.Name);
                if (!window.Current.Name.Contains("FluentControl")) continue;
                foreach (AutomationElement item in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    Console.Error.WriteLine(item.Current.ControlType.ProgrammaticName + " | " + item.Current.Name + " | " + item.Current.IsEnabled);
            }
            catch (ElementNotAvailableException) { }
        }
    }
}
