using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WixToolset.BootstrapperApplicationApi;
namespace FluentControl.Setup;

public partial class SetupWindow : FluentWindow
{
    private readonly SetupBootstrapper? setup;
    private bool ready, present, uninstall, completed, succeeded, removed, changingLanguage, cancelling;
    private InstallationSettings? settings;
    private string detail = "";
    internal SetupWindow(SetupBootstrapper? setup)
    {
        this.setup = setup;
        InitializeComponent();
        LanguageChoice.ItemsSource = Strings.Names;
        LanguageChoice.SelectedIndex = Array.IndexOf(Strings.Languages, Strings.Language);
        ThemeChoice.SelectedIndex = 0;
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        SourceInitialized += (_, _) =>
        {
            // WPF uses logical units; cap initial size on small/high-DPI screens.
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width - 24);
            Height = Math.Min(Height, area.Height - 24);
        };
        Closing += OnClosing;
        Localize();
    }
    private static string T(string key) => Strings.T(key);
    private void Localize()
    {
        changingLanguage = true;
        Title = Caption.Title = T("FluentControl Setup");
        Tagline.Text = T("Your displays, sound and desktop. Together.");
        VersionText.Text = Strings.F("Version {0} · Windows x64 · Offline installer", setup?.Version ?? "preview");
        FolderLabel.Text = T("Installation folder");
        Browse.Content = T("Browse…");
        StartupLabel.Text = T("Start with Windows");
        System.Windows.Automation.AutomationProperties.SetName(StartupSwitch, StartupLabel.Text);
        System.Windows.Automation.AutomationProperties.SetName(Folder, FolderLabel.Text);
        System.Windows.Automation.AutomationProperties.SetName(LanguageChoice, T("Language"));
        System.Windows.Automation.AutomationProperties.SetName(ThemeChoice, T("Theme"));
        StartupHint.Text = T("Run in the tray when you sign in. Change this later in Settings.");
        FolderHint.Text = T(settings?.ExistingDirectory == true ? "Updates keep the existing folder. To move the app, uninstall and reinstall; your settings are preserved." : "Install for your current Windows account.");
        LogButton.Content = T("Open log");
        LogButton.IsEnabled = setup is not null;
        RemoveButton.Content = T("Uninstall");
        var theme = Math.Max(0, ThemeChoice.SelectedIndex);
        ThemeChoice.ItemsSource = new[] { T("System theme"), T("Light"), T("Dark") };
        ThemeChoice.SelectedIndex = theme;
        CloseButton.Content = T(setup?.Busy == true ? "Cancel" : completed ? "Close" : "Cancel");
        if (completed)
        {
            Heading.Text = T(succeeded ? removed ? "Uninstalled" : "All set" : "Installation did not finish");
            Description.Text = detail;
            PrimaryButton.Content = T("Launch FluentControl");
        }
        else if (setup?.Busy == true)
        {
            Heading.Text = T(cancelling ? "Cancelling…" : "Working on it…");
            Description.Text = T("Please wait while setup finishes. Your personal settings are preserved.");
        }
        else
        {
            Heading.Text = T(!ready ? "Checking installation…" : uninstall ? "Uninstall FluentControl?" : present ? "Manage FluentControl" : settings?.ExistingDirectory == true ? "Update FluentControl" : "Make yourself comfortable");
            Description.Text = T(uninstall ? "The application will be removed. Your settings and monitor profiles will be kept." : "Choose your preferences, then let setup take care of the rest.");
            PrimaryButton.Content = T(uninstall ? "Uninstall" : present ? "Repair" : settings?.ExistingDirectory == true ? "Update" : "Install");
        }
        changingLanguage = false;
    }
    internal void ShowReady(bool isPresent, bool isUninstall, InstallationSettings value)
    {
        ready = true; present = isPresent; uninstall = isUninstall; settings = value;
        Folder.Text = value.Directory;
        Folder.IsReadOnly = value.ExistingDirectory;
        Browse.IsEnabled = !value.ExistingDirectory;
        StartupSwitch.IsChecked = value.Startup;
        Options.Visibility = uninstall ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.IsEnabled = setup?.Interactive ?? true;
        RemoveButton.Visibility = isPresent && !uninstall ? Visibility.Visible : Visibility.Collapsed;
        Localize();
    }
    internal void ShowWorking()
    {
        Options.Visibility = RemoveButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Visibility = Visibility.Collapsed;
        LanguageChoice.IsEnabled = false;
        ErrorText.Text = "";
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        Localize();
    }
    internal void ShowProgress(int value, bool rollback)
    {
        if (completed) return;
        Progress.IsIndeterminate = false;
        Progress.Value = value;
        ProgressText.Text = rollback ? T("Restoring the previous installation…") : value + "%";
    }
    internal void ShowCancelling()
    {
        cancelling = true;
        CloseButton.IsEnabled = false;
        Localize();
    }
    internal void ShowCompleted(bool success, string message, bool wasRemoved = false)
    {
        completed = true; succeeded = success; removed = wasRemoved; detail = message;
        Options.Visibility = Progress.Visibility = RemoveButton.Visibility = Visibility.Collapsed;
        ProgressText.Text = "";
        PrimaryButton.Visibility = setup?.CanLaunch == true ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsEnabled = true;
        CloseButton.IsEnabled = true;
        Localize();
    }
    internal void ShowInputError(string error) => ErrorText.Text = error;
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingLanguage || LanguageChoice.SelectedIndex < 0 || ThemeChoice is null) return;
        Strings.SetLanguage(Strings.Languages[LanguageChoice.SelectedIndex]);
        Localize();
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingLanguage) return;
        SystemThemeWatcher.UnWatch(this);
        if (ThemeChoice.SelectedIndex == 0)
        {
            ApplicationThemeManager.Apply(ApplicationThemeManager.IsSystemHighContrast() ? ApplicationTheme.HighContrast : ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        }
        else ApplicationThemeManager.Apply(ThemeChoice.SelectedIndex == 2 ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = FolderPicker.Show(this, T("Installation folder"), Folder.Text);
            if (folder is not null) Folder.Text = folder;
        }
        catch (Exception ex) { setup?.LogInputError(ex); ShowInputError(ex.Message); }
    }
    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (setup is null) { ShowInputError(T("Preview only. No installation will be performed.")); return; }
        if (completed) setup.Launch();
        else setup.Start(uninstall ? LaunchAction.Uninstall : present ? LaunchAction.Repair : LaunchAction.Install, Folder.Text, StartupSwitch.IsChecked == true);
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        uninstall = true;
        Options.Visibility = RemoveButton.Visibility = Visibility.Collapsed;
        Localize(); // second explicit click is required before uninstalling
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Log_Click(object sender, RoutedEventArgs e) => setup?.OpenLog();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (setup?.Busy != true) return;
        e.Cancel = true;
        if (!cancelling && System.Windows.MessageBox.Show(this, T("Cancel installation and wait for rollback?"), T("FluentControl Setup"), System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            setup.Cancel();
    }
}
