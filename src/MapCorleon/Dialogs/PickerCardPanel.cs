using System.Windows;
using System.Windows.Controls;

namespace MapCorleon.Dialogs;

public sealed class PickerCardPanel : System.Windows.Controls.Panel
{
    private const double MinimumCardWidth = 200;
    private const double MaximumCardWidth = 270;
    private const double Gap = 12;
    private int columns;
    private double[] rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : MaximumCardWidth;
        columns = Math.Clamp((int)((width + Gap) / (MinimumCardWidth + Gap)), 1, InternalChildren.Count);
        var cardWidth = CardWidth(width);
        rowHeights = new double[(InternalChildren.Count + columns - 1) / columns];
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            child.Measure(new Size(cardWidth, double.PositiveInfinity));
            var row = index / columns;
            rowHeights[row] = Math.Max(rowHeights[row], child.DesiredSize.Height);
        }
        return new Size(width, rowHeights.Sum() + Gap * (rowHeights.Length - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        var cardWidth = CardWidth(finalSize.Width);
        double y = 0;
        for (var first = 0; first < InternalChildren.Count; first += columns)
        {
            var count = Math.Min(columns, InternalChildren.Count - first);
            var rowWidth = count * cardWidth + (count - 1) * Gap;
            var x = (finalSize.Width - rowWidth) / 2;
            var rowHeight = rowHeights[first / columns];
            for (var index = 0; index < count; index++)
                InternalChildren[first + index].Arrange(new Rect(x + index * (cardWidth + Gap), y, cardWidth, rowHeight));
            y += rowHeight + Gap;
        }
        return finalSize;
    }

    private double CardWidth(double width) => Math.Clamp((width - Gap * (columns - 1)) / columns, 0, MaximumCardWidth);
}
