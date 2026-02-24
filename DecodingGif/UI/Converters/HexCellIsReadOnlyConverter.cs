using System.Globalization;
using System.Windows.Data;
using DecodingGif.Core.Models;

namespace DecodingGif.UI.Converters;

public sealed class HexCellIsReadOnlyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // 0: HexRow.Offset (int)
        // 1: DataGridColumn.Header (string)
        // 2: IsSafeMode (bool)
        // 3: GctRange (GifByteRange)
        // 4: AllowSelectedLctEdit (bool)
        // 5: SelectedLctRange (GifByteRange)
        if (values.Length < 6)
            return true;

        if (values[0] is not int rowOffset)
            return true;

        if (values[1] is not string header || header.Length != 2)
            return true;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return true;

        if (index is < 0 or > 15)
            return true;

        int absoluteOffset = rowOffset + index;

        _ = values;
        _ = absoluteOffset;
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
