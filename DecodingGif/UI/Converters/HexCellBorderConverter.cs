using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;

namespace DecodingGif.UI.Converters;

public sealed class HexCellBorderConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetAbsoluteOffset(values, out int absoluteOffset))
            return new Thickness(0);

        if (values.Length < 3 || values[2] is not IEnumerable<GifByteRange> blocks)
            return new Thickness(0);

        var block = HexBlockLookupCache.Get(blocks).FindContaining(absoluteOffset);
        if (block is null)
            return new Thickness(0);

        bool left = absoluteOffset == block.Start;
        bool right = absoluteOffset == block.EndInclusive;
        return new Thickness(left ? 1.2 : 0.0, 0.6, right ? 1.2 : 0.0, 0.6);
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
