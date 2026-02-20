using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace DecodingGif.UI.Controls;

public sealed class FileOverviewControl : FrameworkElement
{
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks), typeof(IEnumerable<GifByteRange>), typeof(FileOverviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FileLengthProperty = DependencyProperty.Register(
        nameof(FileLength), typeof(int), typeof(FileOverviewControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedOffsetProperty = DependencyProperty.Register(
        nameof(SelectedOffset), typeof(int?), typeof(FileOverviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoveredOffsetProperty = DependencyProperty.Register(
        nameof(HoveredOffset), typeof(int?), typeof(FileOverviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<GifByteRange>? Blocks
    {
        get => (IEnumerable<GifByteRange>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public int FileLength
    {
        get => (int)GetValue(FileLengthProperty);
        set => SetValue(FileLengthProperty, value);
    }

    public int? SelectedOffset
    {
        get => (int?)GetValue(SelectedOffsetProperty);
        set => SetValue(SelectedOffsetProperty, value);
    }

    public int? HoveredOffset
    {
        get => (int?)GetValue(HoveredOffsetProperty);
        set => SetValue(HoveredOffsetProperty, value);
    }

    public event EventHandler<int>? OffsetClicked;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(MediaColor.FromRgb(241, 245, 249)), null, rect);

        if (FileLength <= 0 || Blocks is null)
            return;

        var blocks = Blocks.ToList();
        foreach (var block in blocks)
        {
            double y = block.Start / (double)FileLength * ActualHeight;
            double h = Math.Max(1.0, block.Length / (double)FileLength * ActualHeight);
            var brush = BlockColorPalette.BuildBrush(block.Kind, false);
            dc.DrawRectangle(brush, null, new Rect(0, y, ActualWidth, h));
        }

        DrawOffsetMarker(dc, HoveredOffset, MediaColor.FromRgb(51, 65, 85), 1.0);
        DrawOffsetMarker(dc, SelectedOffset, MediaColor.FromRgb(234, 179, 8), 2.0);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (FileLength <= 0 || ActualHeight <= 0)
            return;

        var p = e.GetPosition(this);
        int offset = (int)Math.Clamp(Math.Round((p.Y / ActualHeight) * (FileLength - 1)), 0, FileLength - 1);
        OffsetClicked?.Invoke(this, offset);
    }

    private void DrawOffsetMarker(DrawingContext dc, int? offset, MediaColor color, double thickness)
    {
        if (!offset.HasValue || FileLength <= 0)
            return;

        double y = offset.Value / (double)FileLength * ActualHeight;
        var pen = new MediaPen(new SolidColorBrush(color), thickness);
        dc.DrawLine(pen, new WpfPoint(0, y), new WpfPoint(ActualWidth, y));
    }
}
