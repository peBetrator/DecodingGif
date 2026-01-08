namespace DecodingGif.Core.Models;
public sealed record GifFile(
    string FilePath,
    byte[] Bytes,
    GifHeader Header,
    LogicalScreenDescriptor Screen);
