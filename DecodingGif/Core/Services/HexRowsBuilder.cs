using DecodingGif.Core.Editing;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class HexRowsBuilder
{
    public IList<HexRow> Build(byte[] bytes, IByteEditPolicy policy, IReadOnlyList<GifByteRange>? blocks = null, int bytesPerRow = 16)
    {
        IReadOnlyList<GifByteRange> sortedBlocks = blocks?
            .OrderBy(block => block.Start)
            .ToArray()
            ?? Array.Empty<GifByteRange>();

        return new VirtualHexRowCollection(bytes, policy, sortedBlocks, bytesPerRow);
    }
}
