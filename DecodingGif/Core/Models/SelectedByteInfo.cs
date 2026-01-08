namespace DecodingGif.Core.Models;

public sealed record SelectedByteInfo(
    int Offset,
    byte Value,
    string Hex,
    int Decimal,
    char Ascii,
    string Description);
