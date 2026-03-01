namespace DecodingGif.Core.Models;

public sealed record CreatorInfo(
    string SoftwareName,
    string EstimatedEra,
    int ConfidencePercent,
    IReadOnlyList<string> KeyEvidence)
{
    public static CreatorInfo Generic(string evidence) =>
        new(
            SoftwareName: "Generic GIF creator",
            EstimatedEra: "Unknown",
            ConfidencePercent: 15,
            KeyEvidence: [evidence]);
}
