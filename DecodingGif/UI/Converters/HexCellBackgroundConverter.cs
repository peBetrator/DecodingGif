using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DecodingGif.Core.Models;

namespace DecodingGif.UI.Converters;

public sealed class HexCellBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // values:
        // 0: HexRow.Offset (int)
        // 1: DataGridColumn.Header (string)
        // 2: Blocks (IEnumerable<GifByteRange>)
        // 3: SelectedByte.Offset (int?) optional

        if (values.Length < 3)
            return Brushes.Transparent;

        if (values[0] is not int rowOffset)
            return Brushes.Transparent;

        if (values[1] is not string header || header.Length != 2)
            return Brushes.Transparent;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return Brushes.Transparent;

        if (index is < 0 or > 15)
            return Brushes.Transparent;

        int absoluteOffset = rowOffset + index;

        // Selected byte highlight (optional)
        int? selectedOffset = null;
        if (values.Length >= 4 && values[3] is int so)
            selectedOffset = so;

        if (selectedOffset.HasValue && absoluteOffset == selectedOffset.Value)
            return Brushes.Gold; // выбранный байт поверх всего

        if (values[2] is not IEnumerable<GifByteRange> blocks)
            return Brushes.Transparent;

        var block = blocks.FirstOrDefault(b => b.Contains(absoluteOffset));
        if (block is null)
            return Brushes.Transparent;

        return block.Kind switch
        {
            GifBlockKind.Header => Brushes.LightSkyBlue,
            GifBlockKind.LogicalScreenDescriptor => Brushes.LightGreen,
            GifBlockKind.GlobalColorTable => Brushes.LightPink,

            GifBlockKind.GraphicControlExtension => Brushes.Plum,
            GifBlockKind.ApplicationExtension => Brushes.Khaki,

            GifBlockKind.ImageDescriptor => Brushes.LightSteelBlue,
            GifBlockKind.LocalColorTable => Brushes.LightSalmon,
            GifBlockKind.ImageData => Brushes.LightGray,

            GifBlockKind.Trailer => Brushes.LightCyan,

            _ => Brushes.Transparent
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
