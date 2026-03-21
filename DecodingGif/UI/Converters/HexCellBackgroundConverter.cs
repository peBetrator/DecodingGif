using System.Globalization;
using System.Windows.Data;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;

namespace DecodingGif.UI.Converters;

public sealed class HexCellBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetCell(values, out var row, out int index, out int absoluteOffset))
            return BlockColorPalette.BuildBrush(GifBlockKind.Unknown, false, 0.1);

        int? selectedOffset = values.Length >= 3 && values[2] is int so ? so : null;
        int? activeRangeStart = values.Length >= 4 && values[3] is int rs ? rs : null;
        int? activeRangeEnd = values.Length >= 5 && values[4] is int re ? re : null;

        if (selectedOffset.HasValue && absoluteOffset == selectedOffset.Value)
            return BlockColorPalette.SelectedByteBrush();

        if (!row.TryGetCellVisualInfo(index, out var visual) || !visual.HasBlock)
            return BlockColorPalette.BuildBrush(GifBlockKind.Unknown, false, 0.1);

        bool isActive = activeRangeStart.HasValue
            && activeRangeEnd.HasValue
            && absoluteOffset >= activeRangeStart.Value
            && absoluteOffset <= activeRangeEnd.Value;

        return BlockColorPalette.BuildBrush(visual.Kind, isActive, visual.SizeFactor);
    }

    private static bool TryGetCell(object[] values, out HexRow row, out int index, out int absoluteOffset)
    {
        row = null!;
        index = -1;
        absoluteOffset = -1;
        if (values.Length < 2 || values[0] is not HexRow hexRow)
            return false;

        if (values[1] is not string header || header.Length != 2)
            return false;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index))
            return false;

        if (index is < 0 or > 15)
            return false;

        if (!hexRow.TryGetByte(index, out _))
            return false;

        row = hexRow;
        absoluteOffset = row.Offset + index;
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
