using System.Collections.ObjectModel;
using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class HexRowsBuilder
{
    public ObservableCollection<HexRow> Build(byte[] bytes, int bytesPerRow = 16)
    {
        var rows = new ObservableCollection<HexRow>();

        if (bytes is null || bytes.Length == 0)
            return rows;

        for (int offset = 0; offset < bytes.Length; offset += bytesPerRow)
        {
            int remaining = bytes.Length - offset;
            int take = Math.Min(bytesPerRow, remaining);
            var slice = new ReadOnlySpan<byte>(bytes, offset, take);
            rows.Add(new HexRow(offset, slice));
        }

        return rows;
    }
}
