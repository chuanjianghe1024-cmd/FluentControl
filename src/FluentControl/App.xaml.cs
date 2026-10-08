using Microsoft.UI.Xaml;
namespace FluentControl;
public partial class App : Application
{
    private Window? window;
    public App()
    {
        StartupLog.Write("App constructor");
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
            StartupLog.Write("Creating main window");
            window = new MainWindow();
            window.Activate();
            StartupLog.Write("Main window activated");
        }
        catch (Exception ex)
        {
            StartupLog.ReportFailure(ex);
            Exit();
        }
    }
}
