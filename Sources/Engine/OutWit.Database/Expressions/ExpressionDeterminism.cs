using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Expressions;

/// <summary>
/// Decides whether an expression gives the same answer every time it is evaluated for a row.
/// </summary>
/// <remarks>
/// <para>
/// The question matters wherever a value is <b>computed once and stored</b>, because from then on
/// the stored value and the expression are two things that can disagree. An index key is the case
/// that exists today: it is written at insert time and read by every query afterwards, so an
/// expression whose answer moves leaves the index describing a value the row no longer has, and the
/// answer a query gets depends on whether the plan used the index.
/// </para>
/// <para>
/// Measured 2026-08-01: <c>CREATE INDEX IX ON T ((V + (SELECT N FROM Lookup WHERE Id = 1)))</c> was
/// accepted, and so was <c>((RANDOM()))</c>. Neither can be maintained.
/// </para>
/// <para>
/// It is also the rule phase 9d needs before a user-defined function may appear in an index
/// expression - a function's body is an expression, so "is this function deterministic" is this same
/// question asked of the body. See <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 5.
/// </para>
/// <para>
/// <b>The walk is <see cref="WitSqlNodes.SelfAndDescendants"/> rather than a switch.</b> Every
/// hand-written walk over this AST in this project has turned out to cover a few of the nineteen
/// expression types and answer "fine" for the rest - which is how an aggregate inside
/// <c>BETWEEN</c> stayed invisible for months. A reflective walk cannot leave a node type out, and
/// it keeps working when the grammar gains one.
/// </para>
/// </remarks>
internal static class ExpressionDeterminism
{
    #region Constants

    /// <summary>
    /// Built-in functions whose answer is not a function of the row.
    /// </summary>
    /// <remarks>
    /// Taken from the evaluator's own router rather than from memory: the clock and calendar
    /// functions, the generators, and the three that read session state. <c>NEXTVAL</c> is here
    /// because it does not merely change - it <i>advances a sequence</i>, so evaluating it twice is
    /// not even the same question.
    /// </remarks>
    private static readonly HashSet<string> NONDETERMINISTIC_FUNCTIONS =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "NOW", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
            "NEWGUID", "NEWUUID", "RANDOM",
            "CHANGES", "LAST_INSERT_ROWID", "ROWID",
            "NEXTVAL", "INCREMENT", "CURRVAL", "LASTINCREMENT"
        };

    #endregion

    #region Functions

    /// <summary>
    /// Why <paramref name="expression"/> cannot be stored, or null when it can.
    /// </summary>
    /// <remarks>
    /// A reason rather than a bool, so the refusal can say which part of the expression is the
    /// problem. A caller told only "not allowed" about a long <c>CASE</c> has to guess.
    /// </remarks>
    /// <param name="expression">The expression to judge.</param>
    /// <param name="isNonDeterministicFunction">
    /// Asked about a function call the built-in list does not cover - a user-defined one, whose
    /// answer was decided from its own body when it was declared and stored on its definition. A
    /// caller with no database in hand passes null and gets the built-ins only.
    /// </param>
    public static string? ReasonItIsNotDeterministic(
        WitSqlExpression? expression,
        Func<string, bool>? isNonDeterministicFunction = null)
    {
        foreach (var node in WitSqlNodes.SelfAndDescendants(expression))
        {
            if (node is WitSqlExpressionFunctionCall udf
                && isNonDeterministicFunction is not null
                && isNonDeterministicFunction(udf.FunctionName))
            {
                return $"{udf.FunctionName}() is a function declared as not deterministic";
            }

            // Every shape a subquery takes, not only the scalar one. The walk stops inside a nested
            // statement, but it yields the node that holds it, which is what is being refused.
            switch (node)
            {
                case WitSqlExpressionSubquery:
                    return "it reads another table through a subquery";

                case WitSqlExpressionExists:
                    return "it reads another table through EXISTS";

                case WitSqlExpressionIn { Subquery: not null }:
                    return "it reads another table through IN (SELECT ...)";

                case WitSqlExpressionQuantified:
                    return "it reads another table through a quantified comparison";

                case WitSqlExpressionFunctionCall call
                    when NONDETERMINISTIC_FUNCTIONS.Contains(call.FunctionName):
                    return $"{call.FunctionName.ToUpperInvariant()}() does not give the same answer twice";
            }
        }

        return null;
    }

    #endregion
}
