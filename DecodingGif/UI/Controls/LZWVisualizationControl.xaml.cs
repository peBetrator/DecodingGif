using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using DecodingGif.Core.Models;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfBrush = System.Windows.Media.Brush;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DecodingGif.UI.Controls;

public partial class LZWVisualizationControl : WpfUserControl
{
    private const double CodeTableRowHeight = 22;
    private const double OutputRowHeight = 20;

    private Rect _codeTableArea;
    private Rect _bitStreamArea;
    private Rect _stepDetailsArea;
    private double _codeTableScrollOffset;
    private int? _hoveredCode;
    private int _bitStreamFirstVisibleRow;
    private int _bitStreamMaxFirstVisibleRow;
    private double _stepDetailsScrollOffset;
    private double _stepDetailsMaxScrollOffset;
    private Rect _codeTrackRect = Rect.Empty;
    private Rect _codeThumbRect = Rect.Empty;
    private Rect _bitTrackRect = Rect.Empty;
    private Rect _bitThumbRect = Rect.Empty;
    private bool _isDraggingCodeScroll;
    private bool _isDraggingBitScroll;
    private double _dragStartY;
    private double _dragStartCodeOffset;
    private int _dragStartBitRow;
    private double _codeMaxOffset;
    private readonly DispatcherTimer _pulseTimer;

    public static readonly DependencyProperty DecompressionStateProperty =
        DependencyProperty.Register(
            nameof(DecompressionState),
            typeof(LZWDecompressionState),
            typeof(LZWVisualizationControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDecompressionStateChanged));

    public LZWDecompressionState? DecompressionState
    {
        get => (LZWDecompressionState?)GetValue(DecompressionStateProperty);
        set => SetValue(DecompressionStateProperty, value);
    }

    public static readonly DependencyProperty CompressedDataProperty =
        DependencyProperty.Register(
            nameof(CompressedData),
            typeof(byte[]),
            typeof(LZWVisualizationControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCompressedDataChanged));

    public byte[]? CompressedData
    {
        get => (byte[]?)GetValue(CompressedDataProperty);
        set => SetValue(CompressedDataProperty, value);
    }

    public static readonly DependencyProperty IsVisualizationActiveProperty =
        DependencyProperty.Register(
            nameof(IsVisualizationActive),
            typeof(bool),
            typeof(LZWVisualizationControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsVisualizationActive
    {
        get => (bool)GetValue(IsVisualizationActiveProperty);
        set => SetValue(IsVisualizationActiveProperty, value);
    }

    public static readonly DependencyProperty ErrorMessageProperty =
        DependencyProperty.Register(
            nameof(ErrorMessage),
            typeof(string),
            typeof(LZWVisualizationControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public static readonly DependencyProperty ShowOnlyDynamicCodesProperty =
        DependencyProperty.Register(
            nameof(ShowOnlyDynamicCodes),
            typeof(bool),
            typeof(LZWVisualizationControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowOnlyDynamicCodes
    {
        get => (bool)GetValue(ShowOnlyDynamicCodesProperty);
        set => SetValue(ShowOnlyDynamicCodesProperty, value);
    }

    public LZWVisualizationControl()
    {
        InitializeComponent();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _pulseTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _pulseTimer.Tick += (_, _) =>
        {
            if (IsVisualizationActive)
                InvalidateVisual();
        };

        Loaded += (_, _) => _pulseTimer.Start();
        Unloaded += (_, _) => _pulseTimer.Stop();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var panelBg = new SolidColorBrush(WpfColor.FromRgb(248, 250, 252));
        panelBg.Freeze();
        drawingContext.DrawRectangle(panelBg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double topOffset = 12;
        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            DrawErrorBanner(drawingContext, new Rect(12, 10, Math.Max(0, ActualWidth - 24), 30), ErrorMessage);
            topOffset = 48;
        }

        if (!IsVisualizationActive)
        {
            DrawEmptyState(drawingContext, new Rect(12, topOffset, Math.Max(0, ActualWidth - 24), Math.Max(0, ActualHeight - topOffset - 12)));
            return;
        }

        const double margin = 14;
        double availableWidth = Math.Max(0, ActualWidth - margin * 2);
        double availableHeight = Math.Max(0, ActualHeight - topOffset - margin);
        double cellWidth = Math.Max(0, (availableWidth - margin) / 2);
        double cellHeight = Math.Max(0, (availableHeight - margin) / 2);

        _codeTableArea = new Rect(margin, topOffset, cellWidth, cellHeight);
        _bitStreamArea = new Rect(margin * 2 + cellWidth, topOffset, cellWidth, cellHeight);
        var outputBufferArea = new Rect(margin, topOffset + margin + cellHeight, cellWidth, cellHeight);
        _stepDetailsArea = new Rect(margin * 2 + cellWidth, topOffset + margin + cellHeight, cellWidth, cellHeight);

        _codeTrackRect = Rect.Empty;
        _codeThumbRect = Rect.Empty;
        _bitTrackRect = Rect.Empty;
        _bitThumbRect = Rect.Empty;
        DrawCodeTable(drawingContext, _codeTableArea);
        DrawBitStream(drawingContext, _bitStreamArea);
        DrawOutputBuffer(drawingContext, outputBufferArea);
        DrawStepDetails(drawingContext, _stepDetailsArea);
    }

    private static void OnDecompressionStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LZWVisualizationControl control)
        {
            return;
        }

        if (e.OldValue is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= control.OnStatePropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newNotify)
        {
            newNotify.PropertyChanged += control.OnStatePropertyChanged;
        }

        control.InvalidateVisual();
    }

    private static void OnCompressedDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LZWVisualizationControl control)
        {
            return;
        }

        control.InvalidateVisual();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnMouseWheel(WpfMouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var p = e.GetPosition(this);
        if (_codeTableArea.Contains(p))
        {
            int count = GetVisibleCodeKeys().Count;
            double maxOffset = Math.Max(0, count * CodeTableRowHeight - _codeTableArea.Height);
            if (maxOffset <= 0)
            {
                return;
            }

            _codeTableScrollOffset = Math.Clamp(_codeTableScrollOffset - (e.Delta / 120d) * (CodeTableRowHeight * 2), 0, maxOffset);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_bitStreamArea.Contains(p))
        {
            if (_bitStreamMaxFirstVisibleRow <= 0)
                return;

            _bitStreamFirstVisibleRow = Math.Clamp(_bitStreamFirstVisibleRow - Math.Sign(e.Delta), 0, _bitStreamMaxFirstVisibleRow);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_stepDetailsArea.Contains(p))
        {
            if (_stepDetailsMaxScrollOffset <= 0)
                return;

            _stepDetailsScrollOffset = Math.Clamp(_stepDetailsScrollOffset - (e.Delta / 120d) * 22, 0, _stepDetailsMaxScrollOffset);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);

        if (_isDraggingCodeScroll)
        {
            double delta = p.Y - _dragStartY;
            double trackTravel = Math.Max(1, _codeTrackRect.Height - _codeThumbRect.Height);
            double contentTravel = _codeMaxOffset;
            _codeTableScrollOffset = Math.Clamp(_dragStartCodeOffset + ((delta / trackTravel) * contentTravel), 0, _codeMaxOffset);
            InvalidateVisual();
            return;
        }

        if (_isDraggingBitScroll)
        {
            double delta = p.Y - _dragStartY;
            double trackTravel = Math.Max(1, _bitTrackRect.Height - _bitThumbRect.Height);
            int maxRow = Math.Max(0, _bitStreamMaxFirstVisibleRow);
            int rowDelta = (int)Math.Round((delta / trackTravel) * maxRow);
            _bitStreamFirstVisibleRow = Math.Clamp(_dragStartBitRow + rowDelta, 0, maxRow);
            InvalidateVisual();
            return;
        }

        if (DecompressionState is null || !_codeTableArea.Contains(p))
        {
            if (_hoveredCode.HasValue)
            {
                _hoveredCode = null;
                ToolTip = null;
            }
            return;
        }

        var keys = GetVisibleCodeKeys();
        if (keys.Count == 0)
        {
            return;
        }

        double contentY = p.Y - _codeTableArea.Y + _codeTableScrollOffset;
        int rowIndex = (int)(contentY / CodeTableRowHeight);
        if (rowIndex < 0 || rowIndex >= keys.Count)
        {
            if (_hoveredCode.HasValue)
            {
                _hoveredCode = null;
                ToolTip = null;
            }
            return;
        }

        int code = keys[rowIndex];
        if (_hoveredCode == code)
        {
            return;
        }

        _hoveredCode = code;
        if (DecompressionState.CodeTable.TryGetValue(code, out var bytes))
        {
            ToolTip = BuildEntryTooltip(code, bytes);
            return;
        }

        if (DecompressionState is not null)
        {
            if (code == DecompressionState.ClearCode)
                ToolTip = $"Code: {code}\nType: CLEAR\nResets dictionary to initial state.";
            else if (code == DecompressionState.EndOfInfoCode)
                ToolTip = $"Code: {code}\nType: EOI\nMarks end of compressed image data.";
            else
                ToolTip = null;
        }
    }

    protected override void OnMouseLeave(WpfMouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredCode = null;
        ToolTip = null;
    }

    protected override void OnMouseLeftButtonDown(WpfMouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);

        if (_codeTrackRect.Contains(p))
        {
            if (_codeThumbRect.Contains(p))
            {
                _isDraggingCodeScroll = true;
                _dragStartY = p.Y;
                _dragStartCodeOffset = _codeTableScrollOffset;
                CaptureMouse();
            }
            else
            {
                JumpCodeScrollToPosition(p.Y);
            }
            e.Handled = true;
            return;
        }

        if (_bitTrackRect.Contains(p))
        {
            if (_bitThumbRect.Contains(p))
            {
                _isDraggingBitScroll = true;
                _dragStartY = p.Y;
                _dragStartBitRow = _bitStreamFirstVisibleRow;
                CaptureMouse();
            }
            else
            {
                JumpBitScrollToPosition(p.Y);
            }
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(WpfMouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDraggingCodeScroll || _isDraggingBitScroll)
        {
            _isDraggingCodeScroll = false;
            _isDraggingBitScroll = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void DrawCodeTable(DrawingContext drawingContext, Rect area)
    {
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)), 1.0);
        border.Freeze();
        var bg = new SolidColorBrush(WpfColor.FromRgb(238, 242, 255));
        bg.Freeze();
        drawingContext.DrawRoundedRectangle(bg, border, area, 6, 6);

        if (DecompressionState is null || DecompressionState.CodeTable.Count == 0)
        {
            DrawText(drawingContext, "Code Table (empty)", area.X + 10, area.Y + 8, 12, WpfBrushes.SlateGray, FontWeights.SemiBold);
            return;
        }

        string header = ShowOnlyDynamicCodes ? "Code Table (new only)" : "Code Table";
        DrawText(drawingContext, header, area.X + 10, area.Y + 8, 12, WpfBrushes.SlateGray, FontWeights.SemiBold);

        const double headerHeight = 26;
        var rowsArea = new Rect(area.X + 6, area.Y + headerHeight, area.Width - 12, Math.Max(0, area.Height - headerHeight - 6));

        var keys = GetVisibleCodeKeys();
        int count = keys.Count;
        if (count == 0)
        {
            DrawText(drawingContext, "No dynamic entries yet.", rowsArea.X + 6, rowsArea.Y + 3, 11, WpfBrushes.Gray, FontWeights.Normal);
            return;
        }

        double totalHeight = count * CodeTableRowHeight;
        double maxOffset = Math.Max(0, totalHeight - rowsArea.Height);
        _codeMaxOffset = maxOffset;
        _codeTableScrollOffset = Math.Clamp(_codeTableScrollOffset, 0, maxOffset);

        int startIndex = Math.Max(0, (int)(_codeTableScrollOffset / CodeTableRowHeight));
        int visibleRows = Math.Max(1, (int)Math.Ceiling(rowsArea.Height / CodeTableRowHeight) + 1);
        int endIndex = Math.Min(count - 1, startIndex + visibleRows);

        drawingContext.PushClip(new RectangleGeometry(rowsArea));

        int firstDynamicCode = Math.Max(0, DecompressionState.InitialDictionarySize);
        int latestAddedCode = DecompressionState.NextAvailableCode - 1;
        int clearCode = DecompressionState.ClearCode;
        int endCode = DecompressionState.EndOfInfoCode;

        for (int i = startIndex; i <= endIndex; i++)
        {
            int code = keys[i];
            double y = rowsArea.Y + (i * CodeTableRowHeight) - _codeTableScrollOffset;
            var rowRect = new Rect(rowsArea.X, y, rowsArea.Width - 8, CodeTableRowHeight - 1);
            bool isServiceCode = code == clearCode || code == endCode;

            WpfBrush rowBrush = i % 2 == 0
                ? new SolidColorBrush(WpfColor.FromArgb(35, 148, 163, 184))
                : new SolidColorBrush(WpfColor.FromArgb(20, 148, 163, 184));

            if (isServiceCode)
            {
                rowBrush = new SolidColorBrush(WpfColor.FromArgb(125, 203, 213, 225));
            }
            else if (code >= firstDynamicCode)
            {
                rowBrush = new SolidColorBrush(WpfColor.FromArgb(95, 187, 247, 208));
            }

            if (!isServiceCode && code == latestAddedCode && latestAddedCode >= firstDynamicCode)
            {
                byte alpha = (byte)(120 + (GetPulse01() * 90));
                rowBrush = new SolidColorBrush(WpfColor.FromArgb(alpha, 251, 191, 36));
            }

            if (_hoveredCode == code)
            {
                rowBrush = new SolidColorBrush(WpfColor.FromArgb(165, 96, 165, 250));
            }

            drawingContext.DrawRectangle(rowBrush, null, rowRect);

            if (!DecompressionState.CodeTable.TryGetValue(code, out var bytes))
            {
                string serviceLabel = code == clearCode
                    ? $"{code,4} -> [CLEAR]"
                    : code == endCode
                        ? $"{code,4} -> [EOI]"
                        : $"{code,4} -> [N/A]";

                DrawText(
                    drawingContext,
                    serviceLabel,
                    rowRect.X + 6,
                    rowRect.Y + 3,
                    11,
                    WpfBrushes.Black,
                    FontWeights.SemiBold);
                continue;
            }

            string bytesText = string.Join(" ", bytes.Take(10).Select(b => b.ToString("X2")));
            if (bytes.Count > 10)
            {
                bytesText += " ...";
            }

            DrawText(
                drawingContext,
                $"{code,4} -> [{bytesText}]",
                rowRect.X + 6,
                rowRect.Y + 3,
                11,
                WpfBrushes.Black,
                FontWeights.Normal);
        }

        drawingContext.Pop();

        if (maxOffset > 0)
        {
            DrawCodeScrollBar(drawingContext, rowsArea, totalHeight, _codeTableScrollOffset);
        }
    }

    private void DrawCodeScrollBar(DrawingContext drawingContext, Rect rowsArea, double totalHeight, double scrollOffset)
    {
        const double barWidth = 6;
        var trackRect = new Rect(rowsArea.Right - barWidth, rowsArea.Y, barWidth, rowsArea.Height);
        _codeTrackRect = trackRect;
        var trackBrush = new SolidColorBrush(WpfColor.FromArgb(35, 51, 65, 85));
        trackBrush.Freeze();
        drawingContext.DrawRoundedRectangle(trackBrush, null, trackRect, 2, 2);

        double thumbHeight = Math.Max(22, rowsArea.Height * (rowsArea.Height / totalHeight));
        double maxThumbY = rowsArea.Height - thumbHeight;
        double thumbY = rowsArea.Y + (scrollOffset / (totalHeight - rowsArea.Height)) * maxThumbY;

        var thumbRect = new Rect(rowsArea.Right - barWidth, thumbY, barWidth, thumbHeight);
        _codeThumbRect = thumbRect;
        var thumbBrush = new SolidColorBrush(WpfColor.FromArgb(160, 51, 65, 85));
        thumbBrush.Freeze();
        drawingContext.DrawRoundedRectangle(thumbBrush, null, thumbRect, 2, 2);
    }

    private void DrawBitStream(DrawingContext drawingContext, Rect area)
    {
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)), 1.0);
        border.Freeze();
        var bg = new SolidColorBrush(WpfColor.FromRgb(236, 254, 255));
        bg.Freeze();
        drawingContext.DrawRoundedRectangle(bg, border, area, 6, 6);

        DrawText(drawingContext, "Bit Stream", area.X + 10, area.Y + 8, 12, WpfBrushes.SlateGray, FontWeights.SemiBold);

        byte[] data = CompressedData ?? [];
        if (data.Length == 0)
        {
            DrawText(drawingContext, "No compressed bytes", area.X + 10, area.Y + 30, 11, WpfBrushes.Gray, FontWeights.Normal);
            return;
        }

        if (DecompressionState is not null)
        {
            var hintRect = new Rect(area.X + 112, area.Y + 6, Math.Max(180, area.Width - 124), 18);
            var hintFill = new SolidColorBrush(WpfColor.FromArgb(220, 16, 185, 129));
            var hintBorder = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(4, 120, 87)), 1.0);
            hintFill.Freeze();
            hintBorder.Freeze();
            drawingContext.DrawRoundedRectangle(hintFill, hintBorder, hintRect, 4, 4);
            DrawText(
                drawingContext,
                $"Current window: bit[{DecompressionState.BitPosition}..{DecompressionState.BitPosition + DecompressionState.CodeSize - 1}] ({DecompressionState.CodeSize} bits)",
                hintRect.X + 6,
                hintRect.Y + 2,
                10,
                WpfBrushes.White,
                FontWeights.Bold,
                "Consolas");
        }

        var sampleChar = CreateFormattedText("0", 11, WpfBrushes.Black, FontWeights.Normal);
        double charWidth = sampleChar.WidthIncludingTrailingWhitespace;
        double rowHeight = sampleChar.Height + 2;

        const double leftPadding = 10;
        const double headerHeight = 30;
        const double footerHeight = 24;
        const double indexColumnChars = 7;
        double indexColumnWidth = indexColumnChars * charWidth;
        double textStartX = area.X + leftPadding + indexColumnWidth;
        double rowsTop = area.Y + headerHeight;
        double rowsHeight = Math.Max(0, area.Height - headerHeight - footerHeight - 8);
        double bitsAreaWidth = Math.Max(20, area.Width - (textStartX - area.X) - 10);

        int bytesPerRow = Math.Max(1, (int)(bitsAreaWidth / (charWidth * 9)));
        int bitsPerRow = bytesPerRow * 8;
        int totalBits = data.Length * 8;
        int totalRows = (int)Math.Ceiling(data.Length / (double)bytesPerRow);
        int visibleRows = Math.Max(1, (int)Math.Ceiling(rowsHeight / rowHeight));

        var clipRect = new Rect(area.X + 6, rowsTop, area.Width - 12, rowsHeight);
        drawingContext.PushClip(new RectangleGeometry(clipRect));

        int highlightStart = DecompressionState?.BitPosition ?? 0;
        int highlightEnd = highlightStart + ((DecompressionState?.CodeSize ?? 0) - 1);
        highlightStart = Math.Clamp(highlightStart, 0, Math.Max(0, totalBits - 1));
        highlightEnd = Math.Clamp(highlightEnd, highlightStart, Math.Max(highlightStart, totalBits - 1));

        _bitStreamMaxFirstVisibleRow = Math.Max(0, totalRows - visibleRows);
        _bitStreamFirstVisibleRow = Math.Clamp(_bitStreamFirstVisibleRow, 0, _bitStreamMaxFirstVisibleRow);
        int startRow = _bitStreamFirstVisibleRow;
        int endRow = Math.Min(totalRows - 1, startRow + visibleRows - 1);

        for (int row = startRow; row <= endRow; row++)
        {
            int rowStartBit = row * bitsPerRow;
            int rowEndBit = Math.Min(totalBits - 1, rowStartBit + bitsPerRow - 1);
            double y = rowsTop + (row - startRow) * rowHeight;

            int firstByte = row * bytesPerRow;
            DrawText(drawingContext, $"{firstByte * 8,5}:", area.X + leftPadding, y, 11, WpfBrushes.SlateGray, FontWeights.Normal, "Consolas");

            var lineBuilder = new System.Text.StringBuilder(bytesPerRow * 9);
            for (int b = 0; b < bytesPerRow; b++)
            {
                int byteIndex = firstByte + b;
                if (byteIndex >= data.Length)
                {
                    break;
                }

                lineBuilder.Append(Convert.ToString(data[byteIndex], 2).PadLeft(8, '0'));
                if (b < bytesPerRow - 1)
                {
                    lineBuilder.Append(' ');
                }
            }

            DrawText(drawingContext, lineBuilder.ToString(), textStartX, y, 11, WpfBrushes.Black, FontWeights.Normal, "Consolas");

            if (highlightEnd < rowStartBit || highlightStart > rowEndBit || DecompressionState is null)
            {
                continue;
            }

            int localStart = Math.Max(highlightStart, rowStartBit) - rowStartBit;
            int localEnd = Math.Min(highlightEnd, rowEndBit) - rowStartBit;

            double xStart = textStartX + GetBitTextOffset(localStart, charWidth);
            double xEnd = textStartX + GetBitTextOffset(localEnd, charWidth) + charWidth;
            var highlightRect = new Rect(xStart - 1, y, Math.Max(2, xEnd - xStart + 2), rowHeight - 1);

            byte pulseAlpha = (byte)(95 + (GetPulse01() * 120));
            var highlightBrush = new SolidColorBrush(WpfColor.FromArgb(pulseAlpha, 251, 191, 36));
            highlightBrush.Freeze();
            drawingContext.DrawRectangle(highlightBrush, null, highlightRect);

            var boundaryPen = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(220, 38, 38)), 1.2);
            boundaryPen.Freeze();
            drawingContext.DrawLine(boundaryPen, new WpfPoint(xStart, y), new WpfPoint(xStart, y + rowHeight - 1));
            drawingContext.DrawLine(boundaryPen, new WpfPoint(xEnd, y), new WpfPoint(xEnd, y + rowHeight - 1));

            var markerPen = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(3, 105, 161)), 1.0);
            markerPen.Freeze();
            drawingContext.DrawLine(markerPen, new WpfPoint(xStart, y - 2), new WpfPoint(xStart, y + rowHeight + 2));
            var bitLabelRect = new Rect(xStart + 2, y - rowHeight - 1, 72, rowHeight - 1);
            var bitLabelFill = new SolidColorBrush(WpfColor.FromArgb(235, 16, 185, 129));
            var bitLabelBorder = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(4, 120, 87)), 1);
            bitLabelFill.Freeze();
            bitLabelBorder.Freeze();
            drawingContext.DrawRoundedRectangle(bitLabelFill, bitLabelBorder, bitLabelRect, 3, 3);
            DrawText(drawingContext, $"bit {highlightStart}", bitLabelRect.X + 5, bitLabelRect.Y + 1, 10, WpfBrushes.White, FontWeights.Bold, "Consolas");
        }

        drawingContext.Pop();

        if (_bitStreamMaxFirstVisibleRow > 0)
        {
            DrawDiscreteScrollBar(drawingContext, clipRect, _bitStreamFirstVisibleRow, _bitStreamMaxFirstVisibleRow);
        }

        if (DecompressionState is not null)
        {
            var footerRect = new Rect(area.X + 6, area.Bottom - footerHeight, area.Width - 12, footerHeight - 4);
            var footerFill = new SolidColorBrush(WpfColor.FromArgb(215, 220, 252, 231));
            var footerBorder = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(5, 150, 105)), 0.8);
            footerFill.Freeze();
            footerBorder.Freeze();
            drawingContext.DrawRoundedRectangle(footerFill, footerBorder, footerRect, 4, 4);

            DrawText(
                drawingContext,
                $"Code size: {DecompressionState.CodeSize} bits | Range: [{highlightStart}..{highlightEnd}]",
                footerRect.X + 6,
                footerRect.Y + 3,
                10,
                WpfBrushes.DarkGreen,
                FontWeights.Bold,
                "Consolas");
        }
    }

    private void DrawOutputBuffer(DrawingContext drawingContext, Rect area)
    {
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)), 1.0);
        border.Freeze();
        var bg = new SolidColorBrush(WpfColor.FromRgb(236, 253, 245));
        bg.Freeze();
        drawingContext.DrawRoundedRectangle(bg, border, area, 6, 6);

        DrawText(drawingContext, "Output Buffer", area.X + 10, area.Y + 8, 12, WpfBrushes.SlateGray, FontWeights.SemiBold);

        if (DecompressionState is null)
        {
            DrawText(drawingContext, "No LZW state.", area.X + 10, area.Y + 30, 11, WpfBrushes.Gray, FontWeights.Normal);
            return;
        }

        var output = DecompressionState.OutputBuffer;
        DrawText(
            drawingContext,
            $"Bytes: {output.Count} | Tail view shows latest decoded bytes",
            area.X + 10,
            area.Y + 26,
            10,
            WpfBrushes.DimGray,
            FontWeights.Normal);

        var rowsArea = new Rect(area.X + 8, area.Y + 46, area.Width - 16, Math.Max(0, area.Height - 54));
        drawingContext.PushClip(new RectangleGeometry(rowsArea));

        if (output.Count == 0)
        {
            DrawText(drawingContext, "(empty)", rowsArea.X + 4, rowsArea.Y + 2, 11, WpfBrushes.Gray, FontWeights.Normal, "Consolas");
            drawingContext.Pop();
            return;
        }

        int bytesPerRow = 16;
        int totalRows = (int)Math.Ceiling(output.Count / (double)bytesPerRow);
        int maxVisibleRows = Math.Max(1, (int)(rowsArea.Height / OutputRowHeight));
        int startRow = Math.Max(0, totalRows - maxVisibleRows);
        int lastHighlightedFrom = Math.Max(0, output.Count - bytesPerRow);

        for (int row = startRow; row < totalRows; row++)
        {
            int offset = row * bytesPerRow;
            int count = Math.Min(bytesPerRow, output.Count - offset);
            double y = rowsArea.Y + ((row - startRow) * OutputRowHeight);
            var rowRect = new Rect(rowsArea.X, y, rowsArea.Width, OutputRowHeight - 1);

            if (offset >= lastHighlightedFrom)
            {
                byte alpha = (byte)(90 + (GetPulse01() * 80));
                var rowHighlight = new SolidColorBrush(WpfColor.FromArgb(alpha, 167, 243, 208));
                rowHighlight.Freeze();
                drawingContext.DrawRectangle(rowHighlight, null, rowRect);
            }

            string hex = string.Join(" ", output.Skip(offset).Take(count).Select(b => b.ToString("X2")));
            string ascii = new(output.Skip(offset).Take(count).Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());
            DrawText(
                drawingContext,
                $"{offset,6:X4}: {hex,-47} | {ascii}",
                rowRect.X + 4,
                rowRect.Y + 2,
                10,
                WpfBrushes.Black,
                FontWeights.Normal,
                "Consolas");
        }

        drawingContext.Pop();
    }

    private void DrawStepDetails(DrawingContext drawingContext, Rect area)
    {
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)), 1.0);
        border.Freeze();
        var bg = new SolidColorBrush(WpfColor.FromRgb(254, 243, 199));
        bg.Freeze();
        drawingContext.DrawRoundedRectangle(bg, border, area, 6, 6);

        DrawText(drawingContext, "Step Details", area.X + 10, area.Y + 8, 12, WpfBrushes.SlateGray, FontWeights.SemiBold);

        if (DecompressionState is null)
        {
            DrawText(drawingContext, "No LZW state.", area.X + 10, area.Y + 30, 11, WpfBrushes.Gray, FontWeights.Normal);
            return;
        }

        int inferredMinCodeSize = InferMinCodeSize(DecompressionState.ClearCode);
        string actionText = GetActionAcademicHint(DecompressionState.CurrentAction);
        var viewport = new Rect(area.X + 8, area.Y + 28, area.Width - 16, Math.Max(0, area.Height - 36));
        drawingContext.PushClip(new RectangleGeometry(viewport));

        double y = viewport.Y + 2 - _stepDetailsScrollOffset;
        var actionRect = new Rect(viewport.X + 1, y, viewport.Width - 2, 18);
        var actionFill = new SolidColorBrush(WpfColor.FromArgb((byte)(70 + (GetPulse01() * 60)), 252, 211, 77));
        actionFill.Freeze();
        drawingContext.DrawRoundedRectangle(actionFill, null, actionRect, 4, 4);
        DrawText(drawingContext, $"Action: {DecompressionState.CurrentAction}", viewport.X + 4, y + 2, 11, WpfBrushes.Black, FontWeights.SemiBold);
        y += 24;

        DrawText(drawingContext, $"Step: {DecompressionState.Step} | Completed: {DecompressionState.IsComplete}", viewport.X + 2, y, 10, WpfBrushes.Black, FontWeights.Normal);
        y += 16;
        DrawText(drawingContext, $"CurrentCode: {DecompressionState.CurrentCode} | PreviousCode: {DecompressionState.PreviousCode}", viewport.X + 2, y, 10, WpfBrushes.Black, FontWeights.Normal);
        y += 16;
        DrawText(drawingContext, $"CodeSize: {DecompressionState.CodeSize} bits | BitPosition: {DecompressionState.BitPosition}", viewport.X + 2, y, 10, WpfBrushes.Black, FontWeights.Normal);
        y += 16;
        DrawText(drawingContext, $"Clear: {DecompressionState.ClearCode} | EOI: {DecompressionState.EndOfInfoCode} | Next: {DecompressionState.NextAvailableCode}", viewport.X + 2, y, 10, WpfBrushes.Black, FontWeights.Normal);
        y += 16;
        DrawText(drawingContext, $"Dictionary size: {DecompressionState.CodeTable.Count} | Output: {DecompressionState.OutputBuffer.Count} bytes", viewport.X + 2, y, 10, WpfBrushes.Black, FontWeights.Normal);
        y += 18;
        DrawText(drawingContext, $"Formula: clear=2^minCodeSize => minCodeSize={inferredMinCodeSize}", viewport.X + 2, y, 10, WpfBrushes.DarkSlateBlue, FontWeights.SemiBold);
        y += 20;

        double descHeight = DrawWrappedTextMeasure(
            drawingContext,
            $"Step description: {DecompressionState.StepDescription}",
            new Rect(viewport.X + 2, y, viewport.Width - 4, 80),
            10,
            WpfBrushes.Black,
            FontWeights.Normal);
        y += descHeight + 8;

        DrawText(drawingContext, "Compressed source (Hex/Image Data): GIF LZW sub-block bytes.", viewport.X + 2, y, 10, WpfBrushes.Maroon, FontWeights.SemiBold);
        y += 16;
        DrawText(drawingContext, "Decompressed output (Output Buffer): palette indices after LZW.", viewport.X + 2, y, 10, WpfBrushes.DarkSlateBlue, FontWeights.SemiBold);
        y += 20;

        double hintHeight = DrawWrappedTextMeasure(
            drawingContext,
            $"Academic hint: {actionText}",
            new Rect(viewport.X + 2, y, viewport.Width - 4, 220),
            10,
            WpfBrushes.DarkSlateGray,
            FontWeights.Normal);
        y += hintHeight + 4;

        drawingContext.Pop();
        double contentHeight = Math.Max(0, y - viewport.Y);
        _stepDetailsMaxScrollOffset = Math.Max(0, contentHeight - viewport.Height);
        _stepDetailsScrollOffset = Math.Clamp(_stepDetailsScrollOffset, 0, _stepDetailsMaxScrollOffset);
        if (_stepDetailsMaxScrollOffset > 0)
        {
            DrawContinuousScrollBar(drawingContext, viewport, _stepDetailsScrollOffset, _stepDetailsMaxScrollOffset);
        }
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double x,
        double y,
        double size,
        WpfBrush brush,
        FontWeight weight,
        string fontFamily = "Segoe UI")
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);

        drawingContext.DrawText(formattedText, new WpfPoint(x, y));
    }

    private FormattedText CreateFormattedText(string text, double size, WpfBrush brush, FontWeight weight)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Consolas"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);
    }

    private double DrawWrappedTextMeasure(
        DrawingContext drawingContext,
        string text,
        Rect area,
        double size,
        WpfBrush brush,
        FontWeight weight)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return 0;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = area.Width,
            MaxTextHeight = area.Height,
            Trimming = TextTrimming.CharacterEllipsis
        };

        drawingContext.DrawText(formattedText, new WpfPoint(area.X, area.Y));
        return formattedText.Height;
    }

    private static double GetBitTextOffset(int bitIndexInRow, double charWidth)
    {
        int byteIndex = bitIndexInRow / 8;
        return (bitIndexInRow * charWidth) + (byteIndex * charWidth);
    }

    private static string BuildEntryTooltip(int code, IReadOnlyList<byte> bytes)
    {
        string hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
        string ascii = new string(bytes.Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());
        return $"Code: {code}\nLength: {bytes.Count}\nHex: {hex}\nASCII: {ascii}";
    }

    private List<int> GetVisibleCodeKeys()
    {
        if (DecompressionState is null || DecompressionState.CodeTable.Count == 0)
            return [];

        IEnumerable<int> keys = DecompressionState.CodeTable.Keys;
        var list = keys.ToList();
        if (!list.Contains(DecompressionState.ClearCode))
            list.Add(DecompressionState.ClearCode);
        if (!list.Contains(DecompressionState.EndOfInfoCode))
            list.Add(DecompressionState.EndOfInfoCode);

        if (ShowOnlyDynamicCodes)
        {
            int firstDynamicCode = Math.Max(0, DecompressionState.InitialDictionarySize);
            list = list.Where(k => k >= firstDynamicCode || k == DecompressionState.ClearCode || k == DecompressionState.EndOfInfoCode).ToList();
        }

        return list.OrderBy(k => k).ToList();
    }

    private static int InferMinCodeSize(int clearCode)
    {
        if (clearCode <= 0 || (clearCode & (clearCode - 1)) != 0)
            return -1;

        int size = 0;
        while (clearCode > 1)
        {
            clearCode >>= 1;
            size++;
        }
        return size;
    }

    private static string GetActionAcademicHint(LZWAction action) =>
        action switch
        {
            LZWAction.Initialize => "Initialize dictionary with single-byte symbols and reserve Clear/EOI codes.",
            LZWAction.ReadCode => "Read next variable-length code from LSB-first bit stream.",
            LZWAction.ProcessClearCode => "On Clear code, reset dictionary and code-size growth state.",
            LZWAction.ProcessExistingCode => "Emit dictionary entry for code K, then add prev + first(K).",
            LZWAction.ProcessNewCode => "Special case K==nextCode: emit prev + first(prev) (KwKwK rule).",
            LZWAction.AddToCodeTable => "Append newly formed sequence to dictionary at next free code.",
            LZWAction.OutputData => "Write decoded sequence bytes to output buffer.",
            LZWAction.Complete => "Stop at EOI or when no complete code remains in bit stream.",
            _ => "N/A"
        };

    private void DrawDiscreteScrollBar(DrawingContext drawingContext, Rect viewport, int firstVisible, int maxFirstVisible)
    {
        if (maxFirstVisible <= 0)
            return;

        const double width = 6;
        var track = new Rect(viewport.Right - width, viewport.Y, width, viewport.Height);
        _bitTrackRect = track;
        var trackBrush = new SolidColorBrush(WpfColor.FromArgb(36, 100, 116, 139));
        trackBrush.Freeze();
        drawingContext.DrawRoundedRectangle(trackBrush, null, track, 2, 2);

        double thumbHeight = Math.Max(24, viewport.Height * (viewport.Height / (viewport.Height + maxFirstVisible * 16.0)));
        double maxY = viewport.Height - thumbHeight;
        double t = maxFirstVisible == 0 ? 0 : firstVisible / (double)maxFirstVisible;
        double y = viewport.Y + (t * maxY);

        var thumb = new Rect(viewport.Right - width, y, width, thumbHeight);
        _bitThumbRect = thumb;
        var thumbBrush = new SolidColorBrush(WpfColor.FromArgb(170, 51, 65, 85));
        thumbBrush.Freeze();
        drawingContext.DrawRoundedRectangle(thumbBrush, null, thumb, 2, 2);
    }

    private static void DrawContinuousScrollBar(DrawingContext drawingContext, Rect viewport, double scrollOffset, double maxOffset)
    {
        if (maxOffset <= 0)
            return;

        const double width = 6;
        var track = new Rect(viewport.Right - width, viewport.Y, width, viewport.Height);
        var trackBrush = new SolidColorBrush(WpfColor.FromArgb(36, 100, 116, 139));
        trackBrush.Freeze();
        drawingContext.DrawRoundedRectangle(trackBrush, null, track, 2, 2);

        double thumbHeight = Math.Max(24, viewport.Height * (viewport.Height / (viewport.Height + maxOffset)));
        double maxY = viewport.Height - thumbHeight;
        double y = viewport.Y + ((scrollOffset / maxOffset) * maxY);
        var thumb = new Rect(viewport.Right - width, y, width, thumbHeight);
        var thumbBrush = new SolidColorBrush(WpfColor.FromArgb(170, 51, 65, 85));
        thumbBrush.Freeze();
        drawingContext.DrawRoundedRectangle(thumbBrush, null, thumb, 2, 2);
    }

    private static void DrawErrorBanner(DrawingContext drawingContext, Rect rect, string error)
    {
        var fill = new SolidColorBrush(WpfColor.FromRgb(254, 226, 226));
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(220, 38, 38)), 1.0);
        fill.Freeze();
        border.Freeze();
        drawingContext.DrawRoundedRectangle(fill, border, rect, 6, 6);

        var text = new FormattedText(
            $"Error: {error}",
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            11,
            new SolidColorBrush(WpfColor.FromRgb(153, 27, 27)),
            1.0)
        {
            MaxTextWidth = Math.Max(0, rect.Width - 14),
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawingContext.DrawText(text, new WpfPoint(rect.X + 7, rect.Y + 6));
    }

    private void DrawEmptyState(DrawingContext drawingContext, Rect rect)
    {
        var fill = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249));
        var border = new WpfPen(new SolidColorBrush(WpfColor.FromRgb(148, 163, 184)), 1.0);
        fill.Freeze();
        border.Freeze();
        drawingContext.DrawRoundedRectangle(fill, border, rect, 8, 8);

        DrawText(drawingContext, "LZW Visualization", rect.X + 18, rect.Y + 18, 18, WpfBrushes.SlateGray, FontWeights.Bold);
        DrawText(drawingContext, "Start visualization from the 'LZW Decompression' tab.", rect.X + 18, rect.Y + 50, 12, WpfBrushes.DimGray, FontWeights.Normal);
        DrawText(drawingContext, "Then use Step/Play controls to observe dictionary growth and bit decoding.", rect.X + 18, rect.Y + 72, 11, WpfBrushes.DimGray, FontWeights.Normal);
    }

    private static double GetPulse01()
    {
        double t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        return (Math.Sin(t * 3.0) + 1.0) * 0.5;
    }

    private static void DrawPlaceholderRect(
        DrawingContext drawingContext,
        WpfBrush fill,
        WpfPen border,
        Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        drawingContext.DrawRoundedRectangle(fill, border, rect, 6, 6);
    }

    private void JumpCodeScrollToPosition(double mouseY)
    {
        if (_codeMaxOffset <= 0 || _codeTrackRect.Height <= _codeThumbRect.Height)
            return;

        double desiredThumbTop = Math.Clamp(mouseY - (_codeThumbRect.Height / 2), _codeTrackRect.Y, _codeTrackRect.Bottom - _codeThumbRect.Height);
        double t = (desiredThumbTop - _codeTrackRect.Y) / Math.Max(1, _codeTrackRect.Height - _codeThumbRect.Height);
        _codeTableScrollOffset = t * _codeMaxOffset;
        InvalidateVisual();
    }

    private void JumpBitScrollToPosition(double mouseY)
    {
        if (_bitStreamMaxFirstVisibleRow <= 0 || _bitTrackRect.Height <= _bitThumbRect.Height)
            return;

        double desiredThumbTop = Math.Clamp(mouseY - (_bitThumbRect.Height / 2), _bitTrackRect.Y, _bitTrackRect.Bottom - _bitThumbRect.Height);
        double t = (desiredThumbTop - _bitTrackRect.Y) / Math.Max(1, _bitTrackRect.Height - _bitThumbRect.Height);
        _bitStreamFirstVisibleRow = (int)Math.Round(t * _bitStreamMaxFirstVisibleRow);
        InvalidateVisual();
    }
}

