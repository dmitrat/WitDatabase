using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Common.Collections;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    public partial class WitSqlStatementUpdate : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementUpdate(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementUpdate update)
                return false;

            return base.Is(update, tolerance) 
                   && TableName.Is(update.TableName)
                   && TableAlias.Is(update.TableAlias)
                   && SetClauses.Is(update.SetClauses)
                   && FromClause.Is(update.FromClause)
                   && WhereClause.Check(update.WhereClause)
                   && ReturningClause.Is(update.ReturningClause);
        }

        public override WitSqlStatementUpdate Clone()
        {
            return new WitSqlStatementUpdate
            {
                Line = Line,
                Column = Column,
                TableName = TableName,
                TableAlias = TableAlias,
                SetClauses = SetClauses.Select(set => set.Clone()).ToList(),
                FromClause = FromClause?.Select(x => (TableSource)x.Clone()).ToList(),
                WhereClause = (WitSqlExpression?)WhereClause?.Clone(),
                ReturningClause = ReturningClause?.Select(x => x.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TableName { get; init; }

        /// <summary>
        /// Optional alias for the target table.
        /// </summary>
        public string? TableAlias { get; init; }

        public required IReadOnlyList<ClauseSet> SetClauses { get; init; }

        /// <summary>
        /// Optional FROM clause for join-based updates.
        /// </summary>
        public IReadOnlyList<TableSource>? FromClause { get; init; }

        public WitSqlExpression? WhereClause { get; init; }

        /// <summary>
        /// RETURNING clause for retrieving updated values.
        /// </summary>
        public IReadOnlyList<ClauseSelectItem>? ReturningClause { get; init; }

        #endregion
    }
}