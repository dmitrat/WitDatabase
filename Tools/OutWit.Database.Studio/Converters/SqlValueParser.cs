using System.Globalization;
using System.Text.Json;
using OutWit.Database.Types;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converts string input to the CLR type a column holds.
///
/// <para>
/// <b>Input is more tolerant than output (WS-66).</b> Studio writes a value one way - a dot, ISO -
/// and accepts several: <c>4812.50</c> and <c>4812,50</c>, <c>2026-06-28</c> and <c>28.06.2026</c>,
/// and a number typed with the group separators a spreadsheet put in. Strictness on the way in
/// protects nothing here - the value is about to be parsed by the engine anyway - and it costs a
/// correction every time someone types the way their keyboard is laid out.
/// </para>
/// <para>
/// What makes that safe rather than sloppy is that the caller can show what the text BECAME:
/// <see cref="Canonical"/> renders the parsed value the way Studio would write it, so an ambiguous
/// date is a thing the user sees rather than a thing they find out about later.
/// </para>
/// </summary>
public static class SqlValueParser
{
    /// <summary>
    /// Parses a string value to the target CLR type.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="targetType">The target CLR type.</param>
    /// <returns>The parsed value or DBNull.Value for empty/null input.</returns>
    /// <exception cref="FormatException">When parsing fails.</exception>
    public static object? Parse(string? text, Type targetType)
    {
        if (string.IsNullOrEmpty(text))
            return DBNull.Value;

        // Get the underlying type for nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Get the SQL type for this CLR type
        var sqlType = WitTypeConverter.GetSqlType(underlyingType);

        return ParseBySqlType(text, sqlType, underlyingType);
    }

    /// <summary>
    /// Parses a string value based on WitSqlType.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="sqlType">The SQL type.</param>
    /// <returns>The parsed value.</returns>
    public static object? ParseBySqlType(string text, WitSqlType sqlType)
    {
        var clrType = WitTypeConverter.GetClrType(sqlType);
        return ParseBySqlType(text, sqlType, clrType);
    }

    /// <summary>
    /// Parses a string value based on SQL type name (from database schema).
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="sqlTypeName">The SQL type name (e.g., "VARCHAR", "INTEGER", "DATETIME").</param>
    /// <returns>The parsed value.</returns>
    public static object? ParseBySqlTypeName(string text, string sqlTypeName)
    {
        if (string.IsNullOrEmpty(text))
            return DBNull.Value;

        var sqlType = WitTypeConverter.ParseSqlTypeName(sqlTypeName);
        return ParseBySqlType(text, sqlType);
    }

    private static object? ParseBySqlType(string text, WitSqlType sqlType, Type clrType)
    {
        if (string.IsNullOrEmpty(text))
            return DBNull.Value;

        return sqlType switch
        {
            WitSqlType.Null => DBNull.Value,

            WitSqlType.Integer => ParseInteger(text, clrType),

            WitSqlType.Real => ParseReal(text, clrType),

            WitSqlType.Decimal => decimal.Parse(NormaliseNumber(text), CultureInfo.InvariantCulture),

            WitSqlType.Boolean => ParseBoolean(text),

            WitSqlType.Text => text,

            WitSqlType.Blob => ParseBlob(text),

            WitSqlType.DateTime => ParseDateTime(text),

            WitSqlType.DateOnly => DateOnly.FromDateTime(ParseDateTime(text)),

            WitSqlType.TimeOnly => TimeOnly.Parse(text, CultureInfo.InvariantCulture),

            WitSqlType.TimeSpan => TimeSpan.Parse(text, CultureInfo.InvariantCulture),

            WitSqlType.Guid => Guid.Parse(text),

            WitSqlType.DateTimeOffset => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),

            WitSqlType.Json => ParseJson(text),

            WitSqlType.RowVersion => ulong.Parse(NormaliseNumber(text), CultureInfo.InvariantCulture),

            _ => text
        };
    }

    /// <summary>
    /// Turns what a person typed into something the invariant parser reads (WS-66).
    ///
    /// <para>
    /// The rule, and its one real ambiguity: when both a dot and a comma are present the LAST of them
    /// is the decimal separator and the other is a group separator - which reads <c>1,234.56</c> and
    /// <c>1.234,56</c> correctly. A lone comma is a DECIMAL separator, not a group one, because that
    /// is what someone typing on a Russian keyboard means; <c>1,234</c> therefore becomes 1.234 and
    /// not 1234, and the caller shows the parsed value back so that is visible rather than surprising.
    /// </para>
    /// <para>
    /// Spaces are dropped whatever kind they are: a value pasted out of a spreadsheet carries a
    /// non-breaking or narrow space between the groups, and those are three different characters that
    /// all look like one.
    /// </para>
    /// </summary>
    public static string NormaliseNumber(string text)
    {
        // char.IsWhiteSpace is what recognises U+00A0 and U+202F as well as an ordinary space -
        // a value pasted out of a spreadsheet carries one of those between its groups. Writing the
        // literals here instead would put non-ASCII bytes into a source file for no gain.
        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');

        if (lastDot >= 0 && lastComma >= 0)
        {
            return lastComma > lastDot
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }

        return lastComma >= 0 ? cleaned.Replace(',', '.') : cleaned;
    }

    /// <summary>
    /// ISO first, then the machine's own culture, then the day-first forms.
    ///
    /// <para>
    /// The order is the decision. <c>06/07/2026</c> is genuinely ambiguous, so it is read the way the
    /// person's own machine writes dates - which is what they meant by typing it - rather than by a
    /// rule Studio invented. ISO comes first because it is what Studio itself writes, so a value
    /// copied out of the grid and pasted back always means what it says.
    /// </para>
    /// </summary>
    public static DateTime ParseDateTime(string text)
    {
        var trimmed = text.Trim();

        string[] iso = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];

        if (DateTime.TryParseExact(trimmed, iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoValue))
            return isoValue;

        if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out var localValue))
            return localValue;

        string[] dayFirst =
        [
            "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss",
            "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"
        ];

        if (DateTime.TryParseExact(trimmed, dayFirst, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayValue))
            return dayValue;

        // Last resort, and it is what the strict version always did: the invariant parser, whose
        // failure message is the one worth showing.
        return DateTime.Parse(trimmed, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// How Studio would write the parsed value - what the editor shows next to the field so that an
    /// ambiguous date is seen rather than discovered (WS-66).
    /// </summary>
    public static string Canonical(object? value)
    {
        return SqlValueFormatter.FormatForDisplay(value, ValueFormat.Default)?.ToString() ?? string.Empty;
    }

    private static object ParseInteger(string text, Type clrType)
    {
        // Parse as long first, then convert to specific integer type
        var longValue = long.Parse(NormaliseNumber(text), CultureInfo.InvariantCulture);

        if (clrType == typeof(sbyte)) return (sbyte)longValue;
        if (clrType == typeof(byte)) return (byte)longValue;
        if (clrType == typeof(short)) return (short)longValue;
        if (clrType == typeof(ushort)) return (ushort)longValue;
        if (clrType == typeof(int)) return (int)longValue;
        if (clrType == typeof(uint)) return (uint)longValue;
        if (clrType == typeof(ulong)) return (ulong)longValue;

        return longValue; // Default to long
    }

    private static object ParseReal(string text, Type clrType)
    {
        // Parse as double first, then convert to specific type
        var doubleValue = double.Parse(NormaliseNumber(text), CultureInfo.InvariantCulture);

        if (clrType == typeof(Half)) return (Half)doubleValue;
        if (clrType == typeof(float)) return (float)doubleValue;

        return doubleValue; // Default to double
    }

    private static bool ParseBoolean(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower switch
        {
            "true" or "yes" or "1" or "on" => true,
            "false" or "no" or "0" or "off" => false,
            _ => bool.Parse(text)
        };
    }

    private static byte[] ParseBlob(string text)
    {
        // Support hex format: X'...' or 0x...
        if (text.StartsWith("X'", StringComparison.OrdinalIgnoreCase) && text.EndsWith("'"))
        {
            text = text[2..^1];
        }
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        // Parse hex string
        if (text.Length % 2 != 0)
            throw new FormatException("Hex string must have even length");

        var bytes = new byte[text.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static JsonDocument ParseJson(string text)
    {
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// Tries to parse a string value to the target type.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="targetType">The target CLR type.</param>
    /// <param name="result">The parsed value if successful.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string? text, Type targetType, out object? result)
    {
        try
        {
            result = Parse(text, targetType);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }
}
