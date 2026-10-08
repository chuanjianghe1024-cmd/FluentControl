using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace FluentControl;

internal sealed class TransparentBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
        compositor = new Windows.UI.Composition.Compositor();
        brush = compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        target.SystemBackdrop = brush;
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        brush?.Dispose(); brush = null;
        compositor?.Dispose(); compositor = null;
        base.OnTargetDisconnected(target);
    }
}
