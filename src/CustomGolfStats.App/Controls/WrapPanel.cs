using Windows.Foundation;

namespace CustomGolfStats.App.Controls;

/// <summary>Lays children out left to right, wrapping to a new row when the width runs out.</summary>
public sealed partial class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;

    /// <summary>Never wrap: one long row (put the panel in a horizontal ScrollViewer).</summary>
    public bool SingleRow { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(SingleRow ? double.PositiveInfinity : availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (!SingleRow && x > 0 && x + size.Width > availableSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
            width = Math.Max(width, x - Spacing);
        }
        return new Size(SingleRow || double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (!SingleRow && x > 0 && x + size.Width > finalSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return finalSize;
    }
}
