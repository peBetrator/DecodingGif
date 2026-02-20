using System.Globalization;
using System.Windows.Data;
using DecodingGif.Core.Models;
using DecodingGif.UI.Visualization;

namespace DecodingGif.UI.Converters;

public sealed class HexCellBackgroundConverter : IMultiValueConverter
{
    private static int? _lastActiveOffset;
    private static HexBlockLookup? _lastLookup;
    private static GifByteRange? _lastActiveRange;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetAbsoluteOffset(values, out int absoluteOffset))
            return BlockColorPalette.BuildBrush(GifBlockKind.Unknown, false, 0.1);

        int? selectedOffset = values.Length >= 4 && values[3] is int so ? so : null;
        int? hoveredOffset = values.Length >= 5 && values[4] is int ho ? ho : null;

        if (selectedOffset.HasValue && absoluteOffset == selectedOffset.Value)
            return BlockColorPalette.SelectedByteBrush();

        if (values.Length < 3 || values[2] is not IEnumerable<GifByteRange> blocks)
            return BlockColorPalette.BuildBrush(GifBlockKind.Unknown, false, 0.1);

        var lookup = HexBlockLookupCache.Get(blocks);
        var block = lookup.FindContaining(absoluteOffset);
        if (block is null)
            return BlockColorPalette.BuildBrush(GifBlockKind.Unknown, false, 0.1);

        var activeRange = GetActiveRange(lookup, hoveredOffset ?? selectedOffset);
        bool isActive = activeRange is not null && activeRange.Contains(absoluteOffset);
        double sizeFactor = 0.75 + (Math.Min(block.Length, lookup.MaxBlockLength) / (double)lookup.MaxBlockLength * 0.5);
        return BlockColorPalette.BuildBrush(block.Kind, isActive, sizeFactor);
    }

    private static GifByteRange? GetActiveRange(HexBlockLookup lookup, int? activeOffset)
    {
        if (!activeOffset.HasValue)
            return null;

        if (_lastLookup == lookup && _lastActiveOffset == activeOffset.Value)
            return _lastActiveRange;

        var range = lookup.GetCompleteRangeForOffset(activeOffset.Value);
        _lastLookup = lookup;
        _lastActiveOffset = activeOffset.Value;
        _lastActiveRange = range;
        return range;
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
