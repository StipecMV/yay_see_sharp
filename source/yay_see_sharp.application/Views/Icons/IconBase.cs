using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace yay_see_sharp.application.Views.Icons;

public abstract class IconBase : UserControl
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IconBase, IBrush?>(nameof(Stroke));

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }
}
