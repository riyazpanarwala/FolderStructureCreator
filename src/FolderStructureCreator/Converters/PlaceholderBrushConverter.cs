using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FolderStructureCreator.Converters;

/// <summary>true (is a "Loading.../empty" placeholder) -> muted gray text; false -> normal text.</summary>
public class PlaceholderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x9C, 0xA1, 0xAA));
    private static readonly SolidColorBrush Normal = new(Color.FromRgb(0x1B, 0x1F, 0x27));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Muted : Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
