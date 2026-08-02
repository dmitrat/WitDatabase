using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Nodes;

namespace OutWit.Database.Expressions;

/// <summary>
/// The function names this engine can evaluate, for checking an expression before it is stored.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-01: a <c>CHECK</c>, a computed column and an index expression all accepted
/// <c>NoSuchFunc(V)</c> at declaration and failed at first use - or, for the computed column, did not
/// fail at all and answered NULL. Phase 7's rule across the DDL surface is accepted, enforced, or
/// refused, and a name the engine cannot evaluate belongs in the third case, where the caller is
/// still holding the statement that is wrong.
/// </para>
/// <para>
/// <b>What this set is, stated exactly.</b> It is a <b>superset</b> of the evaluator's function
/// names, taken from its routers mechanically rather than typed out: the scalar router, the aggregate
/// and window routers, and the sets they consult. Being a superset is the safe direction and is
/// deliberate - a name in here that the evaluator does not handle costs an error at first use, which
/// is what happened before this existed, while a name missing from here would refuse a schema that
/// works. It therefore also contains the type and date-part literals that share those switches
/// (<c>VARCHAR</c>, <c>YYYY</c>), which are not functions; they cost nothing but honesty about what
/// the list is, and that is what this paragraph is for.
/// </para>
/// <para>
/// <b>The net against the dangerous direction</b> is <c>KnownFunctionCorpusTests</c>: every function
/// token the generated lexer defines must be in here, so a function added to the grammar tomorrow
/// cannot start being refused in a <c>CHECK</c> without a test saying so. The list of tokens comes
/// from the lexer's own vocabulary, the same trick <c>KeywordAsIdentifierCorpusTests</c> uses, so
/// nobody has to remember to update it.
/// </para>
/// <para>
/// Phase 9d extends this at exactly one point: a user-defined function is known when the catalog has
/// it, so <see cref="IsKnown"/> gains a catalog lookup beside the built-in set.
/// </para>
/// </remarks>
internal static class ExpressionFunctions
{
    #region Constants

    private static readonly HashSet<string> KNOWN =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ABS", "ACOS", "ASIN", "ATAN", "ATAN2", "AVG",
            "BASE64", "BIGINT", "BINARY", "BLOB", "BOOL", "BOOLEAN",
            "BYTE", "CEIL", "CEILING", "CHANGES", "CHAR", "CHAR_LENGTH",
            "COALESCE", "CONCAT", "CONCAT_WS", "CONVERT", "COS", "COUNT",
            "CUME_DIST", "CURRENT_DATE", "CURRENT_DATE_LOCAL", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_TIME_LOCAL",
            "CURRVAL", "D", "DATABASE", "DATE", "DATEADD", "DATEDIFF",
            "DATEONLY", "DATETIME", "DAY", "DAYOFWEEK", "DAYOFYEAR", "DD",
            "DECIMAL", "DEGREES", "DENSE_RANK", "DOUBLE", "EXP", "FIRST_VALUE",
            "FLOAT", "FLOAT64", "FLOOR", "FORMAT", "GROUP_CONCAT", "GUID",
            "HEX", "HH", "HOUR", "IFNULL", "IIF", "INCREMENT",
            "INSTR", "INT", "INT16", "INT32", "INT64", "INT8",
            "INTEGER", "JSON_ARRAY", "JSON_ARRAY_LENGTH", "JSON_EXTRACT", "JSON_INSERT", "JSON_OBJECT",
            "JSON_QUERY", "JSON_REMOVE", "JSON_REPLACE", "JSON_SET", "JSON_TYPE", "JSON_VALID",
            "JSON_VALUE", "LAG", "LASTINCREMENT", "LAST_INSERT_ROWID", "LAST_VALUE", "LEAD",
            "LEFT", "LEN", "LENGTH", "LN", "LOCALDATE", "LOCALTIME",
            "LOCALTIMESTAMP", "LOG", "LOG10", "LOG2", "LOWER", "LPAD",
            "LTRIM", "M", "MAKEDATE", "MAKETIME", "MAX", "MI",
            "MILLISECOND", "MIN", "MINUTE", "MM", "MOD", "MONTH",
            "MS", "N", "NEWGUID", "NEWUUID", "NEXTVAL", "NOW",
            "NOW_LOCAL", "NTH_VALUE", "NTILE", "NULLIF", "NUMERIC", "NVARCHAR",
            "NVL", "OCTET_LENGTH", "PERCENT_RANK", "PI", "POSITION", "POWER",
            "QUARTER", "RADIANS", "RANDOM", "RANK", "REAL", "REPEAT",
            "REPLACE", "REVERSE", "RIGHT", "ROUND", "ROWID", "ROW_NUMBER",
            "RPAD", "RTRIM", "S", "SECOND", "SHORT", "SIGN",
            "SIN", "SMALLINT", "SPACE", "SQRT", "SS", "STR",
            "STRFTIME", "SUBSTR", "SUBSTRING", "SUM", "TAN", "TEXT",
            "TIME", "TIMEONLY", "TIMESTAMP", "TINYINT", "TOBOOL", "TOBOOLEAN", "TODATE",
            "TODATETIME", "TODECIMAL", "TODOUBLE", "TOGUID", "TOINT", "TOREAL",
            "TOSTRING", "TOTAL_DAYS", "TOTAL_HOURS", "TOTAL_MILLISECONDS", "TOTAL_MINUTES", "TOTAL_SECONDS",
            "TRIM", "TRUNC", "TRUNCATE", "TYPEOF", "UNBASE64", "UNHEX",
            "UPPER", "UUID", "VARBINARY", "VARCHAR", "VERSION", "WEEK",
            "WEEKOFYEAR", "WK", "WW", "YEAR", "YY", "YYYY"
        };

    #endregion

    #region Functions

    /// <summary>
    /// Whether the engine has an implementation for a function of this name.
    /// </summary>
    public static bool IsKnown(string functionName) => KNOWN.Contains(functionName);

    /// <summary>
    /// The first function name under <paramref name="node"/> the engine cannot evaluate, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes a node rather than an expression so a whole DDL statement can be asked at once. A
    /// <c>CREATE TABLE</c> carries stored expressions in four places - a column <c>CHECK</c>, a table
    /// <c>CHECK</c>, a computed column and a <c>DEFAULT</c> - and asking each of them by name is how
    /// the fifth one gets missed.
    /// </para>
    /// <para>
    /// The walk is <see cref="WitSqlNodes.SelfAndDescendants"/> and not a switch, for the reason the
    /// walker's own remarks give: every hand-written walk over this AST has covered a few of the
    /// nineteen expression types and answered "fine" for the rest. It stops at a nested statement,
    /// which keeps a subquery's own functions out of the answer.
    /// </para>
    /// </remarks>
    public static string? FirstUnknownFunction(WitSqlNode? node)
    {
        foreach (var descendant in WitSqlNodes.SelfAndDescendants(node))
        {
            if (descendant is WitSqlExpressionFunctionCall call && !IsKnown(call.FunctionName))
                return call.FunctionName;
        }

        return null;
    }

    #endregion
}
