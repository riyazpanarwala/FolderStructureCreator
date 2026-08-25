using System.Globalization;
using System.Windows.Data;
using FolderStructureCreator.Views;

namespace FolderStructureCreator.Converters;

/// <summary>Converts bool (true -> Vertical, false -> Horizontal) to OrgChartLayoutDirection.</summary>
public class BoolToLayoutDirectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? OrgChartLayoutDirection.Vertical : OrgChartLayoutDirection.Horizontal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is OrgChartLayoutDirection dir && dir == OrgChartLayoutDirection.Vertical;
}
