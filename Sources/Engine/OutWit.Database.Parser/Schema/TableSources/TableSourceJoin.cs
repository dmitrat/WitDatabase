using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.TableSources
{
    [MemoryPackable]
    public sealed partial class TableSourceJoin : TableSource
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableSourceJoin join)
                return false;

            return base.Is(join, tolerance)
                   && Left.Is(join.Left, tolerance)
                   && Right.Is(join.Right, tolerance)
                   && JoinType.Is(join.JoinType)
                   && OnCondition.Check(join.OnCondition);
        }

        public override TableSourceJoin Clone()
        {
            return new TableSourceJoin
            {
                Left = (TableSource)Left.Clone(),
                Right = (TableSource)Right.Clone(),
                JoinType = JoinType,
                OnCondition = (WitSqlExpression?)OnCondition?.Clone(),
                Alias = Alias
            };
        }

        #endregion

        #region Properties

        public required TableSource Left { get; init; }
        public required TableSource Right { get; init; }
        public required JoinType JoinType { get; init; }

        /// <summary>
        /// The <c>ON</c> predicate, or <c>null</c> for a join that has none - <c>CROSS JOIN</c>, and
        /// the comma form.
        /// </summary>
        /// <remarks>
        /// Declared non-nullable until 2026-07-31 while the visitor assigned null to it for exactly
        /// those joins, which the compiler reported as <c>CS8601</c> on every build. Both
        /// <c>Is</c> and <c>Clone</c> dereferenced it unconditionally, so comparing or copying any
        /// <c>CROSS JOIN</c> threw <c>NullReferenceException</c>.
        /// </remarks>
        public WitSqlExpression? OnCondition { get; init; }

        #endregion
    }
}
