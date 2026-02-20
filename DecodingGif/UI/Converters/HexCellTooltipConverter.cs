using System.Globalization;
using System.Windows.Data;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;

namespace DecodingGif.UI.Converters;

public sealed class HexCellTooltipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetAbsoluteOffset(values, out int absoluteOffset))
            return string.Empty;

        if (values.Length < 3 || values[2] is not IEnumerable<GifByteRange> blocks)
            return string.Empty;

        var block = HexBlockLookupCache.Get(blocks).FindContaining(absoluteOffset);
        if (block is null)
            return $"Offset: 0x{absoluteOffset:X8}";

        var info = BlockColorPalette.Get(block.Kind);
        return $"{info.Label}\nRange: 0x{block.Start:X8}..0x{block.EndInclusive:X8}\nSize: {block.Length} bytes\nByte: 0x{absoluteOffset:X8}";
    }

    private static bool TryGetAbsoluteOffset(object[] values, out int absoluteOffset)
    {
        absoluteOffset = -1;
        if (values.Length < 2 || values[0] is not int rowOffset)
            return false;

        if (values[1] is not string header || header.Length != 2)
            return false;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return false;

        if (index is < 0 or > 15)
            return false;

        absoluteOffset = rowOffset + index;
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
