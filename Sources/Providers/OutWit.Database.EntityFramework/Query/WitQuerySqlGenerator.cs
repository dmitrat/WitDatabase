using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Query;

/// <summary>
/// Generates SQL queries for WitDatabase from expression trees.
/// </summary>
public sealed class WitQuerySqlGenerator : QuerySqlGenerator
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitQuerySqlGenerator"/> class.
    /// </summary>
    /// <param name="dependencies">The query SQL generator dependencies.</param>
    public WitQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region Expression Visitors

    /// <inheritdoc/>
    protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        // WitDatabase uses || for string concatenation
        if (sqlBinaryExpression.OperatorType == ExpressionType.Add &&
            sqlBinaryExpression.Type == typeof(string))
        {
            Sql.Append("(");
            Visit(sqlBinaryExpression.Left);
            Sql.Append(" || ");
            Visit(sqlBinaryExpression.Right);
            Sql.Append(")");
            return sqlBinaryExpression;
        }

        // Handle modulo operator - WitDB uses MOD() function or % operator
        if (sqlBinaryExpression.OperatorType == ExpressionType.Modulo)
        {
            Sql.Append("(");
            Visit(sqlBinaryExpression.Left);
            Sql.Append(" % ");
            Visit(sqlBinaryExpression.Right);
            Sql.Append(")");
            return sqlBinaryExpression;
        }

        return base.VisitSqlBinary(sqlBinaryExpression);
    }

    /// <inheritdoc/>
    protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
    {
        // Handle NOT operator
        if (sqlUnaryExpression.OperatorType == ExpressionType.Not)
        {
            if (sqlUnaryExpression.Type == typeof(bool))
            {
                Sql.Append("NOT (");
                Visit(sqlUnaryExpression.Operand);
                Sql.Append(")");
                return sqlUnaryExpression;
            }
        }

        // Handle negation
        if (sqlUnaryExpression.OperatorType == ExpressionType.Negate)
        {
            Sql.Append("-(");
            Visit(sqlUnaryExpression.Operand);
            Sql.Append(")");
            return sqlUnaryExpression;
        }

        return base.VisitSqlUnary(sqlUnaryExpression);
    }

    /// <inheritdoc/>
    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        // WitDatabase uses LIMIT x OFFSET y syntax
        if (selectExpression.Limit != null)
        {
            Sql.AppendLine().Append("LIMIT ");
            Visit(selectExpression.Limit);
        }

        if (selectExpression.Offset != null)
        {
            // WitSQL accepts OFFSET on its own, so there is no need for SQLite's `LIMIT -1`
            // placeholder. That form is still parsed and honoured, but emitting the standard one
            // keeps generated SQL readable and portable.
            if (selectExpression.Limit == null)
                Sql.AppendLine().Append("OFFSET ");
            else
                Sql.Append(" OFFSET ");

            Visit(selectExpression.Offset);
        }
    }

    /// <inheritdoc/>
    protected override void GenerateTop(SelectExpression selectExpression)
    {
        // WitDatabase doesn't use TOP syntax, it uses LIMIT
        // This method intentionally left empty
    }

    /// <inheritdoc/>
    protected override Expression VisitOrdering(OrderingExpression orderingExpression)
    {
        Visit(orderingExpression.Expression);

        if (!orderingExpression.IsAscending)
        {
            Sql.Append(" DESC");
        }

        // Handle NULLS FIRST/LAST if needed
        return orderingExpression;
    }

    /// <inheritdoc/>
    protected override Expression VisitCase(CaseExpression caseExpression)
    {
        Sql.Append("CASE");

        if (caseExpression.Operand != null)
        {
            Sql.Append(" ");
            Visit(caseExpression.Operand);
        }

        foreach (var whenClause in caseExpression.WhenClauses)
        {
            Sql.Append(" WHEN ");
            Visit(whenClause.Test);
            Sql.Append(" THEN ");
            Visit(whenClause.Result);
        }

        if (caseExpression.ElseResult != null)
        {
            Sql.Append(" ELSE ");
            Visit(caseExpression.ElseResult);
        }

        Sql.Append(" END");

        return caseExpression;
    }

    /// <inheritdoc/>
    protected override Expression VisitCollate(CollateExpression collateExpression)
    {
        Visit(collateExpression.Operand);
        Sql.Append(" COLLATE ");
        Sql.Append(collateExpression.Collation);
        return collateExpression;
    }

    /// <summary>
    /// Refuses <c>CROSS APPLY</c> instead of emitting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inherited implementation writes the literal text <c>CROSS APPLY</c>, which WitSQL cannot
    /// parse. Measured, not assumed: a correlated <c>Take</c> produced
    /// <c>OUTER APPLY ( … ) AS "r1"</c> and the provider's own parser then rejected its own SQL. A
    /// query that builds a clean model and fails at execution is worse than one refused up front,
    /// because the failure surfaces far from its cause.
    /// </para>
    /// <para>
    /// Refusing rather than rewriting is the honest answer <b>because the engine has no lateral
    /// execution</b>: <c>APPLY</c> re-evaluates its right-hand side per left row, and no general
    /// rewrite into the joins this engine has preserves that. EF Core's SQLite provider refuses the
    /// identical query in the same words, which is useful confirmation that the shape of the answer
    /// is reasonable — but the reason here is our own missing capability, not an intent to match
    /// SQLite.
    /// </para>
    /// <para>
    /// <b>This is a stopgap, not a settled position.</b> PostgreSQL supports the same operation as
    /// <c>LATERAL</c> and SQL Server as <c>APPLY</c>, and WitDatabase aims to substitute for those.
    /// When lateral execution exists, this refusal should become a translation.
    /// </para>
    /// </remarks>
    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
        => throw new InvalidOperationException(ApplyNotSupported("CROSS APPLY"));

    /// <summary>
    /// Refuses <c>OUTER APPLY</c> instead of emitting it. See <see cref="VisitCrossApply"/>.
    /// </summary>
    protected override Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
        => throw new InvalidOperationException(ApplyNotSupported("OUTER APPLY"));

    private static string ApplyNotSupported(string operation) =>
        $"Translating this query requires the SQL {operation} operation, which WitDatabase does not " +
        "support yet: it needs a lateral join, where the right-hand side is re-evaluated for each " +
        "row of the left. This usually comes from a correlated Take/Skip, or from a filtered or " +
        "limited collection Include. Rewrite it as a join or a subquery, or materialise the outer " +
        "query first with AsEnumerable().";

    #endregion
}
