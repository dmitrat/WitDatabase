using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.MergeClauses;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// MERGE statement for upsert operations.
    /// </summary>
    [MemoryPackable]
    public partial class WitSqlStatementMerge : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementMerge(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementMerge merge)
                return false;

            return base.Is(merge, tolerance)
                   && TargetTable.Is(merge.TargetTable)
                   && TargetAlias.Is(merge.TargetAlias)
                   && SourceTable.Is(merge.SourceTable)
                   && SourceSelect.Check(merge.SourceSelect)
                   && SourceAlias.Is(merge.SourceAlias)
                   && OnCondition.Check(merge.OnCondition)
                   && WhenClauses.Is(merge.WhenClauses);
        }

        public override WitSqlStatementMerge Clone()
        {
            return new WitSqlStatementMerge
            {
                Line = Line,
                Column = Column,
                TargetTable = TargetTable,
                TargetAlias = TargetAlias,
                SourceTable = SourceTable,
                SourceSelect = SourceSelect?.Clone(),
                SourceAlias = SourceAlias,
                OnCondition = (WitSqlExpression)OnCondition.Clone(),
                WhenClauses = WhenClauses.Select(c => c.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// Target table name.
        /// </summary>
        [ToString]
        public required string TargetTable { get; init; }

        /// <summary>
        /// Optional alias for target table.
        /// </summary>
        public string? TargetAlias { get; init; }

        /// <summary>
        /// Source table name (if using a table).
        /// </summary>
        public string? SourceTable { get; init; }

        /// <summary>
        /// Source select statement (if using a subquery).
        /// </summary>
        public WitSqlStatementSelect? SourceSelect { get; init; }

        /// <summary>
        /// Optional alias for source.
        /// </summary>
        public string? SourceAlias { get; init; }

        /// <summary>
        /// ON condition for matching rows.
        /// </summary>
        public required WitSqlExpression OnCondition { get; init; }

        /// <summary>
        /// WHEN MATCHED / WHEN NOT MATCHED clauses.
        /// </summary>
        public required IReadOnlyList<ClauseMergeWhen> WhenClauses { get; init; }

        #endregion
    }
}
