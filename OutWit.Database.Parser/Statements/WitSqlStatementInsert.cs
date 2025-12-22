using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Statements
{
    public class WitSqlStatementInsert : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementInsert(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementInsert insert)
                return false;

            return base.Is(insert, tolerance)
                   && TableName.Is(insert.TableName)
                   && ColumnNames.Is(insert.ColumnNames)
                   && SelectSource.Check(insert.SelectSource)
                   && ReturningClause.Is(insert.ReturningClause)
                   && ConflictResolution.Is(insert.ConflictResolution)
                   && OnConflict.Check(insert.OnConflict)
                   && Values?
                       .SelectMany(expressions => expressions)
                       .ToList()
                       .Is(insert.Values?
                           .SelectMany(expressions => expressions)
                           .ToList()) == true;
        }

        public override WitSqlStatementInsert Clone()
        {
            return new WitSqlStatementInsert
            {
                Line = Line,
                Column = Column,
                TableName = TableName,
                ColumnNames = ColumnNames?.ToList(),
                Values = Values?.Select(row => (IReadOnlyList<WitSqlExpression>)row.Select(x => (WitSqlExpression)x.Clone()).ToList()).ToList(),
                SelectSource = SelectSource?.Clone(),
                ReturningClause = ReturningClause?.Select(x => x.Clone()).ToList(),
                ConflictResolution = ConflictResolution,
                OnConflict = OnConflict?.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TableName { get; init; }

        public IReadOnlyList<string>? ColumnNames { get; init; }

        public IReadOnlyList<IReadOnlyList<WitSqlExpression>>? Values { get; init; }

        public WitSqlStatementSelect? SelectSource { get; init; }

        /// <summary>
        /// RETURNING clause for retrieving generated values.
        /// </summary>
        public IReadOnlyList<ClauseSelectItem>? ReturningClause { get; init; }

        /// <summary>
        /// Conflict resolution strategy (OR REPLACE, OR IGNORE).
        /// </summary>
        public ConflictResolutionType ConflictResolution { get; init; } = ConflictResolutionType.None;

        /// <summary>
        /// ON CONFLICT clause for upsert functionality.
        /// </summary>
        public ClauseOnConflict? OnConflict { get; init; }

        #endregion
    }
}