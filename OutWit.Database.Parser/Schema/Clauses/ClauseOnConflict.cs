using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.Clauses
{
    /// <summary>
    /// Represents an ON CONFLICT clause in an INSERT statement.
    /// </summary>
    public class ClauseOnConflict : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ClauseOnConflict clause)
                return false;

            return ConflictColumns.Is(clause.ConflictColumns)
                   && ActionType.Is(clause.ActionType)
                   && UpdateClauses.Is(clause.UpdateClauses)
                   && WhereClause.Check(clause.WhereClause);
        }

        public override ClauseOnConflict Clone()
        {
            return new ClauseOnConflict
            {
                ConflictColumns = ConflictColumns?.ToList(),
                ActionType = ActionType,
                UpdateClauses = UpdateClauses?.Select(c => c.Clone()).ToList(),
                WhereClause = (WitSqlExpression?)WhereClause?.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The columns that define the conflict (optional).
        /// </summary>
        public IReadOnlyList<string>? ConflictColumns { get; init; }

        /// <summary>
        /// The action to take on conflict (NOTHING or UPDATE).
        /// </summary>
        public required ConflictActionType ActionType { get; init; }

        /// <summary>
        /// The SET clauses for DO UPDATE action.
        /// </summary>
        public IReadOnlyList<ClauseSet>? UpdateClauses { get; init; }

        /// <summary>
        /// The WHERE clause for DO UPDATE action (optional).
        /// </summary>
        public WitSqlExpression? WhereClause { get; init; }

        #endregion
    }
}
