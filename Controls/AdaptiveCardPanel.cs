using System;
using System.Windows;
using System.Windows.Controls;

namespace OrbitLauncher.Controls;

/// <summary>Cards fill the available row and keep one consistent gutter when the window resizes.</summary>
public sealed class AdaptiveCardPanel : Panel
{
    private const double MinimumCardWidth = 340;
    private const double Gutter = 16;
    private double _rowHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? MinimumCardWidth : Math.Max(0, availableSize.Width);
        int columns = ColumnCount(width);
        double cardWidth = Math.Max(0, (width - (columns - 1) * Gutter) / columns);
        _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cardWidth, double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
        }
        int rows = (int)Math.Ceiling((double)InternalChildren.Count / columns);
        return new Size(width, rows == 0 ? 0 : rows * (_rowHeight + Gutter) - Gutter);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnCount(finalSize.Width);
        double width = Math.Max(0, (finalSize.Width - (columns - 1) * Gutter) / columns);
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(i % columns * (width + Gutter), i / columns * (_rowHeight + Gutter), width, _rowHeight));
        return finalSize;
    }

    private static int ColumnCount(double width) => Math.Max(1, (int)Math.Floor((width + Gutter) / (MinimumCardWidth + Gutter)));
}
