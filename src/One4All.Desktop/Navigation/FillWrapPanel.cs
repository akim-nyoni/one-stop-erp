using System.Windows;
using System.Windows.Controls;

namespace One4All.Desktop.Navigation;

/// <summary>
/// Lays cards out in as many equal columns as fit (each at least <see cref="MinItemWidth"/> wide) and stretches them to fill the width;
/// the cards in a row share the tallest one's height, so the page reads as a tidy grid at any window size.
/// </summary>
public sealed class FillWrapPanel : Panel
{
    /// <summary>The narrowest a card may be.</summary>
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(FillWrapPanel), new FrameworkPropertyMetadata(260d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private readonly List<double> _rowHeights = [];
    private double _itemWidth;
    private int _columns = 1;

    /// <summary>Gets or sets the narrowest a card may be.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        _rowHeights.Clear();
        if (count == 0)
        {
            return new Size(0, 0);
        }

        double width = double.IsInfinity(availableSize.Width) ? MinItemWidth * Math.Min(count, 4) : availableSize.Width;
        _columns = Math.Max(1, (int)(width / MinItemWidth));
        _itemWidth = width / _columns;
        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            child.Measure(new Size(_itemWidth, double.PositiveInfinity));
            if (i % _columns == 0)
            {
                _rowHeights.Add(0);
            }

            _rowHeights[^1] = Math.Max(_rowHeights[^1], child.DesiredSize.Height);
        }

        return new Size(width, _rowHeights.Sum());
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double itemWidth = _columns > 0 ? finalSize.Width / _columns : _itemWidth;
        double top = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int row = i / _columns;
            if (i > 0 && i % _columns == 0)
            {
                top += _rowHeights[row - 1];
            }

            InternalChildren[i].Arrange(new Rect((i % _columns) * itemWidth, top, itemWidth, row < _rowHeights.Count ? _rowHeights[row] : 0));
        }

        return finalSize;
    }
}
