using FluentControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FluentControl;

internal sealed class MonitorIdentificationWindow : Window
{
    internal TextBlock IndexLabel { get; }
    internal TextBlock NameLabel { get; }
    private readonly StackPanel labels;
    private readonly Border root;
    private double lastScale;

    internal MonitorIdentificationWindow(MonitorDevice device, string title, ElementTheme theme)
    {
        Title = title;
        IndexLabel = new TextBlock { Text = device.Preference.Label, FontSize = 56, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        NameLabel = new TextBlock { Text = device.DisplayName, FontSize = 20, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        labels = new StackPanel { Spacing = 8 };
        labels.Children.Add(IndexLabel); labels.Children.Add(NameLabel);
        root = new Border
        {
            RequestedTheme = theme, Padding = new Thickness(24), BorderThickness = new Thickness(3),
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child = new ScrollViewer { Content = labels, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Center }
        };
        Content = root;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        }
        WindowPlacement.Place(this, 440, 220, device);
        root.Loaded += (_, _) =>
        {
            FitLabels();
            root.XamlRoot.Changed += OnRootChanged;
        };
        Closed += (_, _) => { if (root.XamlRoot is { } xaml) xaml.Changed -= OnRootChanged; };
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (Math.Abs(lastScale - sender.RasterizationScale) > .01) FitLabels();
    }

    private void FitLabels()
    {
        lastScale = WindowPlacement.Scale(this);
        var area = WindowPlacement.Area(this);
        var size = WindowGeometry.ClientSize(440, 220, lastScale, area, 0, 0);
        // Measure at the actual available DIP width, including wrapping. The
        // scroll viewer remains a fallback for exceptionally long user names.
        labels.Measure(new Size(Math.Max(1, size.Width / lastScale - 54), double.PositiveInfinity));
        WindowPlacement.ResizeAndCenter(this, 440, Math.Max(220, Math.Ceiling(labels.DesiredSize.Height + 54)));
    }
}
