using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private ScrollViewer? _attachedScrollViewer;
    private static readonly WpfBrush BadgeBackgroundBrush = CreateBrush("#8f111827");

    private const double LeftLabelWidth = 78;
    private const double RowHeight = 24;

    public MemoryLayoutControl()
    {
        Loaded += (_, _) => AttachScrollViewer();
        Unloaded += (_, _) => DetachScrollViewer();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MemoryLayoutControl c)
            return;
        c.InvalidateMeasure();
        c.InvalidateVisual();
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        if (Layout is null || Layout.Rows.Count == 0)
            return new WpfSize(900, 300);

        double height = Math.Max(300, Layout.Rows.Count * RowHeight + 24);
        return new WpfSize(1000, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _hitRegions.Clear();
        if (Layout is null || Layout.Rows.Count == 0)
            return;

        DrawMemoryGrid(dc);
        DrawMemoryBlocks(dc);
        DrawMarkers(dc);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Layout is null || Layout.Rows.Count == 0)
            return;

        var pos = e.GetPosition(this);
        int row = (int)(pos.Y / RowHeight);
        if (row < 0 || row >= Layout.Rows.Count)
            return;

        var rowModel = Layout.Rows[row];
        int rowByteSpan = Math.Max(1, rowModel.EndOffset - rowModel.StartOffset + 1);
        double drawableWidth = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        double rx = Math.Clamp(pos.X - LeftLabelWidth, 0, drawableWidth);
        int byteInRow = (int)Math.Floor((rx / drawableWidth) * rowByteSpan);
        int offset = Math.Clamp(rowModel.StartOffset + byteInRow, 0, Layout.FileSize - 1);
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

        GetVisibleRowRange(out int firstRow, out int lastRow);
        if (lastRow < firstRow)
            return;

        var linePen = new WpfPen(CreateBrush("#e5e7eb"), 0.7);
        double width = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        double minY = firstRow * RowHeight;
        double maxY = (lastRow + 1) * RowHeight;

        for (int r = firstRow; r <= lastRow + 1; r++)
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
                dc.DrawLine(linePen, new WpfPoint(x, minY), new WpfPoint(x, maxY));
            }
        }
    }

    private void DrawMemoryBlocks(DrawingContext dc)
    {
        if (Layout is null)
            return;

        GetVisibleRowRange(out int firstRow, out int lastRow);
        if (lastRow < firstRow)
            return;

        double width = Math.Max(1, ActualWidth - LeftLabelWidth - 8);
        for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            var row = Layout.Rows[rowIndex];
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

                DrawBlockOverlay(dc, block, rect);
            }
        }
    }

    private void DrawRowLabel(DrawingContext dc, MemoryLayoutRow row)
    {
        string label = row.IsCollapsedSummary
            ? $"0x{row.StartOffset:X6}..0x{row.EndOffset:X6}"
            : $"0x{row.StartOffset:X6}";
        var text = BuildText(label, 8, WpfBrushes.DimGray);
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
            dc.DrawText(txt, new WpfPoint(x + 2, Layout.Rows.Count * RowHeight + 2));
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

    private void GetVisibleRowRange(out int firstRow, out int lastRow)
    {
        firstRow = 0;
        lastRow = (Layout?.Rows.Count ?? 1) - 1;
        if (Layout is null || Layout.Rows.Count == 0)
            return;

        ScrollViewer? viewer = _attachedScrollViewer ?? FindAncestorScrollViewer();
        if (viewer is null)
            return;

        double offset = viewer.VerticalOffset;
        double viewport = viewer.ViewportHeight;
        if (viewport <= 0)
            return;

        firstRow = Math.Max(0, (int)Math.Floor(offset / RowHeight) - 2);
        lastRow = Math.Min(Layout.Rows.Count - 1, (int)Math.Ceiling((offset + viewport) / RowHeight) + 2);
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void AttachScrollViewer()
    {
        var viewer = FindAncestorScrollViewer();
        if (ReferenceEquals(viewer, _attachedScrollViewer))
            return;

        DetachScrollViewer();
        _attachedScrollViewer = viewer;
        if (_attachedScrollViewer is not null)
            _attachedScrollViewer.ScrollChanged += AttachedScrollViewer_ScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (_attachedScrollViewer is null)
            return;
        _attachedScrollViewer.ScrollChanged -= AttachedScrollViewer_ScrollChanged;
        _attachedScrollViewer = null;
    }

    private void AttachedScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) > 0.01
            || Math.Abs(e.ViewportHeightChange) > 0.01
            || Math.Abs(e.HorizontalChange) > 0.01
            || Math.Abs(e.ViewportWidthChange) > 0.01)
        {
            InvalidateVisual();
        }
    }

    private static string BuildBlockTooltip(MemoryLayoutBlock block)
    {
        int fullEnd = block.FullStartOffset + block.FullLength - 1;
        int visibleEnd = block.StartOffset + block.Length - 1;
        string baseText =
            $"Type: {block.BlockType}\n" +
            $"Name: {block.FullName}\n" +
            $"Full range: 0x{block.FullStartOffset:X8}..0x{fullEnd:X8}\n" +
            $"Full size: {block.FullLength} bytes\n" +
            $"Visible range: 0x{block.StartOffset:X8}..0x{visibleEnd:X8}\n" +
            $"Visible size: {block.Length} bytes";

        if (!string.IsNullOrWhiteSpace(block.AnimationInfo))
            baseText += $"\nAnimation: {block.AnimationInfo}";

        if (block.PerformanceMetrics is null)
        {
            if (block.Title == "..." && block.FullName.Contains("collapsed", StringComparison.OrdinalIgnoreCase))
                return $"{baseText}\n\nCollapsed segment:\n{block.FullName}";
            return baseText;
        }

        string efficiency = block.PerformanceMetrics.UsageEfficiencyPercent.HasValue
            ? $"{block.PerformanceMetrics.UsageEfficiencyPercent.Value:0.#}%"
            : "n/a";

        return
            baseText + "\n\n" +
            "Performance:\n" +
            $"Parse time: {block.PerformanceMetrics.ParseTimeMs:0.##}ms\n" +
            $"Memory impact: {FormatCompactBytes(block.PerformanceMetrics.MemoryImpactBytes)}\n" +
            $"Network priority: {block.PerformanceMetrics.NetworkPriority}\n" +
            $"Optimization potential: {block.PerformanceMetrics.Tier}\n" +
            $"Usage efficiency: {efficiency}\n" +
            $"Suggestion: {block.PerformanceMetrics.OptimizationSuggestion}";
    }

    private void DrawBlockOverlay(DrawingContext dc, MemoryLayoutBlock block, Rect rect)
    {
        if (rect.Width <= 14 || rect.Height <= 10)
            return;

        // Large blocks are split across many rows. Draw performance text only once
        // on the first visible segment to avoid heavy duplicate text rendering.
        bool isContinuationSegment = block.PerformanceMetrics is not null && block.StartOffset > block.FullStartOffset;
        if (isContinuationSegment)
            return;

        var textBrush = ChooseTextBrush(block.BackgroundBrush);
        string topText = block.PerformanceMetrics?.TypeOverlayText ?? block.Title;
        string bottomText = block.PerformanceMetrics?.MetricsOverlayText ?? block.SizeInfo;

        DrawLabel(dc, topText, rect, textBrush, alignBottom: false, alignRight: false, minWidth: 28, fontSize: 8.0);

        // Bottom-right metrics are more expensive and less readable on narrow segments.
        if (rect.Width >= 120)
            DrawLabel(dc, bottomText, rect, textBrush, alignBottom: true, alignRight: true, minWidth: 62, fontSize: 7.0);
    }

    private void DrawLabel(
        DrawingContext dc,
        string text,
        Rect rect,
        WpfBrush textBrush,
        bool alignBottom,
        bool alignRight,
        double minWidth,
        double fontSize)
    {
        if (rect.Width < minWidth || string.IsNullOrWhiteSpace(text))
            return;

        var formatted = BuildText(text, fontSize, textBrush);
        double x = alignRight ? rect.Right - formatted.Width - 4 : rect.X + 4;
        double y = alignBottom ? rect.Bottom - formatted.Height - 2 : rect.Y + 2;

        var badgeRect = new Rect(
            x - 2,
            y - 1,
            Math.Min(formatted.Width + 4, Math.Max(0, rect.Width - 4)),
            formatted.Height + 2);
        dc.DrawRoundedRectangle(BadgeBackgroundBrush, null, badgeRect, 2, 2);
        dc.DrawText(formatted, new WpfPoint(x, y));
    }

    private static WpfBrush ChooseTextBrush(WpfBrush background)
    {
        if (background is not SolidColorBrush solid)
            return WpfBrushes.White;

        double luminance = (0.299 * solid.Color.R) + (0.587 * solid.Color.G) + (0.114 * solid.Color.B);
        return luminance > 145 ? WpfBrushes.Black : WpfBrushes.White;
    }

    private static string FormatCompactBytes(long value)
    {
        if (value < 1024)
            return $"{value}B";
        if (value < 1024 * 1024)
            return $"{(value / 1024d):0.#}KB";
        return $"{(value / 1024d / 1024d):0.#}MB";
    }
}
