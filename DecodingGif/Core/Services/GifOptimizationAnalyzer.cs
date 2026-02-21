using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class GifOptimizationAnalyzer
{
    public OptimizationReport AnalyzeFile(GifFile file, IEnumerable<GifByteRange> blocks)
    {
        var report = new OptimizationReport();
        var ordered = blocks.OrderBy(b => b.Start).ToList();

        AnalyzeColorTables(file, ordered, report);
        AnalyzeAnimation(file, ordered, report);
        AnalyzeStructure(ordered, report);
        AnalyzeDataDensity(file, ordered, report);

        return report;
    }

    private static void AnalyzeColorTables(GifFile file, IReadOnlyList<GifByteRange> blocks, OptimizationReport report)
    {
        var gct = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.GlobalColorTable);
        if (gct is not null && gct.Length >= 6)
        {
            int tableColors = gct.Length / 3;
            int uniqueColors = CountUniqueRgbTriplets(file.Bytes, gct.Start, gct.Length);
            int target = NextPowerOfTwo(Math.Max(2, uniqueColors));
            if (target < tableColors)
            {
                int savings = (tableColors - target) * 3;
                report.Suggestions.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.PaletteReduction,
                    Priority = ComputePriority(savings, file.Bytes.Length),
                    Title = "Reduce Global Color Table",
                    Description = $"GCT has {tableColors} entries, ~{uniqueColors} unique RGB values detected.",
                    Recommendation = $"Reduce GCT to {target} colors if encoder/pixel indices allow it.",
                    BytesSavings = savings,
                    Impact = $"Potential save: {savings} bytes",
                    ImpactType = "Size reduction"
                });
            }
        }

        if (gct is null)
            return;

        foreach (var lct in blocks.Where(b => b.Kind == GifBlockKind.LocalColorTable))
        {
            int compareLen = Math.Min(lct.Length, gct.Length);
            if (compareLen <= 0 || lct.Start + compareLen > file.Bytes.Length || gct.Start + compareLen > file.Bytes.Length)
                continue;

            bool equalsPrefix = true;
            for (int i = 0; i < compareLen; i++)
            {
                if (file.Bytes[lct.Start + i] == file.Bytes[gct.Start + i])
                    continue;
                equalsPrefix = false;
                break;
            }

            if (!equalsPrefix || compareLen < lct.Length)
                continue;

            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.PaletteDuplication,
                Priority = ComputePriority(lct.Length, file.Bytes.Length),
                Title = "Duplicate Local Color Table",
                Description = $"LCT at 0x{lct.Start:X8} duplicates GCT prefix.",
                Recommendation = "Remove LCT and reuse GCT if frame rendering remains identical.",
                BytesSavings = lct.Length,
                Impact = $"Potential save: {lct.Length} bytes",
                ImpactType = "Size reduction"
            });
        }
    }

    private static void AnalyzeAnimation(GifFile file, IReadOnlyList<GifByteRange> blocks, OptimizationReport report)
    {
        var gceBlocks = blocks.Where(b => b.Kind == GifBlockKind.GraphicControlExtension && b.Length >= 8).ToList();
        if (gceBlocks.Count == 0)
            return;

        int veryFastCount = 0;
        int restoreBgCount = 0;
        foreach (var gce in gceBlocks)
        {
            int start = gce.Start;
            if (start + 5 >= file.Bytes.Length)
                continue;

            byte packed = file.Bytes[start + 3];
            ushort delayCs = (ushort)(file.Bytes[start + 4] | (file.Bytes[start + 5] << 8));
            if (delayCs is > 0 and < 5)
                veryFastCount++;

            int disposal = (packed >> 2) & 0b111;
            if (disposal == 2)
                restoreBgCount++;
        }

        if (veryFastCount > 0)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.AnimationTiming,
                Priority = SuggestionPriority.Medium,
                Title = "Very fast frame timing",
                Description = $"{veryFastCount} frame(s) use delay under 50ms.",
                Recommendation = "Increase delay to 50-100ms or merge similar consecutive frames.",
                Impact = "Smoother playback and lower CPU usage",
                ImpactType = "Performance/UX"
            });
        }

        if (restoreBgCount >= Math.Max(3, (int)(gceBlocks.Count * 0.8)))
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.DisposalMethod,
                Priority = SuggestionPriority.Low,
                Title = "Uniform disposal method",
                Description = $"Most GCE blocks use disposal=2 (restore background).",
                Recommendation = "Use disposal=1 for static regions when visually safe.",
                Impact = "Can reduce redraw work",
                ImpactType = "Performance"
            });
        }
    }

    private static void AnalyzeStructure(IReadOnlyList<GifByteRange> blocks, OptimizationReport report)
    {
        var app = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.ApplicationExtension);
        var firstImage = blocks.FirstOrDefault(b => b.Kind == GifBlockKind.ImageDescriptor);
        if (app is not null && firstImage is not null && app.Start > firstImage.Start)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.StructureOrder,
                Priority = SuggestionPriority.Low,
                Title = "Application Extension placement",
                Description = "Application Extension appears after image/frame data.",
                Recommendation = "Move it closer to header/LSD/GCT for cleaner structure.",
                Impact = "Better readability and interoperability",
                ImpactType = "Structure quality"
            });
        }
    }

    private static void AnalyzeDataDensity(GifFile file, IReadOnlyList<GifByteRange> blocks, OptimizationReport report)
    {
        var imageData = blocks.Where(b => b.Kind == GifBlockKind.ImageData).OrderBy(b => b.Length).ToList();
        if (imageData.Count < 3)
            return;

        int tinyBlocks = imageData.Count(b => b.Length < 24);
        if (tinyBlocks < 3)
            return;

        report.Suggestions.Add(new OptimizationSuggestion
        {
            Type = OptimizationType.DataFragmentation,
            Priority = SuggestionPriority.Low,
            Title = "Fragmented image payload",
            Description = $"{tinyBlocks} image-data segment(s) are very small.",
            Recommendation = "Consider re-encoding frames to reduce tiny fragmented payloads.",
            Impact = $"File size currently {file.Bytes.Length} bytes",
            ImpactType = "Compression opportunity"
        });
    }

    private static int CountUniqueRgbTriplets(byte[] bytes, int start, int length)
    {
        var set = new HashSet<int>();
        int end = Math.Min(start + length, bytes.Length);
        for (int i = start; i + 2 < end; i += 3)
        {
            int rgb = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
            set.Add(rgb);
        }
        return set.Count;
    }

    private static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n)
            p <<= 1;
        return p;
    }

    private static SuggestionPriority ComputePriority(int savingsBytes, int fileSize)
    {
        if (fileSize <= 0)
            return SuggestionPriority.Low;

        double ratio = savingsBytes / (double)fileSize;
        if (ratio >= 0.05)
            return SuggestionPriority.High;
        if (ratio >= 0.01)
            return SuggestionPriority.Medium;
        return SuggestionPriority.Low;
    }
}
