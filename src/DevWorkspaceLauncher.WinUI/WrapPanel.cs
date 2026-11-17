using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>Lays its children out left to right, starting a new line when the next one does not fit.</summary>
public sealed partial class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return Arrange(availableSize.Width, place: false);
    }

    protected override Size ArrangeOverride(Size finalSize) => Arrange(finalSize.Width, place: true);

    private Size Arrange(double width, bool place)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += lineHeight + Spacing;
                lineHeight = 0;
            }

            if (place)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            lineHeight = Math.Max(lineHeight, size.Height);
            widest = Math.Max(widest, x - Spacing);
        }

        return new Size(widest, y + lineHeight);
    }
}
