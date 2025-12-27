using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// EXPLAIN statement for displaying query execution plan.
/// </summary>
/// <remarks>
/// Syntax: 
///   EXPLAIN select_statement
///   EXPLAIN QUERY PLAN select_statement
/// </remarks>
public class WitSqlStatementExplain : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementExplain(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementExplain explain)
            return false;

        return base.Is(explain, tolerance)
               && QueryPlan == explain.QueryPlan
               && Statement.Check(explain.Statement);
    }

    public override WitSqlStatementExplain Clone()
    {
        return new WitSqlStatementExplain
        {
            Line = Line,
            Column = Column,
            QueryPlan = QueryPlan,
            Statement = (WitSqlStatementSelect)Statement.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Whether QUERY PLAN modifier was specified.
    /// If true, shows the high-level query plan.
    /// If false, shows bytecode-level execution plan.
    /// </summary>
    [ToString]
    public bool QueryPlan { get; init; }

    /// <summary>
    /// The SELECT statement to explain.
    /// </summary>
    public required WitSqlStatementSelect Statement { get; init; }

    #endregion
}
