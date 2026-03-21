namespace DecodingGif.Core.Models;

public readonly record struct HexCellVisualInfo(
    bool HasBlock,
    GifBlockKind Kind,
    double SizeFactor,
    bool IsLeftBoundary,
    bool IsRightBoundary);
