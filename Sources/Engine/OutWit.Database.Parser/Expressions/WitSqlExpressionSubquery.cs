using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Expressions;

[MemoryPackable]
public partial class WitSqlExpressionSubquery : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionSubquery(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionSubquery subquery)
            return false;

        return base.Is(subquery, tolerance) 
               && Query.Is(subquery.Query, tolerance);
    }

    public override WitSqlExpressionSubquery Clone()
    {
        return new WitSqlExpressionSubquery
        {
            Line = Line,
            Column = Column,
            Query = Query.Clone()
        };
    }

    #endregion

    #region Properties

    public required WitSqlStatementSelect Query { get; init; }

    #endregion
}