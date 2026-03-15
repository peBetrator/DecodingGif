using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class GifOptimizationAnalyzer
{
    public OptimizationReport AnalyzeFile(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        ForensicAnalysisResult? forensicAnalysis = null)
    {
        var report = new OptimizationReport();
        var ordered = blocks.OrderBy(b => b.Start).ToList();

        AnalyzeColorTables(file, ordered, report);
        AnalyzeAnimation(file, ordered, report);
        AnalyzeStructure(ordered, report);
        AnalyzeDataDensity(file, ordered, report);
        AnalyzeForensicConsiderations(forensicAnalysis, report);

        return report;
    }

    private static void AnalyzeForensicConsiderations(ForensicAnalysisResult? forensicAnalysis, OptimizationReport report)
    {
        if (forensicAnalysis is null)
            return;

        if (forensicAnalysis.ProfessionalClassification == ProfessionalClassification.Amateur
            && forensicAnalysis.OverallConfidence >= 55)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.StructureOrder,
                Priority = SuggestionPriority.Medium,
                Title = "Legacy encoding footprint",
                Description = "Forensic analysis suggests an older or manually assembled export pipeline.",
                Recommendation = "Re-encode with a modern optimizer to normalize block order, palette sizing, and compression behavior.",
                Impact = forensicAnalysis.QuickSummary,
                ImpactType = "Forensic quality"
            });
        }

        if (forensicAnalysis.ProfessionalClassification == ProfessionalClassification.Automated
            && forensicAnalysis.EvidenceChain.Any(e => e.EvidenceType == EvidenceType.TimingSignature))
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.AnimationTiming,
                Priority = SuggestionPriority.Low,
                Title = "Preserve timing fingerprints",
                Description = "The file contains timing signatures consistent with scripted or web-based generation.",
                Recommendation = "When optimizing, preserve non-rounded delays if playback fidelity matters for analysis or reproduction.",
                Impact = forensicAnalysis.QuickSummary,
                ImpactType = "Forensic reproducibility"
            });
        }

        if (forensicAnalysis.OverallConfidence < 45)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.StructureOrder,
                Priority = SuggestionPriority.Low,
                Title = "Unknown provenance",
                Description = "Creator attribution confidence is low and the byte structure may mix signals from multiple tools.",
                Recommendation = "Treat optimization results cautiously and keep an original copy for forensic comparison.",
                Impact = forensicAnalysis.QuickSummary,
                ImpactType = "Risk control"
            });
        }
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

        var frameBlocks = BuildFrameTimingRanges(file, blocks);

        if (frameBlocks.Count == 0)
            return;

        var fpsAnalyzer = new AnimationFPSAnalyzer();
        var fps = fpsAnalyzer.Analyze(frameBlocks);
        if (fps.AverageFPS < 12)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.AnimationTiming,
                Priority = fps.AverageFPS < 6 ? SuggestionPriority.High : SuggestionPriority.Medium,
                Title = "Low animation FPS",
                Description = $"Average playback speed is about {fps.AverageFPS:0.0} FPS.",
                Recommendation = "Lower frame delays or simplify overly long pauses to improve perceived smoothness.",
                Impact = $"Range {fps.MinFPS:0.0}-{fps.MaxFPS:0.0} FPS, consistency {fps.ConsistencyRating}",
                ImpactType = "Animation smoothness"
            });
        }

        if (fps.FPSVariance >= 2.0)
        {
            report.Suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.AnimationTiming,
                Priority = fps.FPSVariance >= 4.0 ? SuggestionPriority.Medium : SuggestionPriority.Low,
                Title = "Inconsistent frame pacing",
                Description = $"Frame pacing deviation is {fps.FPSVariance:0.00} FPS.",
                Recommendation = "Normalize neighboring delays to reduce visible timing jumps between frames.",
                Impact = $"Consistency rating: {fps.ConsistencyRating}",
                ImpactType = "Playback stability"
            });
        }
    }

    private static List<GifByteRange> BuildFrameTimingRanges(GifFile file, IReadOnlyList<GifByteRange> blocks)
    {
        var result = new List<GifByteRange>();
        GifByteRange? pendingGce = null;
        int frameIndex = 0;

        foreach (var block in blocks.OrderBy(b => b.Start))
        {
            if (block.Kind == GifBlockKind.GraphicControlExtension)
            {
                pendingGce = block;
                continue;
            }

            if (block.Kind != GifBlockKind.ImageDescriptor)
                continue;

            int delayMs = 0;
            if (pendingGce is not null && TryReadDelayMs(file.Bytes, pendingGce, out int parsedDelay))
                delayMs = parsedDelay;

            result.Add(new GifByteRange(block.Kind, block.Name, block.Start, block.Length, frameIndex, delayMs));
            frameIndex++;
            pendingGce = null;
        }

        return result;
    }

    private static bool TryReadDelayMs(byte[] bytes, GifByteRange gce, out int delayMs)
    {
        delayMs = 0;
        if (gce.Length < 8 || gce.Start < 0 || gce.Start + 5 >= bytes.Length)
            return false;

        ushort delayCs = (ushort)(bytes[gce.Start + 4] | (bytes[gce.Start + 5] << 8));
        delayMs = delayCs * 10;
        return true;
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
