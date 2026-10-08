using Microsoft.UI.Xaml;
namespace FluentControl;
public partial class App : Application
{
    private Window? window;
    private Mutex? instance;
    public App()
    {
        StartupLog.Write("App constructor");
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => StartupLog.Write("XAML resource: " + e.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupLog.Write("Unhandled exception: " + e.ExceptionObject);
        UnhandledException += (_, e) => StartupLog.Write("XAML exception: " + e.Exception);
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            StartupLog.ReportFailure(ex);
            throw;
        }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var name = Environment.GetCommandLineArgs().Contains("--ui-test") ? @"Local\FluentControl.UiTest" : @"Local\FluentControl";
            instance = new Mutex(true, name, out var firstInstance);
            if (!firstInstance) { Services.ShellIntegration.SignalExisting(); instance.Dispose(); instance = null; Exit(); return; }
            StartupLog.Write("Creating main window");
            var main = new MainWindow();
            window = main;
            window.Closed += (_, _) => { instance?.ReleaseMutex(); instance?.Dispose(); instance = null; };
            if (!main.TryStartInBackground()) window.Activate();
            StartupLog.Write("Main window activated");
        }
        catch (Exception ex)
        {
            StartupLog.ReportFailure(ex);
            Exit();
        }
    }
}
