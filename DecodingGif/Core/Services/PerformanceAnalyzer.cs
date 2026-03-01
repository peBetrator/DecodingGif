using System.Text;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class PerformanceAnalyzer
{
    public IReadOnlyDictionary<BlockPerformanceKey, BlockPerformanceMetrics> Analyze(
        GifFile file,
        IEnumerable<GifByteRange> blocks)
    {
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        var map = new Dictionary<BlockPerformanceKey, BlockPerformanceMetrics>(ordered.Count);
        var descriptorPixelQueue = BuildImageDescriptorPixelQueue(file, ordered);
        int firstImageStart = ordered.FirstOrDefault(b => b.Kind == GifBlockKind.ImageDescriptor)?.Start ?? int.MaxValue;

        foreach (var block in ordered)
        {
            var metrics = BuildBlockMetrics(file, block, descriptorPixelQueue, firstImageStart);
            map[BlockPerformanceKey.FromRange(block)] = metrics;
        }

        return map;
    }

    private static BlockPerformanceMetrics BuildBlockMetrics(
        GifFile file,
        GifByteRange block,
        Queue<int> descriptorPixelQueue,
        int firstImageStart)
    {
        return block.Kind switch
        {
            GifBlockKind.Header => BuildHeaderMetrics(),
            GifBlockKind.GlobalColorTable or GifBlockKind.LocalColorTable => BuildPaletteMetrics(file, block),
            GifBlockKind.ImageData => BuildImageDataMetrics(block, descriptorPixelQueue),
            GifBlockKind.ApplicationExtension or GifBlockKind.GraphicControlExtension => BuildExtensionMetrics(file, block, firstImageStart),
            _ => BuildGenericMetrics(block, firstImageStart)
        };
    }

    private static BlockPerformanceMetrics BuildHeaderMetrics()
    {
        const double parse = 0.1;
        return new BlockPerformanceMetrics(
            ParseTimeMs: parse,
            MemoryImpactBytes: 6,
            NetworkPriority: NetworkPriorityLevel.Critical,
            Tier: PerformanceTier.Good,
            TypeOverlayText: "⚡ HDR",
            MetricsOverlayText: $"{parse:0.0}ms Critical",
            OptimizationSuggestion: "Header is optimal; keep on critical path.");
    }

    private static BlockPerformanceMetrics BuildPaletteMetrics(GifFile file, GifByteRange block)
    {
        int entries = Math.Max(1, block.Length / 3);
        int unique = CountUniqueRgbTriplets(file.Bytes, block.Start, block.Length);
        double usage = Math.Clamp((unique * 100.0) / entries, 0.0, 100.0);
        double parse = Math.Round(0.25 + (block.Length / 1200.0), 2);
        var tier = usage < 55 ? PerformanceTier.Poor : usage < 80 ? PerformanceTier.Moderate : PerformanceTier.Good;
        string tableName = block.Kind == GifBlockKind.GlobalColorTable ? "GCT" : "LCT";

        string suggestion = usage < 60
            ? $"Only ~{usage:0}% colors are used. Reduce {tableName} size or deduplicate palette entries."
            : "Palette usage is healthy; minor gains only from deduplication.";

        return new BlockPerformanceMetrics(
            ParseTimeMs: parse,
            MemoryImpactBytes: block.Length,
            NetworkPriority: block.Kind == GifBlockKind.GlobalColorTable ? NetworkPriorityLevel.Critical : NetworkPriorityLevel.Medium,
            Tier: tier,
            TypeOverlayText: $"💾 {tableName}",
            MetricsOverlayText: $"{FormatCompactBytes(block.Length)} 📊 {usage:0}%",
            OptimizationSuggestion: suggestion,
            UsageEfficiencyPercent: usage);
    }

    private static BlockPerformanceMetrics BuildImageDataMetrics(GifByteRange block, Queue<int> descriptorPixelQueue)
    {
        int decompressedBytes = descriptorPixelQueue.Count > 0 ? Math.Max(1, descriptorPixelQueue.Dequeue()) : Math.Max(block.Length * 5, 1);
        double parse = Math.Round(2.0 + (block.Length / 180.0), 1);
        double compressionEfficiency = Math.Clamp((block.Length / (double)decompressedBytes) * 100.0, 0.0, 100.0);

        var tier = parse >= 12.0 || decompressedBytes >= 2_000_000
            ? PerformanceTier.Poor
            : parse >= 6.0 || decompressedBytes >= 700_000
                ? PerformanceTier.Moderate
                : PerformanceTier.Good;

        string suggestion = compressionEfficiency > 70
            ? "Compression is weak; consider frame differencing or reducing color churn."
            : compressionEfficiency > 50
                ? "Re-encoding with stronger temporal optimization may reduce payload."
                : "Compression is efficient; focus on frame count for further gains.";

        return new BlockPerformanceMetrics(
            ParseTimeMs: parse,
            MemoryImpactBytes: decompressedBytes,
            NetworkPriority: NetworkPriorityLevel.High,
            Tier: tier,
            TypeOverlayText: "🐌 IMG",
            MetricsOverlayText: $"{parse:0.#}ms 💾 {FormatCompactBytes(decompressedBytes)}",
            OptimizationSuggestion: suggestion,
            UsageEfficiencyPercent: 100.0 - compressionEfficiency);
    }

    private static BlockPerformanceMetrics BuildExtensionMetrics(GifFile file, GifByteRange block, int firstImageStart)
    {
        double parse = Math.Round(block.Kind == GifBlockKind.ApplicationExtension ? 0.08 : 0.05, 2);
        string extName = block.Kind == GifBlockKind.ApplicationExtension
            ? ParseApplicationExtensionLabel(file.Bytes, block)
            : "GCE";
        NetworkPriorityLevel priority = block.Start < firstImageStart ? NetworkPriorityLevel.Critical : NetworkPriorityLevel.Low;

        return new BlockPerformanceMetrics(
            ParseTimeMs: parse,
            MemoryImpactBytes: block.Length,
            NetworkPriority: priority,
            Tier: PerformanceTier.Good,
            TypeOverlayText: $"🌐 {extName}",
            MetricsOverlayText: $"⚡ {Math.Round(parse, 0):0}ms",
            OptimizationSuggestion: block.Kind == GifBlockKind.ApplicationExtension
                ? "Keep only required app extensions (looping/meta)."
                : "Merge redundant timing/control extensions when frame behavior allows.");
    }

    private static BlockPerformanceMetrics BuildGenericMetrics(GifByteRange block, int firstImageStart)
    {
        double parse = Math.Round(0.12 + (block.Length / 4000.0), 2);
        NetworkPriorityLevel priority = block.Start < firstImageStart
            ? NetworkPriorityLevel.High
            : NetworkPriorityLevel.Medium;
        var tier = parse <= 0.5 ? PerformanceTier.Good : parse <= 1.5 ? PerformanceTier.Moderate : PerformanceTier.Poor;

        return new BlockPerformanceMetrics(
            ParseTimeMs: parse,
            MemoryImpactBytes: block.Length,
            NetworkPriority: priority,
            Tier: tier,
            TypeOverlayText: $"{GetKindIcon(block.Kind)} {ShortLabel(block.Kind)}",
            MetricsOverlayText: $"{parse:0.##}ms 💾 {FormatCompactBytes(block.Length)}",
            OptimizationSuggestion: "No major optimization hotspot detected.");
    }

    private static Queue<int> BuildImageDescriptorPixelQueue(GifFile file, IReadOnlyList<GifByteRange> blocks)
    {
        var queue = new Queue<int>();
        foreach (var descriptor in blocks.Where(b => b.Kind == GifBlockKind.ImageDescriptor).OrderBy(b => b.Start))
        {
            if (!TryReadImageDescriptorSize(file.Bytes, descriptor, out int width, out int height))
                continue;
            queue.Enqueue(Math.Max(1, width * height));
        }

        return queue;
    }

    private static bool TryReadImageDescriptorSize(byte[] bytes, GifByteRange descriptor, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (descriptor.Start < 0 || descriptor.Start + 9 >= bytes.Length)
            return false;
        if (bytes[descriptor.Start] != 0x2C)
            return false;

        width = bytes[descriptor.Start + 5] | (bytes[descriptor.Start + 6] << 8);
        height = bytes[descriptor.Start + 7] | (bytes[descriptor.Start + 8] << 8);
        return width > 0 && height > 0;
    }

    private static string ParseApplicationExtensionLabel(byte[] bytes, GifByteRange block)
    {
        if (block.Start < 0 || block.Start + 14 >= bytes.Length)
            return "EXT";

        if (bytes[block.Start] != 0x21 || bytes[block.Start + 1] != 0xFF)
            return "EXT";

        int idLen = bytes[block.Start + 2];
        if (idLen <= 0 || block.Start + 3 + idLen > bytes.Length)
            return "EXT";

        string id = Encoding.ASCII.GetString(bytes, block.Start + 3, idLen).Trim();
        if (id.StartsWith("NETSCAPE", StringComparison.OrdinalIgnoreCase))
            return "NETSCAPE";
        if (id.Length == 0)
            return "EXT";
        return id.Length <= 10 ? id.ToUpperInvariant() : id[..10].ToUpperInvariant();
    }

    private static int CountUniqueRgbTriplets(byte[] bytes, int start, int length)
    {
        var set = new HashSet<int>();
        int safeStart = Math.Max(start, 0);
        int end = Math.Min(safeStart + length, bytes.Length);
        for (int i = safeStart; i + 2 < end; i += 3)
        {
            int rgb = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
            set.Add(rgb);
        }

        return set.Count;
    }

    private static string GetKindIcon(GifBlockKind kind) =>
        kind switch
        {
            GifBlockKind.LogicalScreenDescriptor => "📐",
            GifBlockKind.ImageDescriptor => "🖼",
            GifBlockKind.Trailer => "✅",
            GifBlockKind.Unknown => "❔",
            _ => "⚙"
        };

    private static string ShortLabel(GifBlockKind kind) =>
        kind switch
        {
            GifBlockKind.LogicalScreenDescriptor => "LSD",
            GifBlockKind.ImageDescriptor => "ID",
            GifBlockKind.Trailer => "END",
            GifBlockKind.Unknown => "UNK",
            _ => kind.ToString().ToUpperInvariant()
        };

    private static string FormatCompactBytes(long value)
    {
        if (value < 1024)
            return $"{value}B";
        if (value < 1024 * 1024)
            return $"{(value / 1024d):0.#}KB";
        return $"{(value / 1024d / 1024d):0.#}MB";
    }
}
