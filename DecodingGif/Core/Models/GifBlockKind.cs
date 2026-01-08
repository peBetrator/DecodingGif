namespace DecodingGif.Core.Models;

public enum GifBlockKind
{
    Unknown = 0,

    Header = 1,
    LogicalScreenDescriptor = 2,
    GlobalColorTable = 3,

    GraphicControlExtension = 10,
    ApplicationExtension = 11,

    ImageDescriptor = 20,
    LocalColorTable = 21,
    ImageData = 22,

    Trailer = 90
}
