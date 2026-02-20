using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DecodingGif.Core.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace DecodingGif.UI.Controls;

public sealed class MemoryLayoutControl : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(
            nameof(Layout),
            typeof(MemoryLayoutVisualization),
            typeof(MemoryLayoutControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty ShowAlignmentGridProperty =
        DependencyProperty.Register(
            nameof(ShowAlignmentGrid),
            typeof(bool),
            typeof(MemoryLayoutControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowEmptySpaceProperty =
        DependencyProperty.Register(
            nameof(ShowEmptySpace),
            typeof(bool),
            typeof(MemoryLayoutControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public MemoryLayoutVisualization? Layout
    {
        get => (MemoryLayoutVisualization?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public bool ShowAlignmentGrid
    {
        get => (bool)GetValue(ShowAlignmentGridProperty);
        set => SetValue(ShowAlignmentGridProperty, value);
    }

    public bool ShowEmptySpace
    {
        get => (bool)GetValue(ShowEmptySpaceProperty);
        set => SetValue(ShowEmptySpaceProperty, value);
    }

    public event EventHandler<int>? NavigateToOffset;
    private readonly List<(Rect Rect, MemoryLayoutBlock Block)> _hitRegions = [];
    private string? _lastTooltipText;

    private const double LeftLabelWidth = 78;
    private const double RowHeight = 24;

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MemoryLayoutControl c)
            return;
        c.InvalidateMeasure();
        c.InvalidateVisual();
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        if (Layout is null || Layout.TotalRows == 0)
            return new WpfSize(900, 300);

        double height = Math.Max(300, Layout.TotalRows * RowHeight + 24);
        return new WpfSize(1000, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _hitRegions.Clear();
        if (Layout is null || Layout.TotalRows == 0)
            return;

        DrawMemoryGrid(dc);
        DrawMemoryBlocks(dc);
        DrawMarkers(dc);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Layout is null || Layout.TotalRows == 0)
            return;

        var pos = e.GetPosition(this);
        int row = (int)(pos.Y / RowHeight);
        if (row < 0 || row >= Layout.Rows.Count)
            return;

        double drawableWidth = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        double rx = Math.Clamp(pos.X - LeftLabelWidth, 0, drawableWidth);
        int byteInRow = (int)Math.Floor((rx / drawableWidth) * Layout.BytesPerRow);
        int offset = Math.Clamp(Layout.Rows[row].StartOffset + byteInRow, 0, Layout.FileSize - 1);
        NavigateToOffset?.Invoke(this, offset);
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var block = HitTestBlock(p);
        if (block is null)
        {
            if (_lastTooltipText is not null)
            {
                ToolTip = null;
                _lastTooltipText = null;
            }
            return;
        }

        string text = BuildBlockTooltip(block);
        if (_lastTooltipText == text)
            return;

        ToolTip = text;
        _lastTooltipText = text;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ToolTip = null;
        _lastTooltipText = null;
    }

    private void DrawMemoryGrid(DrawingContext dc)
    {
        if (Layout is null)
            return;

        var linePen = new WpfPen(CreateBrush("#e5e7eb"), 0.7);
        double width = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        double maxY = Layout.TotalRows * RowHeight;

        for (int r = 0; r <= Layout.TotalRows; r++)
        {
            double y = r * RowHeight;
            dc.DrawLine(linePen, new WpfPoint(LeftLabelWidth, y), new WpfPoint(LeftLabelWidth + width, y));
        }

        if (ShowAlignmentGrid)
        {
            int markerStep = Layout.BytesPerRow >= 64 ? 16 : 8;
            for (int b = 0; b <= Layout.BytesPerRow; b += markerStep)
            {
                double x = LeftLabelWidth + (b / (double)Layout.BytesPerRow) * width;
                dc.DrawLine(linePen, new WpfPoint(x, 0), new WpfPoint(x, maxY));
            }
        }
    }

    private void DrawMemoryBlocks(DrawingContext dc)
    {
        if (Layout is null)
            return;

        double width = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        foreach (var row in Layout.Rows)
        {
            double y = row.RowIndex * RowHeight + 2;
            DrawRowLabel(dc, row);

            foreach (var block in row.Blocks)
            {
                if (!ShowEmptySpace && block.Title == "Empty")
                    continue;

                double x = LeftLabelWidth + block.RelativeStart * width;
                double w = Math.Max(1.0, block.RelativeWidth * width);
                var rect = new Rect(x, y, w, RowHeight - 4);
                dc.DrawRoundedRectangle(block.BackgroundBrush, new WpfPen(WpfBrushes.DarkGray, 0.6), rect, 2, 2);
                _hitRegions.Add((rect, block));

                if (rect.Width > 26)
                {
                    var txt = BuildText($"{block.Title}", 8, WpfBrushes.Black);
                    dc.DrawText(txt, new WpfPoint(rect.X + 3, rect.Y + ((rect.Height - txt.Height) / 2)));
                }
            }
        }
    }

    private void DrawRowLabel(DrawingContext dc, MemoryLayoutRow row)
    {
        var text = BuildText($"0x{row.StartOffset:X6}", 8, WpfBrushes.DimGray);
        double y = row.RowIndex * RowHeight + ((RowHeight - text.Height) / 2);
        dc.DrawText(text, new WpfPoint(4, y));
    }

    private void DrawMarkers(DrawingContext dc)
    {
        if (Layout is null)
            return;

        double width = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        for (int b = 0; b <= Layout.BytesPerRow; b += 16)
        {
            double x = LeftLabelWidth + (b / (double)Layout.BytesPerRow) * width;
            var txt = BuildText($"{b}", 8, WpfBrushes.Gray);
            dc.DrawText(txt, new WpfPoint(x + 2, Layout.TotalRows * RowHeight + 2));
        }
    }

    private static FormattedText BuildText(string text, double size, WpfBrush color) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            color,
            1.0);

    private static WpfBrush CreateBrush(string hex)
    {
        var c = (WpfColor)WpfColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private MemoryLayoutBlock? HitTestBlock(WpfPoint point)
    {
        for (int i = _hitRegions.Count - 1; i >= 0; i--)
        {
            var item = _hitRegions[i];
            if (item.Rect.Contains(point))
                return item.Block;
        }

        return null;
    }

    private static string BuildBlockTooltip(MemoryLayoutBlock block)
    {
        int fullEnd = block.FullStartOffset + block.FullLength - 1;
        int visibleEnd = block.StartOffset + block.Length - 1;
        return
            $"Type: {block.BlockType}\n" +
            $"Name: {block.FullName}\n" +
            $"Full range: 0x{block.FullStartOffset:X8}..0x{fullEnd:X8}\n" +
            $"Full size: {block.FullLength} bytes\n" +
            $"Visible range: 0x{block.StartOffset:X8}..0x{visibleEnd:X8}\n" +
            $"Visible size: {block.Length} bytes";
    }
}
