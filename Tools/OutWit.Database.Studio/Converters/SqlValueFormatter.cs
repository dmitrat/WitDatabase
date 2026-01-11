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
    /// Formats a value for display in UI (DataGrid, etc.).
    /// Returns the original value for types that DataGrid renders well natively,
    /// and formatted strings only for complex types (blobs, booleans).
    /// </summary>
    /// <remarks>
    /// Types returned as-is (DataGrid handles them with culture-aware formatting):
    /// - Numeric types (int, long, float, double, decimal, etc.)
    /// - String
    /// - DateTime, DateOnly, TimeOnly, DateTimeOffset, TimeSpan (culture-aware)
    /// - Guid
    /// 
    /// Types formatted explicitly:
    /// - null/DBNull -> "(NULL)"
    /// - byte[] -> hex preview
    /// - bool -> "true"/"false" (consistent lowercase)
    /// </remarks>
    public static object? FormatForDisplay(object? value)
    {
        if (value == null || value == DBNull.Value)
            return NULL_DISPLAY_TEXT;

        return value switch
        {
            // Only format types that need special handling
            byte[] bytes => FormatBlobForDisplay(bytes),
            bool b => b ? "true" : "false",  // Consistent lowercase
            
            // All other types - return as-is, let DataGrid format them
            // This includes: numbers, strings, DateTime, DateOnly, TimeOnly,
            // DateTimeOffset, TimeSpan, Guid, JsonDocument, etc.
            _ => value
        };
    }

    /// <summary>
    /// Formats a blob for display with truncation for large values.
    /// </summary>
    public static string FormatBlobForDisplay(byte[] bytes)
    {
        if (bytes.Length == 0)
            return EMPTY_BLOB_TEXT;

        if (bytes.Length <= MAX_BLOB_DISPLAY_LENGTH)
            return $"0x{BitConverter.ToString(bytes).Replace("-", "")}";

        var preview = BitConverter.ToString(bytes, 0, MAX_BLOB_DISPLAY_LENGTH).Replace("-", "");
        return $"0x{preview}... ({bytes.Length} bytes)";
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
