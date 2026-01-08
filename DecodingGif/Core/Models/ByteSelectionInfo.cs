namespace DecodingGif.Core.Models;

public sealed record ByteSelectionInfo(
    int Offset,
    string OffsetHex,
    byte Value,
    string ValueHex,
    int ValueDec,
    string Ascii
);
