using System.Buffers.Binary;
using System.Text;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Parsing;

public sealed class GifParser
{
    public GifFile Parse(string filePath, byte[] bytes)
    {
        if (bytes is null || bytes.Length < 13) // 6 header + 7 LSD
            throw new GifFormatException("File is too small to be a valid GIF.");

        // Header: 6 bytes: 'GIF' + '87a'/'89a'
        var signature = Encoding.ASCII.GetString(bytes, 0, 3);
        var version = Encoding.ASCII.GetString(bytes, 3, 3);

        var header = new GifHeader(signature, version);

        if (!GifHeader.IsSupported(signature, version))
            throw new GifFormatException($"Not a supported GIF. Signature='{signature}', Version='{version}'.");

        // Logical Screen Descriptor: 7 bytes starting at offset 6
        ushort width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        byte packed = bytes[10];
        byte bgColorIndex = bytes[11];
        byte pixelAspect = bytes[12];

        var screen = new LogicalScreenDescriptor(width, height, packed, bgColorIndex, pixelAspect);

        return new GifFile(filePath, bytes, header, screen);
    }
}

public sealed class GifFormatException : Exception
{
    public GifFormatException(string message) : base(message) { }
}
