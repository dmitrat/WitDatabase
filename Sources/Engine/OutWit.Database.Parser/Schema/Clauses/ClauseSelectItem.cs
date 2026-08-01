using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.Clauses;

[MemoryPackable]
public sealed partial class ClauseSelectItem : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not ClauseSelectItem item)
            return false;

        return Expression.Check(item.Expression)
               && Alias.Is(item.Alias)
               && IsStar.Is(item.IsStar)
               && TableName.Is(item.TableName);
    }

    public override ClauseSelectItem Clone()
    {
        return new ClauseSelectItem
        {
            Expression = (WitSqlExpression?)Expression?.Clone(),
            Alias = Alias,
            IsStar = IsStar,
            TableName = TableName
        };
    }

    #endregion

    #region Model Base

    public WitSqlExpression? Expression { get; init; }
    public string? Alias { get; init; }
    public bool IsStar { get; init; }
    public string? TableName { get; init; }

    #endregion
}
