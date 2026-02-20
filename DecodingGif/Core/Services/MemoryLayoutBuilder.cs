using System.Collections.ObjectModel;
using System.Windows.Media;
using DecodingGif.Core.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace DecodingGif.Core.Services;

public sealed class MemoryLayoutBuilder
{
    public MemoryLayoutVisualization BuildLayout(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        int bytesPerRow = 48,
        bool showEmptySpace = true,
        bool compressLargeBlocks = true)
    {
        int rowSize = Math.Max(8, bytesPerRow);
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        var layout = new MemoryLayoutVisualization
        {
            FileSize = file.Bytes.Length,
            BytesPerRow = rowSize
        };

        for (int rowIndex = 0; rowIndex < layout.TotalRows; rowIndex++)
        {
            var row = BuildLayoutRow(rowIndex, rowSize, layout.FileSize, ordered, showEmptySpace, compressLargeBlocks);
            layout.Rows.Add(row);
        }

        return layout;
    }

    private static MemoryLayoutRow BuildLayoutRow(
        int rowIndex,
        int bytesPerRow,
        int fileSize,
        IReadOnlyList<GifByteRange> allBlocks,
        bool showEmptySpace,
        bool compressLargeBlocks)
    {
        int startOffset = rowIndex * bytesPerRow;
        int endOffset = Math.Min(startOffset + bytesPerRow - 1, fileSize - 1);
        var row = new MemoryLayoutRow
        {
            RowIndex = rowIndex,
            StartOffset = startOffset,
            EndOffset = endOffset
        };

        var intersecting = allBlocks.Where(b => b.Start <= endOffset && b.EndInclusive >= startOffset).ToList();
        foreach (var block in intersecting)
        {
            row.Blocks.Add(CreateMemoryLayoutBlock(block, startOffset, endOffset, bytesPerRow, compressLargeBlocks));
        }

        if (showEmptySpace && row.Blocks.Count > 0)
        {
            int covered = row.Blocks.Sum(b => b.Length);
            int empty = Math.Max(0, (endOffset - startOffset + 1) - covered);
            if (empty > 0)
            {
                row.Blocks.Add(new MemoryLayoutBlock
                {
                    BlockType = GifBlockKind.Unknown,
                    StartOffset = endOffset - empty + 1,
                    Length = empty,
                    FullStartOffset = endOffset - empty + 1,
                    FullLength = empty,
                    Title = "Empty",
                    SizeInfo = $"{empty}B",
                    FullName = "Unused/empty space",
                    BackgroundBrush = CreateBrush("#f3f4f6"),
                    RelativeStart = (double)(endOffset - empty + 1 - startOffset) / bytesPerRow,
                    RelativeWidth = (double)empty / bytesPerRow
                });
            }
        }

        return row;
    }

    private static MemoryLayoutBlock CreateMemoryLayoutBlock(
        GifByteRange block,
        int rowStart,
        int rowEnd,
        int bytesPerRow,
        bool compressLargeBlocks)
    {
        int blockStart = Math.Max(block.Start, rowStart);
        int blockEnd = Math.Min(block.EndInclusive, rowEnd);
        int visibleLength = blockEnd - blockStart + 1;

        double relativeStart = (double)(blockStart - rowStart) / bytesPerRow;
        double relativeWidth = (double)visibleLength / bytesPerRow;

        bool isCompressed = compressLargeBlocks && block.Length > bytesPerRow;
        string title = ShortTitle(block.Kind, isCompressed);

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
            BackgroundBrush = GetBlockBrush(block.Kind),
            RelativeStart = relativeStart,
            RelativeWidth = relativeWidth,
            IsCompressed = isCompressed
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

    private static MediaBrush GetBlockBrush(GifBlockKind kind) =>
        kind switch
        {
            GifBlockKind.Header => CreateBrush("#1e40af"),
            GifBlockKind.LogicalScreenDescriptor => CreateBrush("#2563eb"),
            GifBlockKind.GlobalColorTable => CreateBrush("#16a34a"),
            GifBlockKind.GraphicControlExtension => CreateBrush("#9333ea"),
            GifBlockKind.ApplicationExtension => CreateBrush("#a855f7"),
            GifBlockKind.ImageDescriptor => CreateBrush("#ea580c"),
            GifBlockKind.LocalColorTable => CreateBrush("#22c55e"),
            GifBlockKind.ImageData => CreateBrush("#fb923c"),
            GifBlockKind.Trailer => CreateBrush("#0f766e"),
            _ => CreateBrush("#cbd5e1")
        };

    private static MediaBrush CreateBrush(string hex)
    {
        var color = (MediaColor)MediaColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(MediaColor.FromArgb(200, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
