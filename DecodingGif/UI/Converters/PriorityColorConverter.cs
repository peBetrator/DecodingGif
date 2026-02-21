using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DecodingGif.Core.Models;
using WpfBrushes = System.Windows.Media.Brushes;

namespace DecodingGif.UI.Converters;

public sealed class PriorityColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SuggestionPriority p)
            return WpfBrushes.Gray;

        return p switch
        {
            SuggestionPriority.High => WpfBrushes.IndianRed,
            SuggestionPriority.Medium => WpfBrushes.DarkOrange,
            _ => WpfBrushes.SteelBlue
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
