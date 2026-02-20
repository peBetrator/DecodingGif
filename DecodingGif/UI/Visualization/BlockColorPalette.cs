using System.Windows.Media;
using DecodingGif.Core.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace DecodingGif.UI.Visualization;

public readonly record struct BlockColorInfo(MediaColor Color, double Intensity, string Label);

public static class BlockColorPalette
{
    private static readonly Dictionary<GifBlockKind, BlockColorInfo> Scheme = new()
    {
        { GifBlockKind.Header, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#1e40af"), 1.00, "File Header") },
        { GifBlockKind.LogicalScreenDescriptor, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#2563eb"), 0.95, "Logical Screen Descriptor") },
        { GifBlockKind.GlobalColorTable, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#16a34a"), 0.82, "Global Color Table") },
        { GifBlockKind.GraphicControlExtension, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#9333ea"), 0.74, "Graphic Control Extension") },
        { GifBlockKind.ApplicationExtension, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#a855f7"), 0.68, "Application Extension") },
        { GifBlockKind.ImageDescriptor, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#ea580c"), 0.80, "Image Descriptor") },
        { GifBlockKind.LocalColorTable, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#22c55e"), 0.66, "Local Color Table") },
        { GifBlockKind.ImageData, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#fb923c"), 0.58, "Image Data") },
        { GifBlockKind.Trailer, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#0f766e"), 0.72, "Trailer") },
        { GifBlockKind.Unknown, new BlockColorInfo((MediaColor)MediaColorConverter.ConvertFromString("#64748b"), 0.40, "Unknown") }
    };
    private static readonly Dictionary<int, MediaBrush> BrushCache = new();
    private static readonly object BrushLock = new();
    private static readonly MediaBrush SelectedBrush = BuildStaticBrush("#fbbf24");

    public static BlockColorInfo Get(GifBlockKind kind) =>
        Scheme.TryGetValue(kind, out var info) ? info : Scheme[GifBlockKind.Unknown];

    public static MediaBrush BuildBrush(GifBlockKind kind, bool isEmphasized, double sizeFactor = 1.0)
    {
        var info = Get(kind);
        double intensity = info.Intensity * Math.Clamp(sizeFactor, 0.65, 1.25);
        if (isEmphasized)
            intensity = Math.Min(intensity + 0.20, 1.0);

        int alphaBucket = Math.Clamp((int)Math.Round(intensity * 10), 0, 10);
        int key = (((int)kind) << 8) | (isEmphasized ? 0x80 : 0) | alphaBucket;

        lock (BrushLock)
        {
            if (BrushCache.TryGetValue(key, out var cached))
                return cached;

            byte alpha = (byte)Math.Clamp((int)(45 + (intensity * 150)), 25, 220);
            var c = info.Color;
            var brush = new SolidColorBrush(MediaColor.FromArgb(alpha, c.R, c.G, c.B));
            brush.Freeze();
            BrushCache[key] = brush;
            return brush;
        }
    }

    public static MediaBrush SelectedByteBrush() => SelectedBrush;

    private static MediaBrush BuildStaticBrush(string hex)
    {
        var brush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
