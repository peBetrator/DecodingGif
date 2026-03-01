using System.Text;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class CreatorDetector
{
    private sealed class Candidate
    {
        public required string Name { get; init; }
        public required string Era { get; init; }
        public int Score { get; set; }
        public List<string> Evidence { get; } = [];
    }

    public CreatorInfo DetectCreator(GifFile file, IEnumerable<GifByteRange> blocks)
    {
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        if (ordered.Count == 0)
            return CreatorInfo.Generic("No structural blocks available for creator fingerprinting.");

        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var unknownExtensions = new List<string>();

        AnalyzeApplicationExtensions(file, ordered, candidates, unknownExtensions);
        AnalyzePalettePatterns(file, ordered, candidates);
        AnalyzeTimingPatterns(file, ordered, candidates);
        AnalyzeBlockOrderingPatterns(ordered, candidates);
        AnalyzeCompressionStyle(file, ordered, candidates);

        if (candidates.Count == 0 && unknownExtensions.Count > 0)
        {
            string custom = string.Join(", ", unknownExtensions.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            return new CreatorInfo(
                SoftwareName: $"Unknown creator with custom extension: {custom}",
                EstimatedEra: "Unknown",
                ConfidencePercent: 35,
                KeyEvidence: [$"Found custom Application Extension signature(s): {custom}."]);
        }

        if (candidates.Count == 0)
            return CreatorInfo.Generic("No clear creator fingerprints detected.");

        var ranked = candidates.Values.OrderByDescending(c => c.Score).ToList();
        var best = ranked[0];
        int confidence = Math.Clamp(best.Score, 0, 100);

        var evidence = new List<string>(best.Evidence);
        if (unknownExtensions.Count > 0)
        {
            string custom = string.Join(", ", unknownExtensions.Distinct(StringComparer.OrdinalIgnoreCase).Take(2));
            evidence.Add($"Custom extension(s) present: {custom}.");
        }

        foreach (var alternative in ranked.Skip(1).Where(c => c.Score >= 30 && (best.Score - c.Score) <= 15).Take(2))
        {
            evidence.Add($"Also matches {alternative.Name} ({Math.Clamp(alternative.Score, 0, 100)}%).");
        }

        if (evidence.Count == 0)
            evidence.Add("Detected from aggregate structural heuristics.");

        return new CreatorInfo(best.Name, best.Era, confidence, evidence);
    }

    private static void AnalyzeApplicationExtensions(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        Dictionary<string, Candidate> candidates,
        List<string> unknownExtensions)
    {
        foreach (var app in blocks.Where(b => b.Kind == GifBlockKind.ApplicationExtension))
        {
            string signature = ReadApplicationSignature(file.Bytes, app);
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            if (signature.StartsWith("NETSCAPE2.0", StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidates, "Netscape Navigator", "1995-2008", 40, "Unique NETSCAPE2.0 loop extension detected (+40).");
                continue;
            }

            if (signature.StartsWith("ANIMEXTS1.0", StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidates, "Legacy animation suites", "1998-2005", 40, "ANIMEXTS1.0 extension detected (+40).");
                continue;
            }

            if (signature.StartsWith("XMP DataXMP", StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidates, "Adobe Creative Suite / Photoshop", "2003+", 40, "XMP DataXMP metadata signature detected (+40).");
                continue;
            }

            if (signature.Contains("ADOBE", StringComparison.OrdinalIgnoreCase))
            {
                AddScore(candidates, "Adobe Creative Suite / Photoshop", "2003+", 40, $"Adobe-like extension signature '{signature}' detected (+40).");
                continue;
            }

            unknownExtensions.Add(signature);
        }
    }

    private static void AnalyzePalettePatterns(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        Dictionary<string, Candidate> candidates)
    {
        var palettes = blocks.Where(b => b.Kind is GifBlockKind.GlobalColorTable or GifBlockKind.LocalColorTable).ToList();
        if (palettes.Count == 0)
            return;

        var paletteSizes = palettes.Select(p => Math.Max(1, p.Length / 3)).ToList();
        double avgPaletteSize = paletteSizes.Average();
        bool all256 = paletteSizes.All(s => s >= 256);
        int usedRatioSamples = 0;
        double usedRatioSum = 0;
        foreach (var palette in palettes)
        {
            int entries = Math.Max(1, palette.Length / 3);
            int unique = CountUniqueRgbTriplets(file.Bytes, palette.Start, palette.Length);
            usedRatioSum += Math.Clamp(unique / (double)entries, 0.0, 1.0);
            usedRatioSamples++;
        }

        double avgUsedRatio = usedRatioSamples == 0 ? 1.0 : usedRatioSum / usedRatioSamples;
        if (all256 && avgUsedRatio < 0.65)
        {
            AddScore(candidates, "Legacy software (old Paint / primitive encoders)", "1995-2005", 20,
                $"Palette pattern is unoptimized (avg usage {avgUsedRatio:P0}, mostly 256-color tables) (+20).");
        }

        bool hasAppExtensions = blocks.Any(b => b.Kind == GifBlockKind.ApplicationExtension);
        if (!hasAppExtensions && avgPaletteSize < 200 && avgUsedRatio >= 0.70)
        {
            AddScore(candidates, "Modern web services (GIPHY / ezgif)", "2019-2024", 20,
                $"No app extensions and palette is size-optimized (avg {avgPaletteSize:0} colors, usage {avgUsedRatio:P0}) (+20).");
        }
    }

    private static void AnalyzeTimingPatterns(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        Dictionary<string, Candidate> candidates)
    {
        var delaysCs = new List<int>();
        foreach (var gce in blocks.Where(b => b.Kind == GifBlockKind.GraphicControlExtension && b.Length >= 8))
        {
            if (gce.Start + 5 >= file.Bytes.Length)
                continue;
            int delayCs = file.Bytes[gce.Start + 4] | (file.Bytes[gce.Start + 5] << 8);
            delaysCs.Add(Math.Max(0, delayCs));
        }

        if (delaysCs.Count == 0)
            return;

        bool mostly100ms = delaysCs.Count(d => d == 10) >= Math.Max(1, (int)Math.Ceiling(delaysCs.Count * 0.8));
        if (mostly100ms)
        {
            AddScore(candidates, "Legacy software (old Paint / primitive encoders)", "1995-2005", 15,
                "Frame timing mostly defaults to 100ms (10cs) (+15).");
        }

        int precise = delaysCs.Count(d => d > 0 && d % 5 != 0);
        bool mathematicalPrecision = precise >= Math.Max(2, (int)Math.Ceiling(delaysCs.Count * 0.5))
            || delaysCs.Distinct().Count() >= 8;
        if (mathematicalPrecision)
        {
            AddScore(candidates, "Command-line tools (FFmpeg / ImageMagick)", "2010+", 15,
                "Timing uses high precision / non-rounded delays, typical of scripted encoders (+15).");
        }
    }

    private static void AnalyzeBlockOrderingPatterns(
        IReadOnlyList<GifByteRange> blocks,
        Dictionary<string, Candidate> candidates)
    {
        int firstImage = -1;
        int firstApp = -1;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (firstImage < 0 && blocks[i].Kind == GifBlockKind.ImageDescriptor)
                firstImage = i;
            if (firstApp < 0 && blocks[i].Kind == GifBlockKind.ApplicationExtension)
                firstApp = i;
            if (firstImage >= 0 && firstApp >= 0)
                break;
        }
        if (firstApp >= 0 && (firstImage < 0 || firstApp < firstImage))
        {
            AddScore(candidates, "Netscape Navigator", "1995-2008", 10,
                "Application Extension placed before image payload (+10).");
        }

        int wellPairedGce = 0;
        int descriptors = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != GifBlockKind.ImageDescriptor)
                continue;
            descriptors++;
            if (i > 0 && blocks[i - 1].Kind == GifBlockKind.GraphicControlExtension)
                wellPairedGce++;
        }

        if (descriptors > 0 && wellPairedGce >= Math.Max(1, (int)Math.Ceiling(descriptors * 0.8)))
        {
            AddScore(candidates, "Command-line tools (FFmpeg / ImageMagick)", "2010+", 10,
                "Consistent GCE->ImageDescriptor ordering indicates strict encoder pipeline (+10).");
        }
    }

    private static void AnalyzeCompressionStyle(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        Dictionary<string, Candidate> candidates)
    {
        long compressed = blocks.Where(b => b.Kind == GifBlockKind.ImageData).Sum(b => (long)b.Length);
        long decompressed = EstimateTotalFramePixels(file, blocks);
        if (compressed <= 0 || decompressed <= 0)
            return;

        double ratio = compressed / (double)decompressed;
        if (ratio <= 0.30)
        {
            AddScore(candidates, "Modern web services (GIPHY / ezgif)", "2019-2024", 5,
                $"Aggressive compression style detected (ratio {ratio:0.00}) (+5).");
            AddScore(candidates, "Command-line tools (FFmpeg / ImageMagick)", "2010+", 5,
                $"Compression ratio {ratio:0.00} indicates optimization-focused pipeline (+5).");
            return;
        }

        if (ratio >= 0.60)
        {
            AddScore(candidates, "Legacy software (old Paint / primitive encoders)", "1995-2005", 5,
                $"Conservative compression ratio ({ratio:0.00}) suggests older encoder defaults (+5).");
        }
    }

    private static void AddScore(
        Dictionary<string, Candidate> candidates,
        string name,
        string era,
        int points,
        string evidence)
    {
        if (!candidates.TryGetValue(name, out var candidate))
        {
            candidate = new Candidate
            {
                Name = name,
                Era = era
            };
            candidates[name] = candidate;
        }

        candidate.Score += points;
        candidate.Evidence.Add(evidence);
    }

    private static string ReadApplicationSignature(byte[] bytes, GifByteRange block)
    {
        if (block.Start < 0 || block.Start + 14 >= bytes.Length)
            return string.Empty;
        if (bytes[block.Start] != 0x21 || bytes[block.Start + 1] != 0xFF)
            return string.Empty;

        int idLength = bytes[block.Start + 2];
        if (idLength <= 0 || block.Start + 3 + idLength > bytes.Length)
            return string.Empty;

        string id = Encoding.ASCII.GetString(bytes, block.Start + 3, idLength).Trim();
        return id;
    }

    private static int CountUniqueRgbTriplets(byte[] bytes, int start, int length)
    {
        var set = new HashSet<int>();
        int safeStart = Math.Max(0, start);
        int end = Math.Min(bytes.Length, safeStart + length);
        for (int i = safeStart; i + 2 < end; i += 3)
        {
            int rgb = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
            set.Add(rgb);
        }

        return set.Count;
    }

    private static long EstimateTotalFramePixels(GifFile file, IReadOnlyList<GifByteRange> blocks)
    {
        long total = 0;
        foreach (var descriptor in blocks.Where(b => b.Kind == GifBlockKind.ImageDescriptor))
        {
            if (descriptor.Start < 0 || descriptor.Start + 9 >= file.Bytes.Length)
                continue;
            if (file.Bytes[descriptor.Start] != 0x2C)
                continue;

            int width = file.Bytes[descriptor.Start + 5] | (file.Bytes[descriptor.Start + 6] << 8);
            int height = file.Bytes[descriptor.Start + 7] | (file.Bytes[descriptor.Start + 8] << 8);
            if (width <= 0 || height <= 0)
                continue;
            total += (long)width * height;
        }

        return total;
    }
}
