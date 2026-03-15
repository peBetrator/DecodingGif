using System.Collections.ObjectModel;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace DecodingGif.Core.Models;

public sealed class MemoryLayoutVisualization
{
    public int FileSize { get; init; }
    public int BytesPerRow { get; init; } = 48;
    public int TotalRows => FileSize <= 0 ? 0 : (int)Math.Ceiling(FileSize / (double)BytesPerRow);
    public ObservableCollection<MemoryLayoutRow> Rows { get; } = new();
}

public sealed class MemoryLayoutRow
{
    public int RowIndex { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public bool IsCollapsedSummary { get; init; }
    public int CollapsedRowCount { get; init; }
    public ObservableCollection<MemoryLayoutBlock> Blocks { get; } = new();
    public int ActualDataBytes => Blocks.Sum(b => b.Length);
    public int EmptyBytes => Math.Max(0, (EndOffset - StartOffset + 1) - ActualDataBytes);
}

public sealed class MemoryLayoutBlock
{
    public GifBlockKind BlockType { get; init; }
    public int StartOffset { get; init; }
    public int Length { get; init; }
    public int FullStartOffset { get; init; }
    public int FullLength { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SizeInfo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public MediaBrush BackgroundBrush { get; init; } = MediaBrushes.Transparent;
    public bool IsCompressed { get; init; }
    public double RelativeStart { get; init; }
    public double RelativeWidth { get; init; }
    public BlockPerformanceMetrics? PerformanceMetrics { get; init; }
    public string AnimationInfo { get; init; } = string.Empty;
}
