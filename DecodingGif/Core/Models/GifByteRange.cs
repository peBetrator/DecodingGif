namespace DecodingGif.Core.Models;

public sealed record GifByteRange(GifBlockKind Kind, string Name, int Start, int Length)
{
    public int EndExclusive => Start + Length;
    public int EndInclusive => Start + Length - 1;

    public bool Contains(int offset) => offset >= Start && offset < EndExclusive;

    public override string ToString() => $"{Name} [{Start:X8}..{EndInclusive:X8}] ({Length} bytes)";
}