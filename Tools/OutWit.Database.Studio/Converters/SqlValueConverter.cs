using System.Globalization;
using Avalonia.Data.Converters;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converter that formats SQL values for display in DataGrid.
/// Displays "(NULL)" for null values and formats binary data as hex.
/// </summary>
public class SqlValueConverter : IValueConverter
{
    #region Constants

    /// <summary>
    /// Text displayed for NULL values. Use SqlValueFormatter.NULL_DISPLAY_TEXT for consistency.
    /// </summary>
    public const string NULL_DISPLAY_TEXT = SqlValueFormatter.NULL_DISPLAY_TEXT;

    #endregion

    #region IValueConverter

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return SqlValueFormatter.FormatForDisplay(value);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && str == NULL_DISPLAY_TEXT)
            return null;

        return value;
    }

    #endregion
}