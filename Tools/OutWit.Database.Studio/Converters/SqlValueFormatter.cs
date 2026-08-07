using System.Globalization;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Shared utilities for formatting SQL values for display and SQL generation.
/// </summary>
public static class SqlValueFormatter
{
    #region Constants

    public const string NULL_DISPLAY_TEXT = "(NULL)";
    public const string EMPTY_BLOB_TEXT = "(empty)";
    private const int MAX_BLOB_DISPLAY_LENGTH = 16;

    #endregion

    #region Display Formatting

    /// <summary>
    /// Writes a value for the screen, in the chosen format (WS-65).
    ///
    /// <para>
    /// <b>Every value comes back as a string, and that is the change.</b> Numbers and dates used to be
    /// returned unchanged with a comment saying the DataGrid would format them "culture-aware" - which
    /// it does, so on a ru-RU machine a DECIMAL was drawn as <c>4812,50</c> and a DATETIME as
    /// <c>28.06.2026</c>. Both are values a person copies into a statement, and neither parses. The
    /// grid now draws exactly what this returns and formats nothing itself.
    /// </para>
    /// <para>
    /// Sorting is unaffected: the columns bind to <c>Row.ItemArray[n]</c> and the converter only
    /// decorates the cell, so a sort still compares the typed value.
    /// </para>
    /// </summary>
    public static object? FormatForDisplay(object? value)
    {
        return FormatForDisplay(value, ValueFormat.Current);
    }

    /// <summary>
    /// The same, with the format given rather than taken from the static. Everything but the Avalonia
    /// converter uses this, and so does every test - a format nobody set is a global nobody notices.
    /// </summary>
    public static object? FormatForDisplay(object? value, ValueFormat format)
    {
        if (value == null || value == DBNull.Value)
            return NULL_DISPLAY_TEXT;

        return value switch
        {
            byte[] bytes => FormatBlobForDisplay(bytes, format),

            bool b => b ? "true" : "false",

            // Dates: ISO by default, whatever the machine says. ISO sorts as text and pastes into a
            // statement; 28.06.2026 does neither.
            DateTime dt => format.DatesAreIso
                ? dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture)
                : dt.ToString(format.DateCulture),
            DateOnly d => format.DatesAreIso
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : d.ToString(format.DateCulture),
            TimeOnly t => format.DatesAreIso
                ? t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : t.ToString(format.DateCulture),
            DateTimeOffset dto => format.DatesAreIso
                ? dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
                : dto.ToString(format.DateCulture),

            // Numbers: a dot and no group separator by default. The group separator is the half of
            // this that people forget - "4 812.50" does not paste either.
            decimal m => m.ToString(format.NumberCulture),
            double db => db.ToString(format.NumberCulture),
            float f => f.ToString(format.NumberCulture),
            Half h => ((double)h).ToString(format.NumberCulture),

            // Integers have no separator to get wrong under the default numeric format, so they are
            // left to render themselves.
            _ => value
        };
    }

    /// <summary>
    /// Formats a blob for display with truncation for large values.
    /// </summary>
    public static string FormatBlobForDisplay(byte[] bytes)
    {
        return FormatBlobForDisplay(bytes, ValueFormat.Current);
    }

    /// <summary>
    /// A BLOB in a cell, in one of the three ways the Data section offers: its size, its hex, or
    /// Base64. Size is the default because a column of truncated hex tells a person nothing they can
    /// use, and the cell viewer shows the bytes when they want them.
    /// </summary>
    public static string FormatBlobForDisplay(byte[] bytes, ValueFormat format)
    {
        if (bytes.Length == 0)
            return EMPTY_BLOB_TEXT;

        switch (format.Binary)
        {
            case ValueFormat.BINARY_BASE64:
                return Convert.ToBase64String(bytes);

            case ValueFormat.BINARY_SIZE:
                return $"({bytes.Length} bytes)";

            default:
                if (bytes.Length <= MAX_BLOB_DISPLAY_LENGTH)
                    return $"0x{BitConverter.ToString(bytes).Replace("-", "")}";

                var preview = BitConverter.ToString(bytes, 0, MAX_BLOB_DISPLAY_LENGTH).Replace("-", "");

                return $"0x{preview}... ({bytes.Length} bytes)";
        }
    }

    #endregion

    #region SQL Formatting

    /// <summary>
    /// Formats a value for use in SQL statements.
    /// Uses invariant culture for numeric types to ensure correct SQL syntax.
    /// </summary>
    /// <remarks>
    /// Supported WitSqlType mappings:
    /// - Null -> NULL
    /// - Integer (sbyte, byte, short, ushort, int, uint, long, ulong) -> numeric literal
    /// - Real (Half, float, double) -> numeric literal (invariant culture)
    /// - Decimal -> numeric literal (invariant culture)
    /// - Text (string) -> 'escaped string'
    /// - Blob (byte[]) -> X'hex'
    /// - Boolean -> TRUE/FALSE
    /// - DateTime -> 'yyyy-MM-dd HH:mm:ss'
    /// - DateOnly -> 'yyyy-MM-dd'
    /// - TimeOnly -> 'HH:mm:ss'
    /// - TimeSpan -> 'hh:mm:ss'
    /// - Guid -> 'guid-string'
    /// - DateTimeOffset -> 'yyyy-MM-dd HH:mm:ss zzz'
    /// - Json (JsonDocument/JsonElement) -> 'json string'
    /// - RowVersion (ulong) -> numeric literal
    /// </remarks>
    public static string FormatForSql(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            // Text types
            string str => $"'{EscapeString(str)}'",
            
            // Date/Time types - use fixed format for SQL
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss zzz}'",
            TimeSpan ts => $"'{ts:hh\\:mm\\:ss}'",
            
            // Boolean
            bool b => b ? "TRUE" : "FALSE",
            
            // Binary
            byte[] bytes => FormatBlobForSql(bytes),
            
            // Guid
            Guid guid => $"'{guid}'",
            
            // Floating point - invariant culture required for decimal separator
            Half h => ((double)h).ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            
            // Integer types - no culture needed but explicit for completeness
            sbyte sb => sb.ToString(CultureInfo.InvariantCulture),
            byte b => b.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            ushort us => us.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            
            // Json - serialize to string
            System.Text.Json.JsonDocument json => $"'{EscapeString(json.RootElement.GetRawText())}'",
            System.Text.Json.JsonElement elem => $"'{EscapeString(elem.GetRawText())}'",
            
            // Fallback
            _ => value.ToString() ?? "NULL"
        };
    }

    /// <summary>
    /// Formats a blob for SQL as hex literal.
    /// </summary>
    public static string FormatBlobForSql(byte[] bytes)
    {
        return $"X'{BitConverter.ToString(bytes).Replace("-", "")}'";
    }

    /// <summary>
    /// Escapes a string for use in SQL by doubling single quotes.
    /// </summary>
    public static string EscapeString(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Escapes an identifier for use in SQL (for bracket-style escaping).
    /// </summary>
    public static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("]", "]]");
    }

    #endregion
}
