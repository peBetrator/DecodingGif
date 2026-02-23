namespace DecodingGif.Core.Models;

public sealed class LZWDecompressionStatistics
{
    public int TotalInputBytes { get; init; }
    public int TotalInputBits { get; init; }
    public int ProcessedBits { get; init; }
    public int ProcessedBytes { get; init; }
    public int OutputBytes { get; init; }
    public int DictionarySize { get; init; }
    public int CurrentCodeSize { get; init; }
    public int StepCount { get; init; }
    public bool IsComplete { get; init; }
    public double ProgressPercent { get; init; }
    public double CompressionRatio { get; init; }
}
