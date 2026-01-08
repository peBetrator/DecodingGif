namespace DecodingGif.Core.Models;

public sealed record LogicalScreenDescriptor(
    ushort Width,
    ushort Height,
    byte Packed,
    byte BackgroundColorIndex,
    byte PixelAspectRatio)
{
    public bool GlobalColorTableFlag => (Packed & 0b1000_0000) != 0;

    // Size = 2^(N+1), где N = packed & 0b0000_0111
    public int GlobalColorTableSize => 1 << ((Packed & 0b0000_0111) + 1);

    public int ColorResolution => ((Packed & 0b0111_0000) >> 4) + 1;

    public bool SortFlag => (Packed & 0b0000_1000) != 0;
}
