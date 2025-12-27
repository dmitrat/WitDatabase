using OutWit.Database.Parser.Expressions;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Function evaluation: routing to specific function implementations.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Function Router

    private WitSqlValue EvaluateFunction(WitSqlExpressionFunctionCall func, WitSqlRow row)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        // Special functions
        if (func.IsStar && funcName == "COUNT")
        {
            // COUNT(*) is handled by aggregation iterator
            throw new InvalidOperationException("COUNT(*) should be handled by aggregation iterator");
        }

        // Evaluate arguments
        var args = func.Arguments?.Select(a => Evaluate(a, row)).ToArray() ?? [];

        return funcName switch
        {
            // Numeric Functions
            "ABS" => EvaluateAbs(args),
            "ROUND" => EvaluateRound(args),
            "FLOOR" => WitSqlValue.FromReal(Math.Floor(args[0].AsDouble())),
            "CEIL" or "CEILING" => WitSqlValue.FromReal(Math.Ceiling(args[0].AsDouble())),
            "TRUNC" or "TRUNCATE" => WitSqlValue.FromReal(Math.Truncate(args[0].AsDouble())),
            "SQRT" => WitSqlValue.FromReal(Math.Sqrt(args[0].AsDouble())),
            "POWER" => WitSqlValue.FromReal(Math.Pow(args[0].AsDouble(), args[1].AsDouble())),
            "SIGN" => WitSqlValue.FromInt(Math.Sign(args[0].AsDouble())),
            "EXP" => WitSqlValue.FromReal(Math.Exp(args[0].AsDouble())),
            "LOG" or "LN" => WitSqlValue.FromReal(Math.Log(args[0].AsDouble())),
            "LOG10" => WitSqlValue.FromReal(Math.Log10(args[0].AsDouble())),
            "LOG2" => WitSqlValue.FromReal(Math.Log2(args[0].AsDouble())),
            "MOD" => WitSqlValue.FromInt(args[0].AsInt64() % args[1].AsInt64()),
            "PI" => WitSqlValue.FromReal(Math.PI),
            "DEGREES" => WitSqlValue.FromReal(args[0].AsDouble() * (180.0 / Math.PI)),
            "RADIANS" => WitSqlValue.FromReal(args[0].AsDouble() * (Math.PI / 180.0)),
            
            // Trigonometric Functions
            "SIN" => WitSqlValue.FromReal(Math.Sin(args[0].AsDouble())),
            "COS" => WitSqlValue.FromReal(Math.Cos(args[0].AsDouble())),
            "TAN" => WitSqlValue.FromReal(Math.Tan(args[0].AsDouble())),
            "ASIN" => WitSqlValue.FromReal(Math.Asin(args[0].AsDouble())),
            "ACOS" => WitSqlValue.FromReal(Math.Acos(args[0].AsDouble())),
            "ATAN" => WitSqlValue.FromReal(Math.Atan(args[0].AsDouble())),
            "ATAN2" => WitSqlValue.FromReal(Math.Atan2(args[0].AsDouble(), args[1].AsDouble())),

            // String Functions
            "LENGTH" or "LEN" or "CHAR_LENGTH" => WitSqlValue.FromInt(args[0].AsString().Length),
            "OCTET_LENGTH" => WitSqlValue.FromInt(System.Text.Encoding.UTF8.GetByteCount(args[0].AsString())),
            "UPPER" => WitSqlValue.FromText(args[0].AsString().ToUpperInvariant()),
            "LOWER" => WitSqlValue.FromText(args[0].AsString().ToLowerInvariant()),
            "TRIM" => WitSqlValue.FromText(args[0].AsString().Trim()),
            "LTRIM" => WitSqlValue.FromText(args[0].AsString().TrimStart()),
            "RTRIM" => WitSqlValue.FromText(args[0].AsString().TrimEnd()),
            "SUBSTR" or "SUBSTRING" => EvaluateSubstring(args),
            "REPLACE" => WitSqlValue.FromText(args[0].AsString().Replace(args[1].AsString(), args[2].AsString())),
            "INSTR" => WitSqlValue.FromInt(args[0].AsString().IndexOf(args[1].AsString(), StringComparison.Ordinal) + 1),
            "POSITION" => WitSqlValue.FromInt(args[1].AsString().IndexOf(args[0].AsString(), StringComparison.Ordinal) + 1),
            "REVERSE" => WitSqlValue.FromText(new string(args[0].AsString().Reverse().ToArray())),
            "CONCAT" => WitSqlValue.FromText(string.Concat(args.Select(a => a.AsString()))),
            "CONCAT_WS" => WitSqlValue.FromText(string.Join(args[0].AsString(), args.Skip(1).Select(a => a.AsString()))),
            "REPEAT" => WitSqlValue.FromText(string.Concat(Enumerable.Repeat(args[0].AsString(), (int)args[1].AsInt64()))),
            "SPACE" => WitSqlValue.FromText(new string(' ', (int)args[0].AsInt64())),
            "LPAD" => EvaluateLPad(args),
            "RPAD" => EvaluateRPad(args),
            "LEFT" => EvaluateLeft(args),
            "RIGHT" => EvaluateRight(args),

            // Date/Time Functions
            "NOW" or "CURRENT_TIMESTAMP" => WitSqlValue.FromDateTime(DateTime.UtcNow),
            "CURRENT_DATE" => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(DateTime.UtcNow)),
            "CURRENT_TIME" => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(DateTime.UtcNow)),
            "DATE" => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(args[0].AsDateTime())),
            "TIME" => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(args[0].AsDateTime())),
            "YEAR" => WitSqlValue.FromInt(args[0].AsDateTime().Year),
            "MONTH" => WitSqlValue.FromInt(args[0].AsDateTime().Month),
            "DAY" => WitSqlValue.FromInt(args[0].AsDateTime().Day),
            "HOUR" => WitSqlValue.FromInt(args[0].AsDateTime().Hour),
            "MINUTE" => WitSqlValue.FromInt(args[0].AsDateTime().Minute),
            "SECOND" => WitSqlValue.FromInt(args[0].AsDateTime().Second),
            "DATEADD" => EvaluateDateAdd(args),
            "DATEDIFF" => EvaluateDateDiff(args),
            "STRFTIME" => EvaluateStrftime(args),
            "DAYOFWEEK" => WitSqlValue.FromInt((int)args[0].AsDateTime().DayOfWeek),
            "DAYOFYEAR" => WitSqlValue.FromInt(args[0].AsDateTime().DayOfYear),
            "WEEKOFYEAR" or "WEEK" => WitSqlValue.FromInt(System.Globalization.ISOWeek.GetWeekOfYear(args[0].AsDateTime())),
            "QUARTER" => WitSqlValue.FromInt((args[0].AsDateTime().Month - 1) / 3 + 1),
            "MAKEDATE" => WitSqlValue.FromDateOnly(new DateOnly((int)args[0].AsInt64(), 1, 1).AddDays((int)args[1].AsInt64() - 1)),
            "MAKETIME" => WitSqlValue.FromTimeOnly(new TimeOnly((int)args[0].AsInt64(), (int)args[1].AsInt64(), (int)args[2].AsInt64())),

            // Null Handling Functions
            "COALESCE" => EvaluateCoalesce(args),
            "NULLIF" => args[0] == args[1] ? WitSqlValue.Null : args[0],
            "IFNULL" or "NVL" => args[0].IsNull ? args[1] : args[0],

            // ID Generation
            "NEWGUID" or "NEWUUID" => WitSqlValue.FromGuid(Guid.NewGuid()),
            "RANDOM" => EvaluateRandom(args),

            // Type Conversion & Encoding
            "TYPEOF" => WitSqlValue.FromText(args[0].Type.ToString()),
            "HEX" => WitSqlValue.FromText(Convert.ToHexString(args[0].AsBlob())),
            "UNHEX" => WitSqlValue.FromBlob(Convert.FromHexString(args[0].AsString())),
            "BASE64" => WitSqlValue.FromText(Convert.ToBase64String(args[0].AsBlob())),
            "UNBASE64" => WitSqlValue.FromBlob(Convert.FromBase64String(args[0].AsString())),
            "FORMAT" => EvaluateFormat(args),
            "CONVERT" => EvaluateConvert(args),

            // Explicit Type Conversions
            "TOSTRING" or "STR" => WitSqlValue.FromText(args[0].AsString()),
            "TOINT" or "INT" => WitSqlValue.FromInt(args[0].AsInt64()),
            "TOREAL" or "REAL" or "TODOUBLE" => WitSqlValue.FromReal(args[0].AsDouble()),
            "TOBOOL" or "BOOL" => WitSqlValue.FromBool(args[0].AsBool()),
            "TODECIMAL" => WitSqlValue.FromDecimal(args[0].AsDecimal()),
            "TODATETIME" => WitSqlValue.FromDateTime(args[0].AsDateTime()),
            "TOGUID" => WitSqlValue.FromGuid(args[0].AsGuid()),

            // System Functions
            "DATABASE" => WitSqlValue.FromText("WitDB"),
            "VERSION" => WitSqlValue.FromText("1.0.0"),

            // Metadata Functions
            "CHANGES" => WitSqlValue.FromInt(m_context.LastChangesCount),
            "LAST_INSERT_ROWID" => WitSqlValue.FromInt(m_context.LastInsertRowId),

            // Sequence Functions
            "NEXTVAL" or "INCREMENT" => WitSqlValue.FromInt(m_context.Database.NextVal(args[0].AsString())),
            "CURRVAL" or "LASTINCREMENT" => WitSqlValue.FromInt(m_context.Database.CurrVal(args[0].AsString())),
            
            // JSON Functions
            "JSON_EXTRACT" => EvaluateJsonExtract(args),
            "JSON_VALUE" => EvaluateJsonValue(args),
            "JSON_QUERY" => EvaluateJsonQuery(args),
            "JSON_TYPE" => EvaluateJsonType(args),
            "JSON_ARRAY_LENGTH" => EvaluateJsonArrayLength(args),
            "JSON_VALID" => EvaluateJsonValid(args),
            "JSON_SET" => EvaluateJsonSet(args),
            "JSON_INSERT" => EvaluateJsonInsert(args),
            "JSON_REPLACE" => EvaluateJsonReplace(args),
            "JSON_REMOVE" => EvaluateJsonRemove(args),
            "JSON_ARRAY" => EvaluateJsonArray(args),
            "JSON_OBJECT" => EvaluateJsonObject(args),

            _ => throw new NotSupportedException($"Function not supported: {funcName}")
        };
    }

    #endregion
}
