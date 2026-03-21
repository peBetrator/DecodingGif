using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DecodingGif.Core.Models;

namespace DecodingGif.UI.Converters;

public sealed class HexCellBorderConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetCell(values, out var row, out int index))
            return new Thickness(0);

        if (!row.TryGetCellVisualInfo(index, out var visual) || !visual.HasBlock)
            return new Thickness(0);

        return new Thickness(visual.IsLeftBoundary ? 1.2 : 0.0, 0.6, visual.IsRightBoundary ? 1.2 : 0.0, 0.6);
    }

    private static bool TryGetCell(object[] values, out HexRow row, out int index)
    {
        row = null!;
        index = -1;
        if (values.Length < 2 || values[0] is not HexRow hexRow)
            return false;

        if (values[1] is not string header || header.Length != 2)
            return false;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index))
            return false;

        if (index is < 0 or > 15)
            return false;

        row = hexRow;
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
