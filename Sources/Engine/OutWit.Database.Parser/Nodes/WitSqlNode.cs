using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Nodes
{
    /// <summary>
    /// Base class for all SQL AST nodes.
    /// </summary>
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(WitSqlExpression))]
    [MemoryPackUnion(1, typeof(WitSqlStatement))]
    public abstract partial class WitSqlNode : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not WitSqlNode other)
                return false;

            return Line.Is(other.Line)
                && Column.Is(other.Column);
        }

        #endregion

        #region Functions

        /// <summary>
        /// Accept a visitor.
        /// </summary>
        public abstract T Accept<T>(IWitSqlVisitor<T> visitor);

        #endregion

        #region Properties

        /// <summary>
        /// Line number in source SQL (1-based).
        /// </summary>
        public int Line { get; init; }

        /// <summary>
        /// Column position in source SQL (0-based).
        /// </summary>
        public int Column { get; init; }

        #endregion
    }
}
