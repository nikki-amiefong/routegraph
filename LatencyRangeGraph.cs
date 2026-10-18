using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Routegraph;

public sealed class LatencyRangeGraph : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LatencyRangeGraph, double>(nameof(Minimum));
    public static readonly StyledProperty<double> AverageProperty =
        AvaloniaProperty.Register<LatencyRangeGraph, double>(nameof(Average));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LatencyRangeGraph, double>(nameof(Maximum));
    public static readonly StyledProperty<double> ScaleMaximumProperty =
        AvaloniaProperty.Register<LatencyRangeGraph, double>(nameof(ScaleMaximum), 10);
    public static readonly StyledProperty<bool> HasSamplesProperty =
        AvaloniaProperty.Register<LatencyRangeGraph, bool>(nameof(HasSamples));

    private static readonly IBrush TrackBrush = Brush("#26334A");
    private static readonly IBrush AverageBrush = Brush("#344D91");
    private static readonly IBrush RangeBrush = Brush("#83A0FF");
    private static readonly IBrush MarkerBrush = Brush("#DCE5FF");

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Average
    {
        get => GetValue(AverageProperty);
        set => SetValue(AverageProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double ScaleMaximum
    {
        get => GetValue(ScaleMaximumProperty);
        set => SetValue(ScaleMaximumProperty, value);
    }

    public bool HasSamples
    {
        get => GetValue(HasSamplesProperty);
        set => SetValue(HasSamplesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MinimumProperty ||
            change.Property == AverageProperty ||
            change.Property == MaximumProperty ||
            change.Property == ScaleMaximumProperty ||
            change.Property == HasSamplesProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var centerY = height / 2;
        var scale = Math.Max(ScaleMaximum, 1);
        var minimumX = Math.Clamp(Minimum / scale * width, 0, width);
        var averageX = Math.Clamp(Average / scale * width, 0, width);
        var maximumX = Math.Clamp(Maximum / scale * width, 0, width);
        var track = new Rect(0, centerY - 2, width, 4);

        context.DrawRectangle(TrackBrush, null, track);
        if (!HasSamples)
            return;

        context.DrawRectangle(AverageBrush, null,
            new Rect(0, centerY - 2, averageX, 4));

        var rangePen = new Pen(RangeBrush, 2);
        context.DrawLine(rangePen, new Point(minimumX, centerY), new Point(maximumX, centerY));
        context.DrawLine(rangePen,
            new Point(minimumX, centerY - 5),
            new Point(minimumX, centerY + 5));
        context.DrawLine(rangePen,
            new Point(maximumX, centerY - 5),
            new Point(maximumX, centerY + 5));
        context.DrawEllipse(MarkerBrush, null, new Point(averageX, centerY), 3.5, 3.5);
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
