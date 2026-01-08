using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class GifStructureService
{
    public IReadOnlyList<GifByteRange> BuildRanges(GifFile file)
    {
        var bytes = file.Bytes;
        var ranges = new List<GifByteRange>();

        // 1) Header + LSD
        ranges.Add(new GifByteRange(GifBlockKind.Header, "Header (Signature+Version)", 0, 6));
        ranges.Add(new GifByteRange(GifBlockKind.LogicalScreenDescriptor, "Logical Screen Descriptor (LSD)", 6, 7));

        // 2) Global Color Table (если есть)
        int offset = 13; // после Header(6) + LSD(7)

        if (file.Screen.GlobalColorTableFlag)
        {
            int gctSize = file.Screen.GlobalColorTableSize;
            int gctLength = 3 * gctSize;

            if (offset + gctLength <= bytes.Length)
            {
                ranges.Add(new GifByteRange(GifBlockKind.GlobalColorTable, $"Global Color Table (GCT) x{gctSize}", offset, gctLength));
                offset += gctLength;
            }
            else
            {
                // Файл битый, но хоть первые блоки покажем
                ranges.Add(new GifByteRange(GifBlockKind.GlobalColorTable, $"Global Color Table (GCT) x{gctSize} (truncated)", offset, Math.Max(0, bytes.Length - offset)));
                return ranges;
            }
        }

        // 3) Дальше сканируем блоки до Trailer
        while (offset < bytes.Length)
        {
            byte b = bytes[offset];

            // Trailer
            if (b == 0x3B)
            {
                ranges.Add(new GifByteRange(GifBlockKind.Trailer, "Trailer (0x3B)", offset, 1));
                break;
            }

            // Extension blocks: 0x21
            if (b == 0x21)
            {
                if (offset + 1 >= bytes.Length)
                {
                    ranges.Add(new GifByteRange(GifBlockKind.Unknown, "Extension (truncated)", offset, bytes.Length - offset));
                    break;
                }

                byte label = bytes[offset + 1];

                if (label == 0xF9)
                {
                    // Graphic Control Extension: 21 F9 04 [4 bytes] 00
                    int len = ReadGraphicControlExtensionLength(bytes, offset);
                    ranges.Add(new GifByteRange(GifBlockKind.GraphicControlExtension, "Graphic Control Extension (GCE)", offset, len));
                    offset += len;
                    continue;
                }

                if (label == 0xFF)
                {
                    // Application Extension: 21 FF [blockSize] [appId(11)] [sub-blocks] 00
                    int len = ReadApplicationExtensionLength(bytes, offset);
                    ranges.Add(new GifByteRange(GifBlockKind.ApplicationExtension, "Application Extension (AppExt)", offset, len));
                    offset += len;
                    continue;
                }

                // Generic extension: 21 <label> <blockSize> <blockData...> <sub-blocks...> 00
                int genericLen = ReadGenericExtensionLength(bytes, offset);
                ranges.Add(new GifByteRange(GifBlockKind.Unknown, $"Extension (0x21 0x{label:X2})", offset, genericLen));
                offset += genericLen;
                continue;
            }

            // Image Descriptor: 0x2C
            if (b == 0x2C)
            {
                // Image Descriptor: 10 bytes total (includes 0x2C)
                if (offset + 10 > bytes.Length)
                {
                    ranges.Add(new GifByteRange(GifBlockKind.ImageDescriptor, "Image Descriptor (truncated)", offset, bytes.Length - offset));
                    break;
                }

                ranges.Add(new GifByteRange(GifBlockKind.ImageDescriptor, "Image Descriptor", offset, 10));

                byte packed = bytes[offset + 9];
                bool lctFlag = (packed & 0b1000_0000) != 0;
                int lctSize = 1 << ((packed & 0b0000_0111) + 1);

                offset += 10;

                // Local Color Table (если есть)
                if (lctFlag)
                {
                    int lctLen = 3 * lctSize;

                    if (offset + lctLen > bytes.Length)
                    {
                        ranges.Add(new GifByteRange(GifBlockKind.LocalColorTable, $"Local Color Table (LCT) x{lctSize} (truncated)", offset, Math.Max(0, bytes.Length - offset)));
                        break;
                    }

                    ranges.Add(new GifByteRange(GifBlockKind.LocalColorTable, $"Local Color Table (LCT) x{lctSize}", offset, lctLen));
                    offset += lctLen;
                }

                // Image Data: LZW min code size + sub-blocks chain until 0x00
                int imageDataLen = ReadImageDataLength(bytes, offset);
                ranges.Add(new GifByteRange(GifBlockKind.ImageData, "Image Data (LZW sub-blocks)", offset, imageDataLen));
                offset += imageDataLen;

                continue;
            }

            // Если встретили неизвестный байт - чтобы не зациклиться:
            // оформим как Unknown 1 byte и двигаемся дальше.
            ranges.Add(new GifByteRange(GifBlockKind.Unknown, $"Unknown byte 0x{b:X2}", offset, 1));
            offset += 1;
        }

        return ranges;
    }

    public string DescribeOffset(GifFile file, int offset)
    {
        if (offset < 0 || offset >= file.Bytes.Length)
            return "Out of file bounds";

        // Быстрое описание старых частей (Header/LSD/GCT)
        if (offset is >= 0 and <= 5)
        {
            return offset switch
            {
                0 => "Header: Signature 'G'",
                1 => "Header: Signature 'I'",
                2 => "Header: Signature 'F'",
                3 => "Header: Version char #1 ('8')",
                4 => "Header: Version char #2 ('7' or '9')",
                5 => "Header: Version char #3 ('a')",
                _ => "Header"
            };
        }

        if (offset is >= 6 and <= 12)
        {
            return offset switch
            {
                6 or 7 => "LSD: Width (UInt16, little-endian)",
                8 or 9 => "LSD: Height (UInt16, little-endian)",
                10 => "LSD: Packed fields (GCT flag, color resolution, sort, GCT size)",
                11 => "LSD: Background Color Index",
                12 => "LSD: Pixel Aspect Ratio",
                _ => "Logical Screen Descriptor"
            };
        }

        // Для новых блоков делаем описание по ranges
        var ranges = BuildRanges(file);
        var block = ranges.FirstOrDefault(r => r.Contains(offset));
        if (block is null)
            return "Data: (unknown region)";

        return block.Kind switch
        {
            GifBlockKind.GlobalColorTable => DescribeColorTableOffset("GCT", block, offset),
            GifBlockKind.LocalColorTable => DescribeColorTableOffset("LCT", block, offset),

            GifBlockKind.ImageDescriptor => DescribeImageDescriptorOffset(block, offset),

            GifBlockKind.ImageData => DescribeImageDataOffset(block, offset),

            GifBlockKind.GraphicControlExtension => DescribeGceOffset(block, offset),

            GifBlockKind.ApplicationExtension => DescribeAppExtOffset(block, offset),

            GifBlockKind.Trailer => "Trailer (0x3B): End of GIF",

            _ => $"Block: {block.Name}"
        };
    }

    private static string DescribeColorTableOffset(string name, GifByteRange tableRange, int offset)
    {
        int rel = offset - tableRange.Start;
        int colorIndex = rel / 3;
        int channel = rel % 3;

        string channelName = channel switch
        {
            0 => "R",
            1 => "G",
            2 => "B",
            _ => "?"
        };

        return $"{name}: Color #{colorIndex} channel {channelName} (RGB)";
    }

    private static string DescribeImageDescriptorOffset(GifByteRange range, int offset)
    {
        int rel = offset - range.Start; // 0..9
        return rel switch
        {
            0 => "Image Descriptor: Separator (0x2C)",
            1 or 2 => "Image Descriptor: Left Position (UInt16 LE)",
            3 or 4 => "Image Descriptor: Top Position (UInt16 LE)",
            5 or 6 => "Image Descriptor: Width (UInt16 LE)",
            7 or 8 => "Image Descriptor: Height (UInt16 LE)",
            9 => "Image Descriptor: Packed (LCT flag, interlace, sort, LCT size)",
            _ => "Image Descriptor"
        };
    }

    private static string DescribeImageDataOffset(GifByteRange range, int offset)
    {
        int rel = offset - range.Start;
        return rel switch
        {
            0 => "Image Data: LZW Minimum Code Size",
            _ => "Image Data: Compressed sub-block bytes"
        };
    }

    private static string DescribeGceOffset(GifByteRange range, int offset)
    {
        int rel = offset - range.Start;
        return rel switch
        {
            0 => "GCE: Extension Introducer (0x21)",
            1 => "GCE: Graphic Control Label (0xF9)",
            2 => "GCE: Block Size (usually 0x04)",
            3 => "GCE: Packed (disposal method, user input flag, transparency flag)",
            4 or 5 => "GCE: Delay Time (UInt16 LE, in 1/100s)",
            6 => "GCE: Transparent Color Index",
            _ => "GCE: Block Terminator (0x00)"
        };
    }

    private static string DescribeAppExtOffset(GifByteRange range, int offset)
    {
        int rel = offset - range.Start;
        return rel switch
        {
            0 => "AppExt: Extension Introducer (0x21)",
            1 => "AppExt: Application Extension Label (0xFF)",
            2 => "AppExt: Block Size (usually 0x0B)",
            >= 3 and <= 13 => "AppExt: Application Identifier + Auth Code (11 bytes)",
            _ => "AppExt: Application Data sub-block bytes"
        };
    }

    private static int ReadGraphicControlExtensionLength(byte[] bytes, int start)
    {
        // 21 F9 04 [4 bytes] 00  => total 8 bytes
        // Но будем осторожны: найдём terminator
        int min = 2 + 1; // introducer+label+blockSize
        if (start + min > bytes.Length)
            return bytes.Length - start;

        // GCE по спецификации фиксированный, но терминатор всегда 0x00 после данных
        // Формат: 21 F9 04 <4 bytes> 00
        int expected = 8;
        if (start + expected <= bytes.Length)
            return expected;

        return bytes.Length - start;
    }

    private static int ReadApplicationExtensionLength(byte[] bytes, int start)
    {
        // 21 FF <blockSize> <blockData(blockSize bytes)> <sub-blocks...> 00
        if (start + 3 > bytes.Length)
            return bytes.Length - start;

        int blockSize = bytes[start + 2];
        int pos = start + 3 + blockSize;
        if (pos > bytes.Length)
            return bytes.Length - start;

        int subLen = ReadSubBlocksTotalLength(bytes, pos, out _);
        return (pos + subLen) - start;
    }

    private static int ReadGenericExtensionLength(byte[] bytes, int start)
    {
        if (start + 3 > bytes.Length)
            return bytes.Length - start;

        int blockSize = bytes[start + 2];
        int pos = start + 3 + blockSize;
        if (pos > bytes.Length)
            return bytes.Length - start;

        int subLen = ReadSubBlocksTotalLength(bytes, pos, out _);
        return (pos + subLen) - start;
    }

    private static int ReadImageDataLength(byte[] bytes, int start)
    {
        // start: LZW minimum code size, затем sub-blocks до 0x00
        if (start >= bytes.Length)
            return 0;

        int pos = start + 1;
        int subLen = ReadSubBlocksTotalLength(bytes, pos, out _);
        return (pos + subLen) - start;
    }

    private static int ReadSubBlocksTotalLength(byte[] bytes, int start, out bool terminated)
    {
        // Sub-blocks: <size><data...> ... until size=0
        terminated = false;

        int pos = start;
        while (pos < bytes.Length)
        {
            int size = bytes[pos];
            pos += 1;

            if (size == 0)
            {
                terminated = true;
                break;
            }

            // skip data bytes
            pos += size;

            if (pos > bytes.Length)
                break;
        }

        return pos - start;
    }
}
