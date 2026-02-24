namespace DecodingGif.Core.Models;

public sealed class BatchColorOperationResult
{
    public required string Operation { get; init; }
    public required int PaletteSize { get; init; }
    public required IReadOnlyList<int> AffectedColorIndexes { get; init; }
    public int AffectedCount => AffectedColorIndexes.Count;
    public string Summary => $"{Operation}: {AffectedCount}/{PaletteSize} colors affected";

    public static BatchColorOperationResult Empty(string operation, int paletteSize) =>
        new()
        {
            Operation = operation,
            PaletteSize = paletteSize,
            AffectedColorIndexes = Array.Empty<int>()
        };
}
