using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.MergeClauses
{
    /// <summary>
    /// Represents a WHEN clause in a MERGE statement.
    /// </summary>
    [MemoryPackable]
    public sealed partial class ClauseMergeWhen : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ClauseMergeWhen clause)
                return false;

            return IsMatched.Is(clause.IsMatched)
                   && Condition.Check(clause.Condition)
                   && ActionType.Is(clause.ActionType)
                   && SetClauses.Is(clause.SetClauses)
                   && InsertColumns.Is(clause.InsertColumns)
                   && InsertValues.Is(clause.InsertValues);
        }

        public override ClauseMergeWhen Clone()
        {
            return new ClauseMergeWhen
            {
                IsMatched = IsMatched,
                Condition = (WitSqlExpression?)Condition?.Clone(),
                ActionType = ActionType,
                SetClauses = SetClauses?.Select(c => c.Clone()).ToList(),
                InsertColumns = InsertColumns?.ToList(),
                InsertValues = InsertValues?.Select(e => (WitSqlExpression)e.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// True for WHEN MATCHED, false for WHEN NOT MATCHED.
        /// </summary>
        [ToString]
        public bool IsMatched { get; init; }

        /// <summary>
        /// Optional additional condition (AND expression).
        /// </summary>
        public WitSqlExpression? Condition { get; init; }

        /// <summary>
        /// The type of action (Update, Delete, or Insert).
        /// </summary>
        [ToString]
        public MergeActionType ActionType { get; init; }

        /// <summary>
        /// SET clauses for UPDATE action.
        /// </summary>
        public IReadOnlyList<ClauseSet>? SetClauses { get; init; }

        /// <summary>
        /// Column names for INSERT action.
        /// </summary>
        public IReadOnlyList<string>? InsertColumns { get; init; }

        /// <summary>
        /// Values for INSERT action.
        /// </summary>
        public IReadOnlyList<WitSqlExpression>? InsertValues { get; init; }

        #endregion
    }
}
