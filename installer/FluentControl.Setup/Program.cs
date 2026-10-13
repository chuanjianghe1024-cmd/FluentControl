using WixToolset.BootstrapperApplicationApi;
namespace FluentControl.Setup;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Preview never connects to Burn and cannot install anything. Used for
        // theme/DPI screenshots; lifecycle tests launch the actual bundle.
        if (args.FirstOrDefault() == "--preview")
        {
            Strings.SetLanguage(args.Skip(1).FirstOrDefault() ?? "auto");
            var app = new App();
            app.InitializeComponent();
            var window = new SetupWindow(null);
            window.Closed += (_, _) => app.Shutdown();
            window.Show();
            window.ShowReady(false, false, InstallationSettings.Read());
            app.Run();
            return 0;
        }
        ManagedBootstrapperApplication.Run(new SetupBootstrapper());
        return 0;
    }
}
