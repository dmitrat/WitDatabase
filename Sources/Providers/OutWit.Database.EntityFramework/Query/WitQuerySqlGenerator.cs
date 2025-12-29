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

        return base.VisitSqlBinary(sqlBinaryExpression);
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
            if (selectExpression.Limit == null)
            {
                // If only OFFSET, we need to use a very large LIMIT
                Sql.AppendLine().Append("LIMIT -1");
            }

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

    #endregion
}
