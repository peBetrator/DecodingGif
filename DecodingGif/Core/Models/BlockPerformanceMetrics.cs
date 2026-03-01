namespace DecodingGif.Core.Models;

public enum PerformanceTier
{
    Good = 0,
    Moderate = 1,
    Poor = 2
}

public enum NetworkPriorityLevel
{
    Critical = 0,
    High = 1,
    Medium = 2,
    Low = 3
}

public readonly record struct BlockPerformanceKey(GifBlockKind Kind, int Start, int Length)
{
    public static BlockPerformanceKey FromRange(GifByteRange range) => new(range.Kind, range.Start, range.Length);
}

public sealed record BlockPerformanceMetrics(
    double ParseTimeMs,
    long MemoryImpactBytes,
    NetworkPriorityLevel NetworkPriority,
    PerformanceTier Tier,
    string TypeOverlayText,
    string MetricsOverlayText,
    string OptimizationSuggestion,
    double? UsageEfficiencyPercent = null);
