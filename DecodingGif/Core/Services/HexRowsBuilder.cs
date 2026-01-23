using System.Collections.ObjectModel;
using DecodingGif.Core.Editing;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class HexRowsBuilder
{
    public ObservableCollection<HexRow> Build(byte[] bytes, IByteEditPolicy policy, int bytesPerRow = 16)
    {
        var rows = new ObservableCollection<HexRow>();

        if (bytes is null || bytes.Length == 0)
            return rows;

        for (int offset = 0; offset < bytes.Length; offset += bytesPerRow)
        {
            rows.Add(new HexRow(offset, bytes, policy));
        }

        return rows;
    }
}
