namespace DecodingGif.Core.Models;

public enum ProfessionalClassification
{
    Professional,
    Amateur,
    Automated,
    Unknown
}

public sealed record ForensicAnalysisResult(
    CreatorInfo PrimaryCreator,
    IReadOnlyList<CreatorInfo> AlternativeCandidates,
    IReadOnlyList<Evidence> EvidenceChain,
    double OverallConfidence,
    ProfessionalClassification ProfessionalClassification,
    string QuickSummary)
{
    public static ForensicAnalysisResult Empty(string summary) =>
        new(
            PrimaryCreator: CreatorInfo.Generic(summary),
            AlternativeCandidates: [],
            EvidenceChain: [],
            OverallConfidence: 0,
            ProfessionalClassification: ProfessionalClassification.Unknown,
            QuickSummary: summary);

    public string ConfidenceText => $"Уверенность: {OverallConfidence:0}%";

    public string EstimatedEra => $"Предполагаемая эпоха: {PrimaryCreator.EstimatedEra}";

    public IReadOnlyList<string> KeyEvidence => EvidenceChain
        .OrderByDescending(e => e.Weight * e.Confidence)
        .Take(3)
        .Select(e => $"• {e.Description}")
        .ToList();

    public string AlternativeCandidatesText =>
        OverallConfidence >= 80 || AlternativeCandidates.Count == 0
            ? "Альтернативные кандидаты не требуются."
            : $"Альтернативы: {string.Join(", ", AlternativeCandidates.Take(3).Select(c => $"{c.SoftwareName} ({c.ConfidencePercent}%)"))}";

    public string ClassificationText => ProfessionalClassification switch
    {
        ProfessionalClassification.Professional => "Класс: профессиональный пайплайн",
        ProfessionalClassification.Amateur => "Класс: любительская/ручная сборка",
        ProfessionalClassification.Automated => "Класс: автоматизированный генератор",
        _ => "Класс: происхождение не определено"
    };
}
