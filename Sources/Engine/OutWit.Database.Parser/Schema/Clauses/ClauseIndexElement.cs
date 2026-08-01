using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.Clauses;

/// <summary>
/// Represents an index element - either a column name or an expression.
/// </summary>
[MemoryPackable]
public sealed partial class ClauseIndexElement : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not ClauseIndexElement element)
            return false;

        return ColumnName.Is(element.ColumnName)
               && Expression.Check(element.Expression)
               && Descending.Is(element.Descending);
    }

    public override ClauseIndexElement Clone()
    {
        return new ClauseIndexElement
        {
            ColumnName = ColumnName,
            Expression = (WitSqlExpression?)Expression?.Clone(),
            Descending = Descending
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Column name for simple column index. Null if this is an expression index.
    /// </summary>
    [ToString]
    public string? ColumnName { get; init; }

    /// <summary>
    /// Expression for expression-based index (e.g., LOWER(column)). Null if this is a column index.
    /// </summary>
    public WitSqlExpression? Expression { get; init; }

    /// <summary>
    /// Whether this element is sorted in descending order.
    /// </summary>
    [ToString]
    public bool Descending { get; init; }

    /// <summary>
    /// Returns true if this is an expression index element.
    /// </summary>
    public bool IsExpression => Expression != null;

    #endregion
}
