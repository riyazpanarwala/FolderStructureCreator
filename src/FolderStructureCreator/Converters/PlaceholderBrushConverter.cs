using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FolderStructureCreator.Converters;

/// <summary>true (is a "Loading.../empty" placeholder) -> secondary muted brush; false -> primary text brush.</summary>
public class PlaceholderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (app == null)
            return Brushes.Black;

        if (value is bool isPlaceholder && isPlaceholder)
        {
            return app.TryFindResource("BrushTextSecondary") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }

        return app.TryFindResource("BrushTextPrimary") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
