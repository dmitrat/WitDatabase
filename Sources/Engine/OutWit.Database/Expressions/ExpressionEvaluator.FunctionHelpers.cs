using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Function helper implementations: numeric, string, date functions.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Constants

    private static readonly Random RANDOM = new();

    #endregion

    #region Numeric Functions

    private static WitSqlValue EvaluateAbs(WitSqlValue[] args)
    {
        var value = args[0];
        if (value.IsNull) return WitSqlValue.Null;

        return value.Type switch
        {
            WitSqlType.Integer => WitSqlValue.FromInt(Math.Abs(value.AsInt64())),
            WitSqlType.Real => WitSqlValue.FromReal(Math.Abs(value.AsDouble())),
            WitSqlType.Decimal => WitSqlValue.FromDecimal(Math.Abs(value.AsDecimal())),
            _ => WitSqlValue.FromReal(Math.Abs(value.AsDouble()))
        };
    }

    private static WitSqlValue EvaluateRound(WitSqlValue[] args)
    {
        var value = args[0].AsDouble();
        var decimals = args.Length > 1 ? (int)args[1].AsInt64() : 0;
        return WitSqlValue.FromReal(Math.Round(value, decimals));
    }

    private static WitSqlValue EvaluateRandom(WitSqlValue[] args)
    {
        if (args.Length == 0)
            return WitSqlValue.FromReal(RANDOM.NextDouble());
        if (args.Length == 2)
        {
            var min = (int)args[0].AsInt64();
            var max = (int)args[1].AsInt64();
            return WitSqlValue.FromInt(RANDOM.Next(min, max + 1));
        }
        return WitSqlValue.FromReal(RANDOM.NextDouble());
    }

    #endregion

    #region String Functions

    private static WitSqlValue EvaluateSubstring(WitSqlValue[] args)
    {
        var str = args[0].AsString();
        var start = (int)args[1].AsInt64() - 1; // SQL is 1-based
        if (start < 0) start = 0;
        if (start >= str.Length) return WitSqlValue.FromText("");

        if (args.Length > 2)
        {
            var len = (int)args[2].AsInt64();
            if (start + len > str.Length) len = str.Length - start;
            return WitSqlValue.FromText(str.Substring(start, len));
        }
        return WitSqlValue.FromText(str.Substring(start));
    }

    private static WitSqlValue EvaluateLeft(WitSqlValue[] args)
    {
        var str = args[0].AsString();
        var len = (int)args[1].AsInt64();
        if (len >= str.Length) return args[0];
        if (len <= 0) return WitSqlValue.FromText("");
        return WitSqlValue.FromText(str.Substring(0, len));
    }

    private static WitSqlValue EvaluateRight(WitSqlValue[] args)
    {
        var str = args[0].AsString();
        var len = (int)args[1].AsInt64();
        if (len >= str.Length) return args[0];
        if (len <= 0) return WitSqlValue.FromText("");
        return WitSqlValue.FromText(str.Substring(str.Length - len));
    }

    private static WitSqlValue EvaluateLPad(WitSqlValue[] args)
    {
        var str = args[0].AsString();
        var len = (int)args[1].AsInt64();
        var pad = args.Length > 2 ? args[2].AsString() : " ";
        if (str.Length >= len) return WitSqlValue.FromText(str.Substring(0, len));
        if (string.IsNullOrEmpty(pad)) return args[0];

        var result = str.PadLeft(len, pad[0]);
        return WitSqlValue.FromText(result.Length > len ? result.Substring(0, len) : result);
    }

    private static WitSqlValue EvaluateRPad(WitSqlValue[] args)
    {
        var str = args[0].AsString();
        var len = (int)args[1].AsInt64();
        var pad = args.Length > 2 ? args[2].AsString() : " ";
        if (str.Length >= len) return WitSqlValue.FromText(str.Substring(0, len));
        if (string.IsNullOrEmpty(pad)) return args[0];

        var result = str.PadRight(len, pad[0]);
        return WitSqlValue.FromText(result.Length > len ? result.Substring(0, len) : result);
    }

    #endregion

    #region Date Functions

    private static WitSqlValue EvaluateDateAdd(WitSqlValue[] args)
    {
        // DATEADD(interval, number, date)
        var interval = args[0].AsString().ToUpperInvariant();
        var number = (int)args[1].AsInt64();
        var date = args[2].AsDateTime();

        return WitSqlValue.FromDateTime(interval switch
        {
            "YEAR" or "YY" or "YYYY" => date.AddYears(number),
            "MONTH" or "MM" or "M" => date.AddMonths(number),
            "DAY" or "DD" or "D" => date.AddDays(number),
            "HOUR" or "HH" => date.AddHours(number),
            "MINUTE" or "MI" or "N" => date.AddMinutes(number),
            "SECOND" or "SS" or "S" => date.AddSeconds(number),
            "MILLISECOND" or "MS" => date.AddMilliseconds(number),
            "WEEK" or "WK" or "WW" => date.AddDays(number * 7),
            _ => throw new ArgumentException($"Unknown interval: {interval}")
        });
    }

    private static WitSqlValue EvaluateDateDiff(WitSqlValue[] args)
    {
        // DATEDIFF(interval, date1, date2)
        var interval = args[0].AsString().ToUpperInvariant();
        var date1 = args[1].AsDateTime();
        var date2 = args[2].AsDateTime();
        var diff = date2 - date1;

        return WitSqlValue.FromInt(interval switch
        {
            "YEAR" or "YY" or "YYYY" => date2.Year - date1.Year,
            "MONTH" or "MM" or "M" => (date2.Year - date1.Year) * 12 + (date2.Month - date1.Month),
            "DAY" or "DD" or "D" => (long)diff.TotalDays,
            "HOUR" or "HH" => (long)diff.TotalHours,
            "MINUTE" or "MI" or "N" => (long)diff.TotalMinutes,
            "SECOND" or "SS" or "S" => (long)diff.TotalSeconds,
            "MILLISECOND" or "MS" => (long)diff.TotalMilliseconds,
            "WEEK" or "WK" or "WW" => (long)(diff.TotalDays / 7),
            _ => throw new ArgumentException($"Unknown interval: {interval}")
        });
    }

    private static WitSqlValue EvaluateStrftime(WitSqlValue[] args)
    {
        // STRFTIME(format, datetime) - SQLite style format
        var format = args[0].AsString();
        var date = args[1].AsDateTime();

        // Convert SQLite format specifiers to .NET
        var netFormat = format
            .Replace("%Y", "yyyy")
            .Replace("%m", "MM")
            .Replace("%d", "dd")
            .Replace("%H", "HH")
            .Replace("%M", "mm")
            .Replace("%S", "ss")
            .Replace("%f", "ffffff")
            .Replace("%j", "ddd") // Day of year (approximation)
            .Replace("%W", "ww")  // Week number (approximation)
            .Replace("%w", "d")   // Day of week
            .Replace("%%", "%");

        return WitSqlValue.FromText(date.ToString(netFormat, System.Globalization.CultureInfo.InvariantCulture));
    }

    #endregion

    #region Null Functions

    private static WitSqlValue EvaluateCoalesce(WitSqlValue[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.IsNull)
                return arg;
        }
        return WitSqlValue.Null;
    }

    #endregion

    #region Conversion Functions

    private static WitSqlValue EvaluateFormat(WitSqlValue[] args)
    {
        // FORMAT(value, format_string)
        var value = args[0];
        var format = args[1].AsString();

        return WitSqlValue.FromText(value.Type switch
        {
            WitSqlType.Integer => value.AsInt64().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
            WitSqlType.Real => value.AsDouble().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
            WitSqlType.Decimal => value.AsDecimal().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
            WitSqlType.DateTime => value.AsDateTime().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.AsString()
        });
    }

    private static WitSqlValue EvaluateConvert(WitSqlValue[] args)
    {
        // CONVERT(value, type_name)
        var value = args[0];
        var typeName = args[1].AsString().ToUpperInvariant();

        return typeName switch
        {
            "INT" or "INTEGER" => WitSqlValue.FromInt(value.AsInt64()),
            "REAL" or "FLOAT" or "DOUBLE" => WitSqlValue.FromReal(value.AsDouble()),
            "TEXT" or "VARCHAR" or "STRING" => WitSqlValue.FromText(value.AsString()),
            "DECIMAL" => WitSqlValue.FromDecimal(value.AsDecimal()),
            "DATETIME" => WitSqlValue.FromDateTime(value.AsDateTime()),
            "DATE" => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(value.AsDateTime())),
            "TIME" => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(value.AsDateTime())),
            "BOOL" or "BOOLEAN" => WitSqlValue.FromBool(value.AsBool()),
            "BLOB" => WitSqlValue.FromBlob(value.AsBlob()),
            "GUID" or "UUID" => WitSqlValue.FromGuid(value.AsGuid()),
            _ => value
        };
    }

    #endregion
}
