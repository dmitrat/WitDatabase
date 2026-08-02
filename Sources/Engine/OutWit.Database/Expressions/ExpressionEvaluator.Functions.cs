using OutWit.Database.Parser.Expressions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Function evaluation: routing to specific function implementations.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Constants

    /// <summary>
    /// The scalar functions that must receive a NULL argument rather than short-circuit to NULL.
    /// </summary>
    /// <remarks>
    /// Two groups. First, the functions whose entire purpose is to inspect or replace a NULL.
    /// Second, the JSON constructors and inspectors: JSON has a null of its own, so
    /// <c>JSON_ARRAY(1, NULL, 'hello')</c> must build <c>[1,null,"hello"]</c> rather than collapse,
    /// and <c>JSON_TYPE(NULL)</c> must answer "null" rather than return SQL NULL.
    /// </remarks>
    private static readonly HashSet<string> NULL_TOLERANT_FUNCTIONS =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "COALESCE", "NULLIF", "IFNULL", "NVL", "TYPEOF",
            "JSON_VALID", "JSON_TYPE", "JSON_ARRAY", "JSON_OBJECT"
        };

    #endregion

    #region Function Router

    private WitSqlValue EvaluateFunction(WitSqlExpressionFunctionCall func, WitSqlRow row)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        // An aggregate already computed for the current group - see EvaluateAggregate. This is what
        // lets an aggregate appear anywhere in a HAVING expression rather than only beside a
        // comparison operator.
        if (m_aggregates is not null && m_aggregates.TryGetValue(func, out var aggregated))
            return aggregated;

        // Special functions
        if (func.IsStar && funcName == "COUNT")
        {
            // A caller error, and worded as one. It used to read "COUNT(*) should be handled by
            // aggregation iterator", which is an internal invariant and told the caller nothing.
            throw new InvalidOperationException(
                "COUNT(*) is only meaningful in an aggregate query - use it in the SELECT list or "
                + "HAVING clause of a query with GROUP BY, or of an aggregate query without one.");
        }

        // Evaluate arguments
        var args = func.Arguments?.Select(a => Evaluate(a, row)).ToArray() ?? [];

        // SQL scalar functions are strict: a NULL argument yields NULL. Without this, every
        // function below silently substituted a zero-value for the NULL - LENGTH(NULL) was 0,
        // UPPER(NULL) was '', YEAR(NULL) was 1, ROUND(NULL) was 0 - which is a wrong answer rather
        // than a missing one, and it propagates into comparisons and aggregates unnoticed.
        //
        // The exceptions are the functions whose whole purpose is to inspect or replace a NULL;
        // they have to see it. Zero-argument functions are unaffected, having nothing to check.
        if (args.Any(a => a.IsNull) && !NULL_TOLERANT_FUNCTIONS.Contains(funcName))
            return WitSqlValue.Null;

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
            // The local-time counterparts. Everything used to land on NOW(), so DateTime.Now came
            // back as UTC - the answer was wrong by exactly the machine's offset, silently.
            "LOCALTIMESTAMP" or "NOW_LOCAL" => WitSqlValue.FromDateTime(DateTime.Now),
            "LOCALDATE" or "CURRENT_DATE_LOCAL" => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)),
            "LOCALTIME" or "CURRENT_TIME_LOCAL" => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(DateTime.Now)),
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
            // MILLISECOND and TOTAL_SECONDS were emitted by the EF Core translators for
            // DateTime.Millisecond and TimeSpan.TotalSeconds and had no implementation here, so the
            // query reached the engine and died with "Function not supported".
            "MILLISECOND" or "MS" => WitSqlValue.FromInt(EvaluateMillisecond(args[0])),
            "TOTAL_SECONDS" => WitSqlValue.FromReal(args[0].AsTimeSpan().TotalSeconds),
            "TOTAL_MINUTES" => WitSqlValue.FromReal(args[0].AsTimeSpan().TotalMinutes),
            "TOTAL_HOURS" => WitSqlValue.FromReal(args[0].AsTimeSpan().TotalHours),
            "TOTAL_DAYS" => WitSqlValue.FromReal(args[0].AsTimeSpan().TotalDays),
            "TOTAL_MILLISECONDS" => WitSqlValue.FromReal(args[0].AsTimeSpan().TotalMilliseconds),
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
            // TOBOOLEAN is the spelling the grammar has always admitted, and the only one of the
            // TO... conversions that had no implementation - TOSTRING, TOINT, TODOUBLE, TODECIMAL,
            // TODATE, TODATETIME and TOGUID all work. Found by KnownFunctionCorpusTests on its first
            // green run, which is what that corpus is for: it asks the question of every function
            // token the lexer defines rather than of the ones somebody thought to try.
            "TOBOOL" or "TOBOOLEAN" or "BOOL" => WitSqlValue.FromBool(args[0].AsBool()),
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

            _ => EvaluateUserDefined(func, args)
        };
    }

    /// <summary>
    /// A user-defined function: substitute the arguments and evaluate the stored body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the whole of function invocation, and it is deliberately not an execution.</b> The
    /// body is one expression, so calling a function means evaluating that expression against a row
    /// built from its parameters - no re-entry into <c>StatementExecutor</c>, no statement, no
    /// transaction, and nothing added to the nesting count. Measured before the design was chosen: a
    /// parsed expression evaluates against a synthetic parameter row, and that row shadows the
    /// caller's outer row rather than leaking into it.
    /// </para>
    /// <para>
    /// The arguments arrive already evaluated - the router above evaluates them once, in the
    /// caller's scope, before dispatch. So a parameter used twice in a body costs one evaluation of
    /// the argument, not two, and an argument referring to a column resolves against the caller's
    /// row, which is the only row it could mean.
    /// </para>
    /// <para>
    /// Recursion cannot arrive here: a function that calls itself is refused when it is declared,
    /// and a function that does not exist yet cannot be called, so the call graph is acyclic by
    /// construction. That matters because this path is not counted by the statement nesting limit
    /// and a cycle would end the process rather than raise.
    /// </para>
    /// </remarks>
    private WitSqlValue EvaluateUserDefined(WitSqlExpressionFunctionCall func, WitSqlValue[] args)
    {
        var function = m_context.Database.GetFunction(func.FunctionName)
            ?? throw new NotSupportedException($"Function not supported: {func.FunctionName.ToUpperInvariant()}");

        var parameters = function.Parameters ?? [];

        if (parameters.Count != args.Length)
        {
            throw new InvalidOperationException(
                $"Function {function.Name} takes {parameters.Count} argument(s) but was given "
                + $"{args.Length}.");
        }

        // The parameter row IS the scope. Nothing of the caller's row is put into it, so a body can
        // only ever see what it was passed - which is what makes a function safe to reach from a
        // CHECK or an index expression, where the caller's row is mid-write.
        var names = new string[parameters.Count];

        for (var i = 0; i < parameters.Count; i++)
            names[i] = parameters[i].Name;

        var value = Evaluate(function.Body, new WitSqlRow(args, names));

        // Coerced to the declared return type, because otherwise RETURNS is decorative. Measured in
        // the pre-release audit: a function declared RETURNS INT whose body returned 'not a number'
        // handed the text straight through, and INFORMATION_SCHEMA.ROUTINES reported the column type
        // as INTEGER while the function produced text. That is the accepted-but-not-enforced class
        // phase 7 spent itself closing, and a declared type nothing checks is worse than no declared
        // type at all - a consumer reading the catalog would build against it.
        //
        // The same converter a column write uses, so a function and a column agree about what a
        // declared type means, including when the value cannot be converted at all.
        return value.IsNull ? value : WitTypeConverter.Convert(value, function.ReturnType);
    }

    #endregion
}
