using System.Globalization;
using System.Windows.Data;
using MediaBrushes = System.Windows.Media.Brushes;
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
            return MediaBrushes.Transparent;

        if (values[0] is not int rowOffset)
            return MediaBrushes.Transparent;

        if (values[1] is not string header || header.Length != 2)
            return MediaBrushes.Transparent;

        if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int index))
            return MediaBrushes.Transparent;

        if (index is < 0 or > 15)
            return MediaBrushes.Transparent;

        int absoluteOffset = rowOffset + index;

        // Selected byte highlight (optional)
        int? selectedOffset = null;
        if (values.Length >= 4 && values[3] is int so)
            selectedOffset = so;

        if (selectedOffset.HasValue && absoluteOffset == selectedOffset.Value)
            return MediaBrushes.Gold; // выбранный байт поверх всего

        if (values[2] is not IEnumerable<GifByteRange> blocks)
            return MediaBrushes.Transparent;

        var block = blocks.FirstOrDefault(b => b.Contains(absoluteOffset));
        if (block is null)
            return MediaBrushes.Transparent;

        return block.Kind switch
        {
            GifBlockKind.Header => MediaBrushes.LightSkyBlue,
            GifBlockKind.LogicalScreenDescriptor => MediaBrushes.LightGreen,
            GifBlockKind.GlobalColorTable => MediaBrushes.LightPink,

            GifBlockKind.GraphicControlExtension => MediaBrushes.Plum,
            GifBlockKind.ApplicationExtension => MediaBrushes.Khaki,

            GifBlockKind.ImageDescriptor => MediaBrushes.LightSteelBlue,
            GifBlockKind.LocalColorTable => MediaBrushes.LightSalmon,
            GifBlockKind.ImageData => MediaBrushes.LightGray,

            GifBlockKind.Trailer => MediaBrushes.LightCyan,

            _ => MediaBrushes.Transparent
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
