using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderStructureCreator.Converters;

/// <summary>true -> Collapsed, false -> Visible. Used to show the TextBlock when NOT editing.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
