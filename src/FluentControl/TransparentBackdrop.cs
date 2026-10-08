using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace FluentControl;

internal sealed class TransparentBackdrop : SystemBackdrop
{
    private CompositionColorBrush? brush;
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        brush = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread().CreateColorBrush(Microsoft.UI.Colors.Transparent);
        target.SystemBackdrop = brush;
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        brush?.Dispose(); brush = null;
        base.OnTargetDisconnected(target);
    }
}
