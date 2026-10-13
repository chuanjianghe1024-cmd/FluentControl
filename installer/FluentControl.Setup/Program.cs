using WixToolset.BootstrapperApplicationApi;
namespace FluentControl.Setup;
internal static class Program
{
    private static int Main(string[] args)
    {
        // Preview never connects to Burn and cannot install anything. Used for
        // theme/DPI screenshots; lifecycle tests launch the actual bundle.
        if (args.FirstOrDefault() == "--preview")
        {
            var thread = new Thread(() =>
            {
                Strings.SetLanguage(args.Skip(1).FirstOrDefault() ?? "auto");
                var app = new App();
                app.InitializeComponent();
                var window = new SetupWindow(null);
                window.Closed += (_, _) => app.Shutdown();
                window.Show();
                window.ShowReady(false, false, InstallationSettings.Read());
                app.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return 0;
        }
        // Burn's native connection initializes COM as MTA. The base BA starts
        // its own STA UI thread; making this entry point STA prevents connection.
        ManagedBootstrapperApplication.Run(new SetupBootstrapper());
        return 0;
    }
}
