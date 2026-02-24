namespace DecodingGif.Core.Models;

public readonly record struct PaletteTarget(PaletteTargetType PaletteType, int? FrameIndex = null)
{
    public static PaletteTarget Global() => new(PaletteTargetType.GlobalColorTable, null);
    public static PaletteTarget Local(int frameIndex) => new(PaletteTargetType.LocalColorTable, frameIndex);
}
