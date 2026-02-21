namespace DecodingGif.Core.Models;

public enum OptimizationType
{
    PaletteReduction,
    PaletteDuplication,
    AnimationTiming,
    DisposalMethod,
    StructureOrder,
    DataFragmentation
}

public enum SuggestionPriority
{
    High,
    Medium,
    Low
}

public sealed class OptimizationSuggestion
{
    public OptimizationType Type { get; init; }
    public SuggestionPriority Priority { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public int? BytesSavings { get; init; }
    public string Impact { get; init; } = string.Empty;
    public string ImpactType { get; init; } = string.Empty;
}

public sealed class OptimizationReport
{
    public List<OptimizationSuggestion> Suggestions { get; } = [];
    public int TotalPotentialSavingsBytes => Suggestions.Where(s => s.BytesSavings.HasValue).Sum(s => s.BytesSavings!.Value);
}
