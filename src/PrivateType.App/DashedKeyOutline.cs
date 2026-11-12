using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PrivateType.App;

// The outline of an empty key: four solid rounded corners joined by dashes spaced evenly along each side, so every side
// starts and ends the same way and no dash is cut by a corner. The line is a whole number of pixels thick, on whole pixels.
public sealed class DashedKeyOutline : FrameworkElement
{
    private const double Radius = 6;
    private const double Dash = 3;
    private const double Gap = 3;

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(DashedKeyOutline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        if (Stroke is null || ActualWidth < 2 * Radius || ActualHeight < 2 * Radius)
            return;

        var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var thickness = Math.Max(1, Math.Round(scale)) / scale;
        var pen = new Pen(Stroke, thickness);
        // The line's centre runs half its thickness inside the key's edge.
        var left = thickness / 2;
        var top = thickness / 2;
        var right = ActualWidth - (thickness / 2);
        var bottom = ActualHeight - (thickness / 2);
        var radius = Radius - (thickness / 2);

        var corners = new StreamGeometry();
        using (var path = corners.Open())
        {
            Corner(path, new Point(left, top + radius), new Point(left + radius, top), radius);
            Corner(path, new Point(right - radius, top), new Point(right, top + radius), radius);
            Corner(path, new Point(right, bottom - radius), new Point(right - radius, bottom), radius);
            Corner(path, new Point(left + radius, bottom), new Point(left, bottom - radius), radius);
        }

        corners.Freeze();
        context.DrawGeometry(null, pen, corners);
        Dashes(context, pen, new Point(left + radius, top), new Point(right - radius, top));
        Dashes(context, pen, new Point(right, top + radius), new Point(right, bottom - radius));
        Dashes(context, pen, new Point(right - radius, bottom), new Point(left + radius, bottom));
        Dashes(context, pen, new Point(left, bottom - radius), new Point(left, top + radius));
    }

    private static void Corner(StreamGeometryContext path, Point from, Point to, double radius)
    {
        path.BeginFigure(from, isFilled: false, isClosed: false);
        path.ArcTo(to, new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
    }

    // As many dashes as fit at about the nominal gap, with the same gap before the first, between each two and after the last.
    private static void Dashes(DrawingContext context, Pen pen, Point from, Point to)
    {
        var side = to - from;
        var count = (int)Math.Round((side.Length - Gap) / (Dash + Gap));
        if (count < 1)
            return;

        var gap = (side.Length - (count * Dash)) / (count + 1);
        var along = side / side.Length;
        for (var index = 0; index < count; index++)
        {
            var start = from + (along * (gap + (index * (Dash + gap))));
            context.DrawLine(pen, start, start + (along * Dash));
        }
    }
}
