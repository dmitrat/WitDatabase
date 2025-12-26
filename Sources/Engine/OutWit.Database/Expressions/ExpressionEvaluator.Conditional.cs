using OutWit.Database.Parser.Expressions;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// Conditional expression evaluation: CASE, IIF, IS NULL, BETWEEN, IN, LIKE, GLOB, Collate.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region Case

    private WitSqlValue EvaluateCase(WitSqlExpressionCase caseExpr, WitSqlRow row)
    {
        if (caseExpr.Operand != null)
        {
            // Simple CASE: CASE expr WHEN value THEN result
            var operand = Evaluate(caseExpr.Operand, row);
            foreach (var when in caseExpr.WhenClauses)
            {
                var whenValue = Evaluate(when.When, row);
                if (operand == whenValue)
                    return Evaluate(when.Then, row);
            }
        }
        else
        {
            // Searched CASE: CASE WHEN condition THEN result
            foreach (var when in caseExpr.WhenClauses)
            {
                var condition = Evaluate(when.When, row);
                if (!condition.IsNull && condition.AsBool())
                    return Evaluate(when.Then, row);
            }
        }

        return caseExpr.ElseResult != null
            ? Evaluate(caseExpr.ElseResult, row)
            : WitSqlValue.Null;
    }

    #endregion

    #region IIF

    private WitSqlValue EvaluateIif(WitSqlExpressionIif iif, WitSqlRow row)
    {
        var condition = Evaluate(iif.Condition, row);

        // If condition is NULL or false, return false value
        if (condition.IsNull || !condition.AsBool())
            return Evaluate(iif.FalseValue, row);

        return Evaluate(iif.TrueValue, row);
    }

    #endregion

    #region IS NULL

    private WitSqlValue EvaluateIsNull(WitSqlExpressionIsNull isNull, WitSqlRow row)
    {
        var value = Evaluate(isNull.Expression, row);
        var result = value.IsNull;
        return WitSqlValue.FromBool(isNull.IsNot ? !result : result);
    }

    #endregion

    #region BETWEEN

    private WitSqlValue EvaluateBetween(WitSqlExpressionBetween between, WitSqlRow row)
    {
        var value = Evaluate(between.Expression, row);
        var low = Evaluate(between.Low, row);
        var high = Evaluate(between.High, row);

        if (value.IsNull || low.IsNull || high.IsNull)
            return WitSqlValue.Null;

        var inRange = value >= low && value <= high;
        return WitSqlValue.FromBool(between.IsNot ? !inRange : inRange);
    }

    #endregion

    #region IN

    private WitSqlValue EvaluateIn(WitSqlExpressionIn inExpr, WitSqlRow row)
    {
        // IN with subquery - delegate to subquery evaluator
        if (inExpr.Subquery != null)
        {
            return EvaluateInSubquery(inExpr, row);
        }

        var value = Evaluate(inExpr.Expression, row);

        if (value.IsNull)
            return WitSqlValue.Null;

        // IN with value list
        if (inExpr.Values != null)
        {
            bool hasNull = false;
            foreach (var item in inExpr.Values)
            {
                var itemValue = Evaluate(item, row);
                if (itemValue.IsNull)
                {
                    hasNull = true;
                    continue;
                }
                if (value == itemValue)
                    return WitSqlValue.FromBool(!inExpr.IsNot);
            }
            // If we didn't find a match but had nulls, result is NULL
            if (hasNull)
                return WitSqlValue.Null;
            return WitSqlValue.FromBool(inExpr.IsNot);
        }

        return WitSqlValue.FromBool(inExpr.IsNot);
    }

    #endregion

    #region LIKE

    private WitSqlValue EvaluateLike(WitSqlExpressionLike like, WitSqlRow row)
    {
        var value = Evaluate(like.Expression, row);
        var pattern = Evaluate(like.Pattern, row);

        if (value.IsNull || pattern.IsNull)
            return WitSqlValue.Null;

        var str = value.AsString();
        var patternStr = pattern.AsString();

        // Handle ESCAPE character
        char? escapeChar = null;
        if (like.Escape != null)
        {
            var escapeValue = Evaluate(like.Escape, row);
            if (!escapeValue.IsNull)
            {
                var escapeStr = escapeValue.AsString();
                if (escapeStr.Length > 0)
                    escapeChar = escapeStr[0];
            }
        }

        // Convert SQL LIKE pattern to regex, respecting escape character
        var regex = LikePatternToRegex(patternStr, escapeChar);

        var matches = System.Text.RegularExpressions.Regex.IsMatch(
            str, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return WitSqlValue.FromBool(like.IsNot ? !matches : matches);
    }

    private static string LikePatternToRegex(string pattern, char? escapeChar)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            // Handle escape character
            if (escapeChar.HasValue && c == escapeChar.Value && i + 1 < pattern.Length)
            {
                // Next character is escaped - treat literally
                i++;
                sb.Append(System.Text.RegularExpressions.Regex.Escape(pattern[i].ToString()));
                continue;
            }

            switch (c)
            {
                case '%':
                    sb.Append(".*");
                    break;
                case '_':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    #endregion

    #region GLOB

    private WitSqlValue EvaluateGlob(WitSqlExpressionGlob glob, WitSqlRow row)
    {
        var value = Evaluate(glob.Expression, row);
        var pattern = Evaluate(glob.Pattern, row);

        if (value.IsNull || pattern.IsNull)
            return WitSqlValue.Null;

        var text = value.AsString();
        var globPattern = pattern.AsString();

        // Convert GLOB pattern to regex
        // GLOB uses: * for any chars, ? for single char, [...] for character classes
        var regex = GlobToRegex(globPattern);
        var match = System.Text.RegularExpressions.Regex.IsMatch(text, regex);

        return WitSqlValue.FromBool(glob.IsNot ? !match : match);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        bool inCharClass = false;
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];

            if (inCharClass)
            {
                if (c == ']')
                {
                    sb.Append(c);
                    inCharClass = false;
                }
                else if (c == '!' && i > 0 && glob[i - 1] == '[')
                {
                    sb.Append('^'); // [! -> [^
                }
                else
                {
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                }
            }
            else
            {
                switch (c)
                {
                    case '*':
                        sb.Append(".*");
                        break;
                    case '?':
                        sb.Append('.');
                        break;
                    case '[':
                        sb.Append('[');
                        inCharClass = true;
                        break;
                    default:
                        sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                        break;
                }
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    #endregion

    #region Collate

    private WitSqlValue EvaluateCollate(WitSqlExpressionCollate collate, WitSqlRow row)
    {
        // Collation doesn't change the value, only affects comparison behavior
        // For now, just evaluate the operand - proper collation handling would need
        // to be implemented in comparison operators
        if (collate.Operand == null)
            return WitSqlValue.Null;

        return Evaluate(collate.Operand, row);
    }

    #endregion

    #region CAST

    private WitSqlValue EvaluateCast(WitSqlExpressionCast cast, WitSqlRow row)
    {
        var value = Evaluate(cast.Expression, row);
        if (value.IsNull) return WitSqlValue.Null;

        var targetType = cast.TargetType.TypeName.ToUpperInvariant();
        return targetType switch
        {
            "INT" or "INT32" or "INTEGER" or "BIGINT" or "INT64" => WitSqlValue.FromInt(value.AsInt64()),
            "REAL" or "FLOAT" or "DOUBLE" or "FLOAT64" => WitSqlValue.FromReal(value.AsDouble()),
            "TEXT" or "VARCHAR" or "CHAR" or "NVARCHAR" => WitSqlValue.FromText(value.AsString()),
            "BOOLEAN" or "BOOL" => WitSqlValue.FromBool(value.AsBool()),
            "DECIMAL" or "NUMERIC" => WitSqlValue.FromDecimal(value.AsDecimal()),
            "DATETIME" or "TIMESTAMP" => WitSqlValue.FromDateTime(value.AsDateTime()),
            "DATE" or "DATEONLY" => WitSqlValue.FromDateOnly(DateOnly.FromDateTime(value.AsDateTime())),
            "TIME" or "TIMEONLY" => WitSqlValue.FromTimeOnly(TimeOnly.FromDateTime(value.AsDateTime())),
            "GUID" or "UUID" => WitSqlValue.FromGuid(value.AsGuid()),
            "BLOB" or "BINARY" or "VARBINARY" => WitSqlValue.FromBlob(value.AsBlob()),
            _ => throw new NotSupportedException($"CAST to {targetType} not supported")
        };
    }

    #endregion
}
