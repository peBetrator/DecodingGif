using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class GifStructureService
{
    public IReadOnlyList<GifByteRange> BuildRanges(GifFile file)
    {
        var ranges = new List<GifByteRange>
        {
            new(GifBlockKind.Header, "Header (Signature+Version)", 0, 6),
            new(GifBlockKind.LogicalScreenDescriptor, "Logical Screen Descriptor (LSD)", 6, 7)
        };

        int gctStart = 13;
        if (file.Screen.GlobalColorTableFlag)
        {
            int gctSize = file.Screen.GlobalColorTableSize;
            int gctLength = 3 * gctSize;
            ranges.Add(new GifByteRange(GifBlockKind.GlobalColorTable, $"Global Color Table (GCT) x{gctSize}", gctStart, gctLength));
        }

        return ranges;
    }

    public string DescribeOffset(GifFile file, int offset)
    {
        if (offset < 0 || offset >= file.Bytes.Length)
            return "Out of file bounds";

        // Header
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

        // LSD (6..12)
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

        // GCT (если есть)
        if (file.Screen.GlobalColorTableFlag)
        {
            int gctStart = 13;
            int gctSize = file.Screen.GlobalColorTableSize;
            int gctLen = 3 * gctSize;
            int gctEndExcl = gctStart + gctLen;

            if (offset >= gctStart && offset < gctEndExcl)
            {
                int rel = offset - gctStart;
                int colorIndex = rel / 3;
                int channel = rel % 3;

                string channelName = channel switch
                {
                    0 => "R",
                    1 => "G",
                    2 => "B",
                    _ => "?"
                };

                return $"GCT: Color #{colorIndex} channel {channelName} (RGB)";
            }
        }

        return "Data: (not parsed yet)";
    }
}
