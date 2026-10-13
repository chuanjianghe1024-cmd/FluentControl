using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WixToolset.BootstrapperApplicationApi;
namespace FluentControl.Setup;

internal sealed class SetupBootstrapper : BootstrapperApplication
{
    private IBootstrapperCommand command = null!;
    private App app = null!;
    private SetupWindow window = null!;
    private Dispatcher dispatcher = null!;
    private bool present, newerVersion, rollback, finished;
    private volatile bool cancelled;
    private int result = 1602;
    private LaunchAction action;
    private string lastError = "";
    internal bool Busy { get; private set; }
    internal bool CanLaunch { get; private set; }
    internal string Version => engine.GetVariableVersion("WixBundleVersion");
    internal bool Interactive => command.Display == Display.Full;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        command = args.Command;
    }

    protected override void Run()
    {
        try
        {
            Strings.SetLanguage("auto");
            dispatcher = Dispatcher.CurrentDispatcher;
            app = new App();
            app.InitializeComponent();
            window = new SetupWindow(this);
            window.Closed += (_, _) => app.Shutdown();
            if (command.Display is Display.Full or Display.Passive) window.Show();
            engine.CloseSplashScreen();
            if (command.Action == LaunchAction.Help)
            {
                window.ShowCompleted(false, Strings.T("Use /quiet, /repair or /uninstall. Installation is offline and per user."));
                result = 0;
                finished = true;
                if (!Interactive) app.Dispatcher.BeginInvoke(() => app.Shutdown());
            }
            else if (command.Scope == BundleScope.PerMachine ||
                     command.Action is LaunchAction.Layout or LaunchAction.Cache or LaunchAction.UnsafeUninstall)
            {
                Ui(() => Complete(unchecked((int)0x80070057), Strings.T("This operation is not supported. Use install, repair or uninstall.")));
            }
            else
            {
                HookEvents();
                engine.Detect();
            }
            app.Run();
        }
        catch (Exception ex)
        {
            engine.Log(LogLevel.Error, ex.ToString());
            result = 1603;
        }
        engine.Quit(result);
    }

    private void Ui(Action callback) => dispatcher.BeginInvoke(callback);

    private void HookEvents()
    {
        DetectPackageComplete += (_, e) => { if (e.PackageId == "FluentControlMsi") present = e.State == PackageState.Present; };
        DetectRelatedMsiPackage += (_, e) =>
        {
            if (e.PackageId == "FluentControlMsi" && engine.CompareVersions(e.Version, Version) > 0) newerVersion = true;
        };
        DetectRelatedBundle += (_, e) => { if (engine.CompareVersions(e.Version, Version) > 0) newerVersion = true; };
        DetectComplete += (_, e) => Ui(() =>
        {
            if (e.Status < 0) { Complete(e.Status); return; }
            if (newerVersion && command.Action != LaunchAction.Uninstall)
            { Complete(unchecked((int)0x80070666), Strings.T("A newer version is already installed.")); return; }
            var settings = InstallationSettings.Read();
            var uninstall = command.Action == LaunchAction.Uninstall;
            window.ShowReady(present, uninstall, settings);
            if (!Interactive)
                Start(uninstall ? LaunchAction.Uninstall : command.Action == LaunchAction.Repair || present ? LaunchAction.Repair : LaunchAction.Install,
                    settings.Directory, settings.Startup);
        });
        PlanMsiPackage += (_, e) =>
        {
            // Burn owns progress/cancellation; never display the traditional MSI wizard.
            e.UiLevel = INSTALLUILEVEL.None;
            e.DisableExternalUiHandler = false;
        };
        PlanComplete += (_, e) => Ui(() =>
        {
            if (e.Status < 0 || cancelled) { Complete(e.Status < 0 ? e.Status : unchecked((int)0x80070642)); return; }
            // Burn requires a real owner HWND even for /quiet. EnsureHandle
            // creates the hidden native window without showing the WPF UI.
            engine.Apply(new WindowInteropHelper(window).EnsureHandle());
        });
        Progress += (_, e) =>
        {
            e.Cancel = cancelled && !rollback;
            Ui(() => window.ShowProgress(e.OverallPercentage, rollback));
        };
        CacheAcquireProgress += (_, e) => e.Cancel = cancelled;
        CacheVerifyProgress += (_, e) => e.Cancel = cancelled;
        ExecutePackageBegin += (_, e) => { rollback = !e.ShouldExecute; e.Cancel = cancelled && !rollback; };
        ExecuteProgress += (_, e) => e.Cancel = cancelled && !rollback;
        Error += (_, e) =>
        {
            lastError = e.ErrorMessage;
            engine.Log(LogLevel.Error, $"Setup error {e.ErrorCode}: {e.ErrorMessage}");
            // No automatic retry loops, ignored errors, or process termination.
            e.Result = Result.Cancel;
        };
        ExecuteFilesInUse += (_, e) =>
        {
            e.Result = Result.Cancel;
            if (!Interactive) return;
            dispatcher.Invoke(() =>
            {
                var message = Strings.T("Close the listed applications yourself, then choose OK to retry. Cancel rolls back installation.") +
                    Environment.NewLine + string.Join(Environment.NewLine, e.Files);
                if (System.Windows.MessageBox.Show(window, message, Strings.T("Files in use"), MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
                    e.Result = Result.Retry;
            });
        };
        ApplyComplete += (_, e) =>
        {
            e.Action = BOOTSTRAPPER_APPLYCOMPLETE_ACTION.None; // never reboot automatically
            Ui(() =>
            {
                CanLaunch = e.Status >= 0 && e.Restart == ApplyRestart.None && action == LaunchAction.Install && Interactive && !cancelled;
                Complete(e.Status, e.Restart != ApplyRestart.None ? Strings.T("Restart Windows to finish. The app will not be started now.") : null,
                    e.Restart != ApplyRestart.None);
            });
        };
    }

    internal void Start(LaunchAction requested, string directory, bool startup)
    {
        if (Busy || finished) return;
        try
        {
            var actual = InstallationSettings.Read();
            if (requested != LaunchAction.Uninstall)
            {
                // Re-read just before planning; another installer cannot silently
                // move stable components between directories.
                directory = InstallationSettings.ValidateDirectory(actual.ExistingDirectory ? actual.Directory : directory);
                engine.SetVariableString("InstallFolder", directory, false);
                engine.SetVariableString("StartWithWindows", startup ? "1" : "0", false);
            }
            action = requested;
            Busy = true;
            window.ShowWorking();
            engine.Plan(requested, BundleScope.Default);
        }
        catch (Exception ex)
        {
            engine.Log(LogLevel.Error, ex.ToString());
            if (!Interactive || Busy) Complete(unchecked((int)0x80070057), ex.Message);
            else window.ShowInputError(ex.Message);
        }
    }

    internal void Cancel()
    {
        if (!Busy) { window.Close(); return; }
        cancelled = true;
        window.ShowCancelling();
    }

    private void Complete(int status, string? detail = null, bool restart = false)
    {
        Busy = false;
        finished = true;
        result = status < 0 ? ((status & unchecked((int)0xffff0000)) == unchecked((int)0x80070000) ? status & 0xffff : status) : restart ? 3010 : 0;
        var succeeded = status >= 0;
        if (!succeeded) CanLaunch = false;
        var text = detail ?? (succeeded ? Strings.T("Your settings and monitor profiles are preserved.") : result == 1602
            ? Strings.T("Installation was cancelled. No application was started.")
            : Strings.F("Error {0}. Open the installation log for details.", $"0x{status:X8}") + "\n" + lastError);
        window.ShowCompleted(succeeded, text, action == LaunchAction.Uninstall);
        if (!Interactive) window.Close();
    }

    internal void Launch()
    {
        if (!CanLaunch) return;
        var path = Path.Combine(InstallationSettings.Read().Directory, "FluentControl.exe");
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
            window.Close();
        }
        catch (Exception ex) { window.ShowInputError(ex.Message); }
    }

    internal void OpenLog()
    {
        try
        {
            var path = engine.GetVariableString("WixBundleLog");
            if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { window.ShowInputError(ex.Message); }
    }
}
