using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementSelect : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementSelect(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementSelect select)
                return false;

            return base.Is(select, tolerance)
                   && IsDistinct.Is(select.IsDistinct)
                   && IsRecursive.Is(select.IsRecursive)
                   && CteDefinitions.Is(select.CteDefinitions)
                   && SelectList.Is(select.SelectList)
                   && FromClause.Is(select.FromClause)
                   && WhereClause.Check(select.WhereClause)
                   && GroupByClause.Is(select.GroupByClause)
                   && HavingClause.Check(select.HavingClause)
                   && OrderByClause.Is(select.OrderByClause)
                   && ValuesRowsAre(select)
                   && LimitCount.Check(select.LimitCount)
                   && LimitOffset.Check(select.LimitOffset)
                   && SetOperations.Is(select.SetOperations)
                   && ForClause.Check(select.ForClause);
        }

        public override WitSqlStatementSelect Clone()
        {
            return new WitSqlStatementSelect
            {
                Line = Line,
                Column = Column,
                IsDistinct = IsDistinct,
                IsRecursive = IsRecursive,
                CteDefinitions = CteDefinitions?.Select(cte => cte.Clone()).ToList(),
                SelectList = SelectList.Select(item => item.Clone()).ToList(),
                FromClause = FromClause?.Select(source => (TableSource)source.Clone()).ToList(),
                WhereClause = (WitSqlExpression?)WhereClause?.Clone(),
                GroupByClause = GroupByClause?.Select(expression => (WitSqlExpression)expression.Clone()).ToList(),
                HavingClause = (WitSqlExpression?)HavingClause?.Clone(),
                OrderByClause = OrderByClause?.Select(item => item.Clone()).ToList(),
                ValuesRows = ValuesRows?.Select(row =>
                    (IReadOnlyList<WitSqlExpression>)row.Select(v => (WitSqlExpression)v.Clone()).ToList()).ToList(),
                LimitCount = (WitSqlExpression?)LimitCount?.Clone(),
                LimitOffset = (WitSqlExpression?)LimitOffset?.Clone(),
                SetOperations = SetOperations?.Select(op => op.Clone()).ToList(),
                ForClause = ForClause?.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// Whether this is a DISTINCT select.
        /// </summary>
        public bool IsDistinct { get; init; }

        /// <summary>
        /// Whether this is a recursive CTE (WITH RECURSIVE).
        /// </summary>
        public bool IsRecursive { get; init; }

        /// <summary>
        /// CTE definitions from the WITH clause.
        /// </summary>
        public IReadOnlyList<ClauseCteDefinition>? CteDefinitions { get; init; }

        /// <summary>
        /// The select list (columns/expressions).
        /// </summary>
        public required IReadOnlyList<ClauseSelectItem> SelectList { get; init; }

        /// <summary>
        /// Table sources from the FROM clause.
        /// </summary>
        public IReadOnlyList<TableSource>? FromClause { get; init; }

        /// <summary>
        /// The WHERE condition.
        /// </summary>
        public WitSqlExpression? WhereClause { get; init; }

        /// <summary>
        /// GROUP BY expressions.
        /// </summary>
        public IReadOnlyList<WitSqlExpression>? GroupByClause { get; init; }

        /// <summary>
        /// The HAVING condition.
        /// </summary>
        public WitSqlExpression? HavingClause { get; init; }

        /// <summary>
        /// ORDER BY items (set by queryExpression level).
        /// </summary>
        public IReadOnlyList<ClauseOrderByItem>? OrderByClause { get; set; }

        /// <summary>
        /// LIMIT count expression (set by queryExpression level).
        /// </summary>
        public WitSqlExpression? LimitCount { get; set; }

        /// <summary>
        /// LIMIT offset expression (set by queryExpression level).
        /// </summary>
        public WitSqlExpression? LimitOffset { get; set; }

        /// <summary>
        /// Set operations (UNION, INTERSECT, EXCEPT) with their right operands.
        /// </summary>
        public IReadOnlyList<ClauseSetOperation>? SetOperations { get; set; }

        /// <summary>
        /// FOR UPDATE/SHARE clause with locking options.
        /// </summary>
        public ClauseFor? ForClause { get; init; }

        #endregion
    
    /// <summary>
    /// The literal rows of a <c>VALUES</c> query, or null for an ordinary <c>SELECT</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VALUES (1), (2)</c> is a query: PostgreSQL and SQL Server both accept it wherever a query
    /// goes, and <c>SELECT * FROM (VALUES …) AS V</c> is the shape EF Core and hand-written SQL use
    /// it in.
    /// </para>
    /// <para>
    /// Carried on the select rather than given a statement type of its own, deliberately. A new
    /// statement type would need a MemoryPack union tag - a persisted format - and would force
    /// every place typed to <c>WitSqlStatementSelect</c> to learn a second shape, starting with
    /// <c>TableSourceSubquery.Subquery</c>. A VALUES query is a query that produces rows; this says
    /// exactly that and costs one appended member.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<WitSqlExpression>>? ValuesRows { get; init; }

    private bool ValuesRowsAre(WitSqlStatementSelect other)
    {
        if (ValuesRows is null || other.ValuesRows is null)
            return ValuesRows is null && other.ValuesRows is null;

        if (ValuesRows.Count != other.ValuesRows.Count)
            return false;

        for (var row = 0; row < ValuesRows.Count; row++)
        {
            if (ValuesRows[row].Count != other.ValuesRows[row].Count)
                return false;

            for (var column = 0; column < ValuesRows[row].Count; column++)
            {
                if (!ValuesRows[row][column].Is(other.ValuesRows[row][column]))
                    return false;
            }
        }

        return true;
    }
}
}