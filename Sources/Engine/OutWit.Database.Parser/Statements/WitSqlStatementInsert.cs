using MemoryPack;
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
    [MemoryPackable]
    public partial class WitSqlStatementInsert : WitSqlStatement
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
                   && RowsAre(Values, insert.Values, tolerance);
        }

        /// <summary>
        /// Compares the <c>VALUES</c> rows, row by row.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Until 2026-07-31 this read <c>Values?.SelectMany(…).Is(…) == true</c>, which had two
        /// faults. An <c>INSERT … SELECT</c> has no <c>VALUES</c> at all, so <c>Values</c> was null,
        /// the null-propagation yielded null, and <c>null == true</c> is false - <b>such a statement
        /// never compared equal even to itself</b>. And flattening every row into one sequence lost
        /// the row boundaries, so <c>VALUES (1, 2), (3)</c> and <c>VALUES (1), (2, 3)</c> compared
        /// equal.
        /// </para>
        /// </remarks>
        private static bool RowsAre(IReadOnlyList<IReadOnlyList<WitSqlExpression>>? left,
            IReadOnlyList<IReadOnlyList<WitSqlExpression>>? right, double tolerance)
        {
            if (left is null || right is null)
                return left is null && right is null;

            if (left.Count != right.Count)
                return false;

            for (var row = 0; row < left.Count; row++)
            {
                if (left[row].Count != right[row].Count)
                    return false;

                for (var column = 0; column < left[row].Count; column++)
                {
                    if (!left[row][column].Is(right[row][column], tolerance))
                        return false;
                }
            }

            return true;
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