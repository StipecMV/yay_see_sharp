using Avalonia;
using Avalonia.Controls;

namespace yay_see_sharp.application.Views.Icons;

public partial class LogoMark : UserControl
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LogoMark, double>(nameof(Size), 24d);

    public LogoMark()
    {
        InitializeComponent();
        ApplySize(Size);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty)
        {
            ApplySize((double)(change.NewValue ?? 24d));
        }
    }

    private void ApplySize(double size)
    {
        Badge.Width = size;
        Badge.Height = size;
        Badge.CornerRadius = new CornerRadius(size * 16d / 60d);
        Glyph.FontSize = size * 28d / 60d;

        var hashSize = size * 14d / 60d;
        var inset = size * 9d / 60d;
        HashHost.Width = hashSize;
        HashHost.Height = hashSize;
        HashHost.Margin = new Thickness(0, 0, inset, inset);
    }
}
