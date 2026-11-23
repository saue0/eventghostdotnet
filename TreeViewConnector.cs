using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EventGhostDotNet;

public sealed class TreeViewConnector : FrameworkElement
{
    private const double DotSpacing = 3;
    private double _renderedOffset = double.NaN;

    public static readonly DependencyProperty HeaderHeightProperty =
        DependencyProperty.Register(nameof(HeaderHeight), typeof(double), typeof(TreeViewConnector),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLastSiblingProperty =
        DependencyProperty.Register(nameof(IsLastSibling), typeof(bool), typeof(TreeViewConnector),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsRootProperty =
        DependencyProperty.Register(nameof(IsRoot), typeof(bool), typeof(TreeViewConnector),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasChildrenProperty =
        DependencyProperty.Register(nameof(HasChildren), typeof(bool), typeof(TreeViewConnector),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TreeViewConnector),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public bool IsLastSibling
    {
        get => (bool)GetValue(IsLastSiblingProperty);
        set => SetValue(IsLastSiblingProperty, value);
    }

    public bool IsRoot
    {
        get => (bool)GetValue(IsRootProperty);
        set => SetValue(IsRootProperty, value);
    }

    public bool HasChildren
    {
        get => (bool)GetValue(HasChildrenProperty);
        set => SetValue(HasChildrenProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public TreeViewConnector()
    {
        LayoutUpdated += (_, _) =>
        {
            if (Math.Abs(GetVerticalOffset() - _renderedOffset) > 0.001)
                InvalidateVisual();
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualHeight <= 0)
            return;

        var brush = System.Windows.Media.Brushes.Gray;
        var offset = GetVerticalOffset();
        _renderedOffset = offset;
        var centerY = Math.Round((offset + Math.Min(HeaderHeight, ActualHeight) / 2) / DotSpacing)
                      * DotSpacing - offset;
        var horizontalEnd = Math.Min(26.5, ActualWidth - 0.5);
        if (!IsRoot)
        {
            for (var x = 0d; x <= horizontalEnd; x += DotSpacing)
                DrawDot(drawingContext, brush, x, centerY);

            var verticalEnd = IsLastSibling ? centerY : ActualHeight;
            DrawVerticalDots(drawingContext, brush, 0, 0, verticalEnd, offset, IsLastSibling);
        }

        if (HasChildren && IsExpanded)
        {
            var bottom = Math.Min(HeaderHeight, ActualHeight);
            DrawVerticalDots(drawingContext, brush, 18, centerY, bottom, offset, false);
        }
    }

    private double GetVerticalOffset()
    {
        // Keep one dot phase across the root and all of its descendants.
        TreeViewItem? root = null;
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem item)
                root = item;
            else if (current is System.Windows.Controls.TreeView)
                break;
        }

        return root is null ? 0 : TranslatePoint(new System.Windows.Point(), root).Y;
    }

    private static void DrawVerticalDots(DrawingContext drawingContext, System.Windows.Media.Brush brush,
        double x, double start, double end, double offset, bool includeEnd)
    {
        var first = Math.Ceiling((offset + start - 0.001) / DotSpacing) * DotSpacing - offset;
        for (var y = first; includeEnd ? y <= end + 0.001 : y < end - 0.001; y += DotSpacing)
            DrawDot(drawingContext, brush, x, y);
    }

    private static void DrawDot(
        DrawingContext drawingContext, System.Windows.Media.Brush brush, double x, double y) =>
        drawingContext.DrawRectangle(brush, null, new Rect(x - 0.5, y - 0.5, 1, 1));
}
