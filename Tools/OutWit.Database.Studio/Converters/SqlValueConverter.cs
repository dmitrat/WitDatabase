using System.Globalization;
using Avalonia.Data.Converters;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converter that displays "(NULL)" for null or empty values.
/// </summary>
public class SqlValueConverter : IValueConverter
{
    #region Constants

    public const string NULL_DISPLAY_TEXT = "(NULL)";

    #endregion

    #region IValueConverter

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || value == DBNull.Value)
            return NULL_DISPLAY_TEXT;

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is NULL_DISPLAY_TEXT 
            ? null 
            : value;
    }

    #endregion
}