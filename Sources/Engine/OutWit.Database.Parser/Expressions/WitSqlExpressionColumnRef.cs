using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions;

[MemoryPackable]
public partial class WitSqlExpressionColumnRef : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionColumnRef(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionColumnRef column)
            return false;

        return base.Is(column, tolerance)
               && TableName.Is(column.TableName)
               && ColumnName.Is(column.ColumnName)
               && IsExcluded.Is(column.IsExcluded);
    }

    public override WitSqlExpressionColumnRef Clone()
    {
        return new WitSqlExpressionColumnRef
        {
            Line = Line,
            Column = Column,
            TableName = TableName,
            ColumnName = ColumnName,
            IsExcluded = IsExcluded
        };
    }

    #endregion

    #region Properties

    [ToString]
    public string? TableName { get; init; }

    [ToString]
    public required string ColumnName { get; init; }

    /// <summary>
    /// Indicates if this is an EXCLUDED.column reference (used in ON CONFLICT DO UPDATE).
    /// </summary>
    public bool IsExcluded { get; init; }

    #endregion
}