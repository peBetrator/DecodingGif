using System.Text;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class CreatorDetector
{
    private sealed record CreatorProfile(string Name, string Era);

    private static readonly CreatorProfile[] Profiles =
    [
        new("Adobe Photoshop", "2003+"),
        new("GIPHY / Web Services", "2018+"),
        new("Command-line Tools", "2010+"),
        new("Legacy Software", "1995-2005")
    ];

    public CreatorInfo DetectCreator(GifFile file, IEnumerable<GifByteRange> blocks) =>
        AnalyzeForensics(file, blocks).PrimaryCreator;

    public Task<ForensicAnalysisResult> AnalyzeForensicsAsync(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => AnalyzeForensics(file, blocks, cancellationToken), cancellationToken);

    public ForensicAnalysisResult AnalyzeForensics(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        CancellationToken cancellationToken = default)
    {
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        if (ordered.Count == 0)
            return ForensicAnalysisResult.Empty("Недостаточно структурных блоков для цифровой форензики.");

        cancellationToken.ThrowIfCancellationRequested();

        var evidences = WeightedEvidenceAnalysis(file, ordered, cancellationToken);
        double overallConfidence = CalculateCreatorConfidence(evidences);
        var rankedCandidates = RankCandidates(evidences);

        if (rankedCandidates.Count == 0)
        {
            return ForensicAnalysisResult.Empty(
                evidences.Count == 0
                    ? "Форензические отпечатки не обнаружены."
                    : "Обнаружены слабые артефакты, но ни один профиль не доминирует.");
        }

        var primary = rankedCandidates[0];
        var classification = DetermineClassification(primary, overallConfidence, evidences);

        return new ForensicAnalysisResult(
            PrimaryCreator: primary,
            AlternativeCandidates: GenerateAlternativeCandidates(evidences),
            EvidenceChain: evidences
                .OrderByDescending(e => e.Weight * e.Confidence)
                .ThenBy(e => e.EvidenceType)
                .ToList(),
            OverallConfidence: overallConfidence,
            ProfessionalClassification: classification,
            QuickSummary: BuildQuickSummary(primary, overallConfidence, classification, evidences));
    }

    public List<Evidence> WeightedEvidenceAnalysis(GifFile file, IEnumerable<GifByteRange> blocks) =>
        WeightedEvidenceAnalysis(file, blocks, CancellationToken.None);

    public double CalculateCreatorConfidence(List<Evidence> evidences)
    {
        if (evidences.Count == 0)
            return 0;

        double weightedSum = evidences.Sum(e => e.Weight * Math.Clamp(e.Confidence, 0.0, 1.0));
        return Math.Clamp(weightedSum, 0, 100);
    }

    public List<CreatorInfo> GenerateAlternativeCandidates(List<Evidence> evidences)
    {
        var ranked = RankCandidates(evidences);
        if (ranked.Count <= 1)
            return [];

        int primaryConfidence = ranked[0].ConfidencePercent;
        return ranked
            .Skip(1)
            .Where(c => c.ConfidencePercent >= 20 && primaryConfidence - c.ConfidencePercent <= 30)
            .Take(3)
            .ToList();
    }

    public List<Evidence> AnalyzePaletteOptimizationFingerprints(GifFile file)
    {
        var evidences = new List<Evidence>();
        if (!file.Screen.GlobalColorTableFlag || file.Screen.GlobalColorTableSize <= 0)
            return evidences;

        int gctLength = file.Screen.GlobalColorTableSize * 3;
        const int gctStart = 13;
        if (gctStart + gctLength > file.Bytes.Length || gctLength < 6)
            return evidences;

        int unique = CountUniqueRgbTriplets(file.Bytes, gctStart, gctLength);
        int entries = Math.Max(1, gctLength / 3);
        double usageRatio = Math.Clamp(unique / (double)entries, 0.0, 1.0);

        if (entries >= 256 && usageRatio < 0.60)
        {
            evidences.Add(new Evidence(
                EvidenceType.PalettePattern,
                25,
                $"Глобальная палитра выглядит не оптимизированной: {unique} уникальных цветов из {entries}.",
                0.90,
                "GCT"));
        }
        else if (entries <= 128 && usageRatio >= 0.80)
        {
            evidences.Add(new Evidence(
                EvidenceType.PalettePattern,
                25,
                $"Глобальная палитра компактна и оптимизирована: {unique} из {entries} цветов реально задействованы.",
                0.86,
                "GCT"));
        }

        return evidences;
    }

    public List<Evidence> ExtractTimingSignatures(IEnumerable<GifByteRange> frames)
    {
        var delays = frames
            .Select(f => f.DelayMs)
            .Where(d => d.HasValue)
            .Select(d => Math.Max(0, d!.Value))
            .ToList();

        var evidences = new List<Evidence>();
        if (delays.Count == 0)
            return evidences;

        int preciseCount = delays.Count(d => d > 0 && d % 10 != 0);
        bool standardTiming = delays.Count(d => d == 100 || d == 80 || d == 60) >= Math.Max(1, (int)Math.Ceiling(delays.Count * 0.6));
        bool mathematicalPrecision = preciseCount >= Math.Max(2, delays.Count / 3)
            || delays.Distinct().Count() >= Math.Max(4, delays.Count / 2);

        if (standardTiming)
        {
            evidences.Add(new Evidence(
                EvidenceType.TimingSignature,
                20,
                "Тайминги кадров тяготеют к стандартным web-значениям 60-100 мс.",
                0.70,
                "GCE delay field"));
        }

        if (mathematicalPrecision)
        {
            evidences.Add(new Evidence(
                EvidenceType.TimingSignature,
                20,
                "Обнаружены математически точные или сильно варьирующиеся задержки, типичные для scripted-энкодеров.",
                0.92,
                "GCE delay field"));
        }

        if (delays.Count(d => d == 100) >= Math.Max(1, (int)Math.Ceiling(delays.Count * 0.8)))
        {
            evidences.Add(new Evidence(
                EvidenceType.TimingSignature,
                20,
                "Большинство кадров используют дефолтные 100 мс, что характерно для старых приложений.",
                0.82,
                "GCE delay field"));
        }

        return evidences;
    }

    private List<Evidence> WeightedEvidenceAnalysis(
        GifFile file,
        IEnumerable<GifByteRange> blocks,
        CancellationToken cancellationToken)
    {
        var ordered = blocks.OrderBy(b => b.Start).ToList();
        var evidences = new List<Evidence>();

        cancellationToken.ThrowIfCancellationRequested();
        AnalyzeApplicationExtensions(file, ordered, evidences);

        cancellationToken.ThrowIfCancellationRequested();
        evidences.AddRange(AnalyzePaletteOptimizationFingerprints(file));
        AnalyzePaletteContextFromBlocks(file, ordered, evidences);

        cancellationToken.ThrowIfCancellationRequested();
        var timingFrames = ordered
            .Where(b => b.Kind == GifBlockKind.GraphicControlExtension || (b.Kind == GifBlockKind.ImageDescriptor && b.DelayMs.HasValue))
            .ToList();
        evidences.AddRange(ExtractTimingSignatures(timingFrames));

        cancellationToken.ThrowIfCancellationRequested();
        AnalyzeBlockOrderingPatterns(ordered, evidences);

        cancellationToken.ThrowIfCancellationRequested();
        AnalyzeCompressionStyle(file, ordered, evidences);

        return evidences;
    }

    private static void AnalyzeApplicationExtensions(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        List<Evidence> evidences)
    {
        var appBlocks = blocks.Where(b => b.Kind == GifBlockKind.ApplicationExtension).ToList();
        if (appBlocks.Count == 0)
        {
            evidences.Add(new Evidence(
                EvidenceType.ApplicationSignature,
                40,
                "Application Extension отсутствуют, метаданные происхождения минимальны.",
                0.55,
                "Application Extensions"));
            return;
        }

        foreach (var app in appBlocks)
        {
            string signature = ReadApplicationSignature(file.Bytes, app);
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            if (signature.StartsWith("XMP DataXMP", StringComparison.OrdinalIgnoreCase))
            {
                evidences.Add(new Evidence(
                    EvidenceType.ApplicationSignature,
                    40,
                    "XMP DataXMP указывает на Adobe-совместимый workflow и сохранение авторских метаданных.",
                    1.0,
                    $"AppExt 0x{app.Start:X8}"));
                continue;
            }

            if (signature.Contains("ADOBE", StringComparison.OrdinalIgnoreCase))
            {
                evidences.Add(new Evidence(
                    EvidenceType.ApplicationSignature,
                    40,
                    $"Обнаружена Adobe-подобная сигнатура '{signature}'.",
                    0.95,
                    $"AppExt 0x{app.Start:X8}"));
                continue;
            }

            if (signature.StartsWith("NETSCAPE2.0", StringComparison.OrdinalIgnoreCase))
            {
                evidences.Add(new Evidence(
                    EvidenceType.ApplicationSignature,
                    40,
                    "NETSCAPE2.0 подтверждает стандартный цикл анимации и совместимый экспорт.",
                    0.72,
                    $"AppExt 0x{app.Start:X8}"));
                continue;
            }

            evidences.Add(new Evidence(
                EvidenceType.ApplicationSignature,
                40,
                $"Найдена пользовательская сигнатура приложения '{signature}'.",
                0.45,
                $"AppExt 0x{app.Start:X8}"));
        }
    }

    private static void AnalyzePaletteContextFromBlocks(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        List<Evidence> evidences)
    {
        var palettes = blocks
            .Where(b => b.Kind is GifBlockKind.GlobalColorTable or GifBlockKind.LocalColorTable)
            .ToList();

        if (palettes.Count == 0)
            return;

        double avgEntries = palettes.Average(p => Math.Max(1, p.Length / 3d));
        double avgUsage = palettes
            .Select(p =>
            {
                int entries = Math.Max(1, p.Length / 3);
                int unique = CountUniqueRgbTriplets(file.Bytes, p.Start, p.Length);
                return Math.Clamp(unique / (double)entries, 0.0, 1.0);
            })
            .DefaultIfEmpty(0)
            .Average();

        if (avgEntries < 160 && avgUsage >= 0.78)
        {
            evidences.Add(new Evidence(
                EvidenceType.PalettePattern,
                25,
                $"Средний размер палитры {avgEntries:0} цветов при использовании {avgUsage:P0}: сильная автооптимизация.",
                0.88,
                "Palette tables"));
        }

        if (avgEntries >= 224 && avgUsage < 0.62)
        {
            evidences.Add(new Evidence(
                EvidenceType.PalettePattern,
                25,
                $"Палитры крупные ({avgEntries:0} цветов) и слабо заполненные ({avgUsage:P0}), что типично для старых редакторов.",
                0.91,
                "Palette tables"));
        }
    }

    private static void AnalyzeBlockOrderingPatterns(
        IReadOnlyList<GifByteRange> blocks,
        List<Evidence> evidences)
    {
        int firstImage = IndexOfKind(blocks, GifBlockKind.ImageDescriptor);
        int firstApp = IndexOfKind(blocks, GifBlockKind.ApplicationExtension);
        int imageCount = 0;
        int strictPairs = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != GifBlockKind.ImageDescriptor)
                continue;

            imageCount++;
            if (i > 0 && blocks[i - 1].Kind == GifBlockKind.GraphicControlExtension)
                strictPairs++;
        }

        if (firstApp >= 0 && (firstImage < 0 || firstApp < firstImage))
        {
            evidences.Add(new Evidence(
                EvidenceType.BlockOrdering,
                15,
                "Application Extension размещён до кадровых данных, что соответствует стандартному экспортному порядку.",
                0.78,
                "Global block order"));
        }

        if (imageCount > 0 && strictPairs >= Math.Max(1, (int)Math.Ceiling(imageCount * 0.8)))
        {
            evidences.Add(new Evidence(
                EvidenceType.BlockOrdering,
                15,
                "Почти каждый кадр строго следует шаблону GCE -> Image Descriptor.",
                0.85,
                "Frame block order"));
        }
        else if (imageCount > 2 && strictPairs <= imageCount / 3)
        {
            evidences.Add(new Evidence(
                EvidenceType.BlockOrdering,
                15,
                "Порядок управляющих блоков нерегулярен и напоминает примитивный legacy-экспорт.",
                0.80,
                "Frame block order"));
        }
    }

    private static void AnalyzeCompressionStyle(
        GifFile file,
        IReadOnlyList<GifByteRange> blocks,
        List<Evidence> evidences)
    {
        long compressed = blocks.Where(b => b.Kind == GifBlockKind.ImageData).Sum(b => (long)b.Length);
        long decompressed = EstimateTotalFramePixels(file, blocks);
        if (compressed <= 0 || decompressed <= 0)
            return;

        double ratio = compressed / (double)decompressed;
        if (ratio <= 0.30)
        {
            evidences.Add(new Evidence(
                EvidenceType.CompressionStyle,
                10,
                $"Сжатие агрессивное (отношение {ratio:0.00}), похоже на автоматизированную web-оптимизацию.",
                0.82,
                "Image Data"));
        }
        else if (ratio <= 0.45)
        {
            evidences.Add(new Evidence(
                EvidenceType.CompressionStyle,
                10,
                $"Сжатие эффективное ({ratio:0.00}), характерно для утилитного пайплайна.",
                0.70,
                "Image Data"));
        }
        else if (ratio >= 0.60)
        {
            evidences.Add(new Evidence(
                EvidenceType.CompressionStyle,
                10,
                $"Сжатие консервативное ({ratio:0.00}), что напоминает старые кодировщики.",
                0.78,
                "Image Data"));
        }
    }

    private static List<CreatorInfo> RankCandidates(List<Evidence> evidences)
    {
        if (evidences.Count == 0)
            return [];

        var scores = Profiles.ToDictionary(
            profile => profile.Name,
            profile => new CandidateScore(profile),
            StringComparer.Ordinal);

        foreach (var evidence in evidences)
        {
            double contribution = evidence.Weight * Math.Clamp(evidence.Confidence, 0.0, 1.0);

            foreach (var profile in Profiles)
            {
                double multiplier = MatchEvidence(profile.Name, evidence);
                if (multiplier <= 0)
                    continue;

                scores[profile.Name].Score += contribution * multiplier;
                scores[profile.Name].Evidence.Add(evidence.Description);
            }
        }

        return scores.Values
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .Select(c => new CreatorInfo(
                c.Profile.Name,
                c.Profile.Era,
                Math.Clamp((int)Math.Round(c.Score), 0, 100),
                c.Evidence.Distinct(StringComparer.Ordinal).Take(3).ToList()))
            .ToList();
    }

    private static double MatchEvidence(string profileName, Evidence evidence)
    {
        string text = $"{evidence.Description} {evidence.Source}".ToLowerInvariant();

        return profileName switch
        {
            "Adobe Photoshop" when text.Contains("xmp") || text.Contains("adobe") => 1.00,
            "Adobe Photoshop" when evidence.EvidenceType == EvidenceType.PalettePattern || evidence.EvidenceType == EvidenceType.BlockOrdering => 0.55,

            "GIPHY / Web Services" when text.Contains("web") || text.Contains("агрессив") || text.Contains("60-100") => 0.95,
            "GIPHY / Web Services" when evidence.EvidenceType is EvidenceType.PalettePattern or EvidenceType.CompressionStyle => 0.70,

            "Command-line Tools" when text.Contains("scripted") || text.Contains("строго") || text.Contains("эффектив") => 1.00,
            "Command-line Tools" when evidence.EvidenceType is EvidenceType.TimingSignature or EvidenceType.BlockOrdering => 0.75,
            "Command-line Tools" when evidence.EvidenceType == EvidenceType.CompressionStyle => 0.55,

            "Legacy Software" when text.Contains("legacy") || text.Contains("100 мс") || text.Contains("консерватив") || text.Contains("стар") => 1.00,
            "Legacy Software" when evidence.EvidenceType is EvidenceType.PalettePattern or EvidenceType.BlockOrdering => 0.75,
            _ => 0
        };
    }

    private static ProfessionalClassification DetermineClassification(
        CreatorInfo primary,
        double overallConfidence,
        List<Evidence> evidences)
    {
        string creator = primary.SoftwareName.ToLowerInvariant();
        bool metadataRich = evidences.Any(e => e.EvidenceType == EvidenceType.ApplicationSignature && e.Description.Contains("xmp", StringComparison.OrdinalIgnoreCase));
        bool optimizedPalette = evidences.Any(e => e.EvidenceType == EvidenceType.PalettePattern && !e.Description.Contains("не оптим", StringComparison.OrdinalIgnoreCase));
        bool legacySignals = evidences.Any(e => e.Description.Contains("100 мс", StringComparison.OrdinalIgnoreCase)
            || e.Description.Contains("legacy", StringComparison.OrdinalIgnoreCase)
            || e.Description.Contains("стар", StringComparison.OrdinalIgnoreCase));

        if ((creator.Contains("adobe") || metadataRich) && optimizedPalette && overallConfidence >= 70)
            return ProfessionalClassification.Professional;

        if (creator.Contains("web services") || creator.Contains("command-line"))
            return ProfessionalClassification.Automated;

        if (creator.Contains("legacy") || legacySignals)
            return ProfessionalClassification.Amateur;

        return overallConfidence >= 75
            ? ProfessionalClassification.Professional
            : ProfessionalClassification.Unknown;
    }

    private static string BuildQuickSummary(
        CreatorInfo primary,
        double overallConfidence,
        ProfessionalClassification classification,
        List<Evidence> evidences)
    {
        string bestEvidence = evidences
            .OrderByDescending(e => e.Weight * e.Confidence)
            .Select(e => e.Description)
            .FirstOrDefault() ?? "ключевые следы не выделены";

        string classLabel = classification switch
        {
            ProfessionalClassification.Professional => "профессиональный",
            ProfessionalClassification.Amateur => "любительский",
            ProfessionalClassification.Automated => "автоматизированный",
            _ => "неопределённый"
        };

        return $"Наиболее вероятный источник: {primary.SoftwareName} ({overallConfidence:0}%); профиль: {classLabel}; ключевой след: {bestEvidence}";
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

        return Encoding.ASCII.GetString(bytes, block.Start + 3, idLength).Trim();
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

    private static int IndexOfKind(IReadOnlyList<GifByteRange> blocks, GifBlockKind kind)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind == kind)
                return i;
        }

        return -1;
    }

    private sealed class CandidateScore
    {
        public CandidateScore(CreatorProfile profile)
        {
            Profile = profile;
        }

        public CreatorProfile Profile { get; }
        public double Score { get; set; }
        public List<string> Evidence { get; } = [];
    }
}
