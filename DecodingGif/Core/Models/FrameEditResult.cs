namespace DecodingGif.Core.Models;

public sealed class FrameEditResult
{
    public required GifFile UpdatedFile { get; init; }
    public required IReadOnlyList<GifByteRange> UpdatedRanges { get; init; }
    public required FrameEditValidationResult Validation { get; init; }
    public required string OperationDescription { get; init; }
}
