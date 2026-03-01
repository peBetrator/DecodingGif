using System.Collections.ObjectModel;
using System.Windows.Media;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace DecodingGif.Core.Services;

public sealed class MemoryLayoutBuilder
{
    private static readonly Dictionary<(GifBlockKind Kind, PerformanceTier Tier), MediaBrush> BlockBrushCache = new();
    private static readonly MediaBrush EmptySpaceBrush = CreatePerformanceBrush(MediaColor.FromRgb(243, 244, 246), 1.0, warningTint: false);

    public MemoryLayoutVisualization BuildLayout(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        int bytesPerRow = 48,
        bool showEmptySpace = true,
        bool compressLargeBlocks = true,
        IReadOnlyDictionary<BlockPerformanceKey, BlockPerformanceMetrics>? performance = null)
    {
        int rowSize = Math.Max(8, bytesPerRow);
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        var layout = new MemoryLayoutVisualization
        {
            FileSize = file.Bytes.Length,
            BytesPerRow = rowSize
        };

        if (layout.TotalRows == 0)
            return layout;

        var rawRows = CreateRows(layout.TotalRows, rowSize, layout.FileSize);
        PopulateRowsWithBlocks(rawRows, ordered, rowSize, compressLargeBlocks, performance);
        if (showEmptySpace)
            AppendTrailingEmptySegments(rawRows, rowSize);

        var displayRows = compressLargeBlocks ? CollapseRepetitiveRows(rawRows, rowSize) : rawRows;
        for (int i = 0; i < displayRows.Count; i++)
        {
            var src = displayRows[i];
            var normalized = new MemoryLayoutRow
            {
                RowIndex = i,
                StartOffset = src.StartOffset,
                EndOffset = src.EndOffset,
                IsCollapsedSummary = src.IsCollapsedSummary,
                CollapsedRowCount = src.CollapsedRowCount
            };
            foreach (var block in src.Blocks)
                normalized.Blocks.Add(block);
            layout.Rows.Add(normalized);
        }

        return layout;
    }

    private static List<MemoryLayoutRow> CreateRows(int totalRows, int bytesPerRow, int fileSize)
    {
        var rows = new List<MemoryLayoutRow>(totalRows);
        for (int rowIndex = 0; rowIndex < totalRows; rowIndex++)
        {
            int startOffset = rowIndex * bytesPerRow;
            int endOffset = Math.Min(startOffset + bytesPerRow - 1, fileSize - 1);
            rows.Add(new MemoryLayoutRow
            {
                RowIndex = rowIndex,
                StartOffset = startOffset,
                EndOffset = endOffset
            });
        }

        return rows;
    }

    private static void PopulateRowsWithBlocks(
        IReadOnlyList<MemoryLayoutRow> rows,
        IReadOnlyList<GifByteRange> orderedBlocks,
        int bytesPerRow,
        bool compressLargeBlocks,
        IReadOnlyDictionary<BlockPerformanceKey, BlockPerformanceMetrics>? performance)
    {
        if (rows.Count == 0)
            return;

        int maxRow = rows.Count - 1;
        foreach (var block in orderedBlocks)
        {
            if (block.Length <= 0)
                continue;

            int blockStartRow = Math.Clamp(block.Start / bytesPerRow, 0, maxRow);
            int blockEndRow = Math.Clamp(block.EndInclusive / bytesPerRow, 0, maxRow);
            for (int rowIndex = blockStartRow; rowIndex <= blockEndRow; rowIndex++)
            {
                var row = rows[rowIndex];
                if (block.Start > row.EndOffset || block.EndInclusive < row.StartOffset)
                    continue;
                row.Blocks.Add(CreateMemoryLayoutBlock(block, row.StartOffset, row.EndOffset, bytesPerRow, compressLargeBlocks, performance));
            }
        }
    }

    private static void AppendTrailingEmptySegments(IReadOnlyList<MemoryLayoutRow> rows, int bytesPerRow)
    {
        foreach (var row in rows)
        {
            if (row.Blocks.Count == 0)
                continue;

            int rowSpan = row.EndOffset - row.StartOffset + 1;
            int covered = row.Blocks.Sum(b => b.Length);
            int empty = Math.Max(0, rowSpan - covered);
            if (empty <= 0)
                continue;

            int emptyStart = row.EndOffset - empty + 1;
            row.Blocks.Add(new MemoryLayoutBlock
            {
                BlockType = GifBlockKind.Unknown,
                StartOffset = emptyStart,
                Length = empty,
                FullStartOffset = emptyStart,
                FullLength = empty,
                Title = "Empty",
                SizeInfo = $"{empty}B",
                FullName = "Unused/empty space",
                BackgroundBrush = EmptySpaceBrush,
                RelativeStart = (double)(emptyStart - row.StartOffset) / bytesPerRow,
                RelativeWidth = (double)empty / bytesPerRow
            });
        }
    }

    private static MemoryLayoutBlock CreateMemoryLayoutBlock(
        GifByteRange block,
        int rowStart,
        int rowEnd,
        int bytesPerRow,
        bool compressLargeBlocks,
        IReadOnlyDictionary<BlockPerformanceKey, BlockPerformanceMetrics>? performance)
    {
        int blockStart = Math.Max(block.Start, rowStart);
        int blockEnd = Math.Min(block.EndInclusive, rowEnd);
        int visibleLength = blockEnd - blockStart + 1;

        double relativeStart = (double)(blockStart - rowStart) / bytesPerRow;
        double relativeWidth = (double)visibleLength / bytesPerRow;

        bool isCompressed = compressLargeBlocks && block.Length > bytesPerRow;
        string title = ShortTitle(block.Kind, isCompressed);
        BlockPerformanceMetrics? perf = null;
        if (performance is not null)
            performance.TryGetValue(BlockPerformanceKey.FromRange(block), out perf);

        return new MemoryLayoutBlock
        {
            BlockType = block.Kind,
            StartOffset = blockStart,
            Length = visibleLength,
            FullStartOffset = block.Start,
            FullLength = block.Length,
            Title = title,
            SizeInfo = $"{visibleLength}B",
            FullName = block.Name,
            BackgroundBrush = GetBlockBrush(block.Kind, perf?.Tier ?? PerformanceTier.Good),
            RelativeStart = relativeStart,
            RelativeWidth = relativeWidth,
            IsCompressed = isCompressed,
            PerformanceMetrics = perf
        };
    }

    private static string ShortTitle(GifBlockKind kind, bool compressed) =>
        kind switch
        {
            GifBlockKind.Header => "HDR",
            GifBlockKind.LogicalScreenDescriptor => "LSD",
            GifBlockKind.GlobalColorTable => compressed ? "GCT..." : "GCT",
            GifBlockKind.GraphicControlExtension => "GCE",
            GifBlockKind.ApplicationExtension => "APP",
            GifBlockKind.ImageDescriptor => "ID",
            GifBlockKind.LocalColorTable => compressed ? "LCT..." : "LCT",
            GifBlockKind.ImageData => compressed ? "IMG..." : "IMG",
            GifBlockKind.Trailer => "END",
            _ => "UNK"
        };

    private static MediaBrush GetBlockBrush(GifBlockKind kind, PerformanceTier tier)
    {
        if (BlockBrushCache.TryGetValue((kind, tier), out var cached))
            return cached;

        var baseColor = BlockColorPalette.Get(kind).Color;
        double brightness = tier switch
        {
            PerformanceTier.Good => 1.0,
            PerformanceTier.Moderate => 0.7,
            PerformanceTier.Poor => 0.4,
            _ => 1.0
        };

        var brush = CreatePerformanceBrush(baseColor, brightness, tier == PerformanceTier.Poor);
        BlockBrushCache[(kind, tier)] = brush;
        return brush;
    }

    private static MediaBrush CreatePerformanceBrush(MediaColor baseColor, double brightness, bool warningTint)
    {
        int r = (int)Math.Round(baseColor.R * brightness);
        int g = (int)Math.Round(baseColor.G * brightness);
        int b = (int)Math.Round(baseColor.B * brightness);

        if (warningTint)
        {
            r = (int)Math.Round((r * 0.65) + (255 * 0.35));
            g = (int)Math.Round((g * 0.80) + (64 * 0.20));
            b = (int)Math.Round(b * 0.72);
        }

        var brush = new SolidColorBrush(MediaColor.FromArgb(
            210,
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255)));
        brush.Freeze();
        return brush;
    }

    private static List<MemoryLayoutRow> CollapseRepetitiveRows(IReadOnlyList<MemoryLayoutRow> rows, int bytesPerRow)
    {
        if (rows.Count < 4)
            return rows.ToList();

        var result = new List<MemoryLayoutRow>(rows.Count);
        int i = 0;
        while (i < rows.Count)
        {
            if (!TryFindUniformRun(rows, i, bytesPerRow, out int runEnd))
            {
                result.Add(rows[i]);
                i++;
                continue;
            }

            int runLength = runEnd - i + 1;
            if (runLength < 3)
            {
                result.Add(rows[i]);
                i++;
                continue;
            }

            result.Add(rows[i]);
            int omittedStart = i + 1;
            int omittedEnd = runEnd - 1;
            if (omittedStart <= omittedEnd)
                result.Add(BuildCollapsedSummaryRow(rows, omittedStart, omittedEnd));
            result.Add(rows[runEnd]);
            i = runEnd + 1;
        }

        return result;
    }

    private static bool TryFindUniformRun(IReadOnlyList<MemoryLayoutRow> rows, int startIndex, int bytesPerRow, out int runEnd)
    {
        runEnd = startIndex;
        if (!IsUniformSingleBlockRow(rows[startIndex], bytesPerRow, out var template))
            return false;

        int index = startIndex + 1;
        while (index < rows.Count && IsUniformSingleBlockRow(rows[index], bytesPerRow, out var candidate))
        {
            if (candidate.BlockType != template.BlockType)
                break;
            if (candidate.FullStartOffset != template.FullStartOffset || candidate.FullLength != template.FullLength)
                break;
            if (rows[index].StartOffset != rows[index - 1].StartOffset + bytesPerRow)
                break;
            runEnd = index;
            index++;
        }

        return runEnd > startIndex;
    }

    private static bool IsUniformSingleBlockRow(MemoryLayoutRow row, int bytesPerRow, out MemoryLayoutBlock block)
    {
        block = default!;
        if (row.Blocks.Count != 1)
            return false;

        block = row.Blocks[0];
        int rowSpan = row.EndOffset - row.StartOffset + 1;
        if (rowSpan != bytesPerRow)
            return false;
        if (block.BlockType == GifBlockKind.Unknown)
            return false;
        if (block.StartOffset != row.StartOffset)
            return false;
        if (block.Length != rowSpan)
            return false;
        if (block.RelativeStart > 0.0001 || block.RelativeWidth < 0.999)
            return false;
        return true;
    }

    private static MemoryLayoutRow BuildCollapsedSummaryRow(IReadOnlyList<MemoryLayoutRow> rows, int startIndex, int endIndex)
    {
        var first = rows[startIndex];
        var last = rows[endIndex];
        var template = first.Blocks[0];
        int startOffset = first.StartOffset;
        int endOffset = last.EndOffset;
        int span = Math.Max(1, endOffset - startOffset + 1);
        int collapsedRows = Math.Max(1, endIndex - startIndex + 1);

        var row = new MemoryLayoutRow
        {
            StartOffset = startOffset,
            EndOffset = endOffset,
            IsCollapsedSummary = true,
            CollapsedRowCount = collapsedRows
        };

        row.Blocks.Add(new MemoryLayoutBlock
        {
            BlockType = template.BlockType,
            StartOffset = startOffset,
            Length = span,
            FullStartOffset = template.FullStartOffset,
            FullLength = template.FullLength,
            Title = "...",
            SizeInfo = $"{span:N0}B",
            FullName = $"{template.FullName} (collapsed {collapsedRows} rows)",
            BackgroundBrush = template.BackgroundBrush,
            IsCompressed = true,
            RelativeStart = 0.0,
            RelativeWidth = 1.0
        });

        return row;
    }
}
