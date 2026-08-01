using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.Clauses
{
    [MemoryPackable]
    public sealed partial class ClauseOrderByItem : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ClauseOrderByItem item)
                return false;

            return Expression.Is(item.Expression, tolerance)
                   && Descending.Is(item.Descending)
                   && NullsOrder.Is(item.NullsOrder);
        }

        public override ClauseOrderByItem Clone()
        {
            return new ClauseOrderByItem
            {
                Expression = (WitSqlExpression)Expression.Clone(),
                Descending = Descending,
                NullsOrder = NullsOrder
            };
        }

        #endregion

        #region Properties

        public required WitSqlExpression Expression { get; init; }
        public bool Descending { get; init; }
        public NullsOrderType NullsOrder { get; init; }

        #endregion
    }
}
