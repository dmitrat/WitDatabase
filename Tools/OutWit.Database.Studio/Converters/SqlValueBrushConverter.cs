using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converter that returns a specific brush for null values.
/// </summary>
public class SqlValueBrushConverter : IValueConverter
{
    #region Constants

    private const string NULL_BRUSH_COLOR = "#808080"; // Gray color for null values
    private const string NORMAL_BRUSH_COLOR = "#000000"; // Black color for normal values

    #endregion

    #region IValueConverter

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || value == DBNull.Value)
            return NullBrush;

        return NormalBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    #endregion

    #region Properties

    public IBrush NullBrush { get; set; } = SolidColorBrush.Parse(NULL_BRUSH_COLOR);

    public IBrush NormalBrush { get; set; } = SolidColorBrush.Parse(NORMAL_BRUSH_COLOR);

    #endregion
}