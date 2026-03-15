namespace DecodingGif.Core.Models;

public enum EvidenceType
{
    ApplicationSignature,
    PalettePattern,
    TimingSignature,
    BlockOrdering,
    CompressionStyle
}

public sealed record Evidence(
    EvidenceType EvidenceType,
    int Weight,
    string Description,
    double Confidence,
    string Source);
