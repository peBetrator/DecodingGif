namespace DecodingGif.Core.Models;

public enum FPSConsistencyRating
{
    Excellent,
    Good,
    Fair,
    Poor
}

public enum FPSPerformanceRating
{
    Smooth,
    Acceptable,
    Choppy,
    VeryChoppy
}

public sealed class FPSAnalysisResult
{
    public double AverageFPS { get; init; }
    public double MinFPS { get; init; }
    public double MaxFPS { get; init; }
    public double FPSVariance { get; init; }
    public FPSConsistencyRating ConsistencyRating { get; init; }
    public FPSPerformanceRating PerformanceRating { get; init; }
    public List<string> Recommendations { get; init; } = [];
}
