using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Schema.Clauses;

/// <summary>
/// Represents a set operation (UNION, INTERSECT, EXCEPT) with its right operand.
/// </summary>
public class ClauseSetOperation : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not ClauseSetOperation setOp)
            return false;

        return OperationType.Is(setOp.OperationType)
               && IsAll.Is(setOp.IsAll)
               && RightQuery.Is(setOp.RightQuery, tolerance);
    }

    public override ClauseSetOperation Clone()
    {
        return new ClauseSetOperation
        {
            OperationType = OperationType,
            IsAll = IsAll,
            RightQuery = RightQuery.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The type of set operation.
    /// </summary>
    [ToString]
    public required SetOperationType OperationType { get; init; }

    /// <summary>
    /// Whether this is UNION ALL (preserves duplicates).
    /// </summary>
    public bool IsAll { get; init; }

    /// <summary>
    /// The right operand of the set operation.
    /// </summary>
    public required WitSqlStatementSelect RightQuery { get; init; }

    #endregion
}
