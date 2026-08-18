using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderStructureCreator.Converters;

/// <summary>
/// true -> GridLength(0) (column collapses to no width).
/// false -> the GridLength given in ConverterParameter ("Auto", "*", "1.2*", or a plain number for Star).
/// Used to fully collapse the left directory panel when the org-chart view wants full window width.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool collapse && collapse)
            return new GridLength(0);

        var spec = parameter as string ?? "1*";

        if (string.Equals(spec, "Auto", StringComparison.OrdinalIgnoreCase))
            return GridLength.Auto;

        var starText = spec.EndsWith("*") ? spec[..^1] : spec;
        var factor = string.IsNullOrEmpty(starText) ? 1.0 : double.Parse(starText, CultureInfo.InvariantCulture);
        return new GridLength(factor, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
