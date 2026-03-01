using DecodingGif.Core.Editing;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class HexRowsBuilder
{
    public IList<HexRow> Build(byte[] bytes, IByteEditPolicy policy, int bytesPerRow = 16) =>
        new VirtualHexRowCollection(bytes, policy, bytesPerRow);
}
