using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a named table constraint.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionNamedConstraint : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionNamedConstraint other)
                return false;

            return Name.Is(other.Name)
                   && Type.Is(other.Type)
                   && Columns.Is(other.Columns)
                   && CheckExpression.Is(other.CheckExpression)
                   && Check.Check(other.Check)
                   && ForeignKey.Check(other.ForeignKey);
        }

        public override DefinitionNamedConstraint Clone()
        {
            return new DefinitionNamedConstraint
            {
                Name = Name,
                Type = Type,
                Columns = Columns?.ToList(),
                CheckExpression = CheckExpression,
                Check = Check?.Clone() as WitSqlExpression,
                ForeignKey = ForeignKey?.Clone()
            };
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"CONSTRAINT {Name} ({Type})";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the constraint name.
        /// </summary>
        [ToString]
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the constraint type.
        /// </summary>
        [MemoryPackOrder(1)]
        public required ConstraintType Type { get; init; }

        /// <summary>
        /// Gets the columns involved in this constraint.
        /// Used for PRIMARY KEY, UNIQUE, and FOREIGN KEY constraints.
        /// </summary>
        [MemoryPackOrder(2)]
        public IReadOnlyList<string>? Columns { get; init; }

        /// <summary>
        /// Gets the CHECK constraint expression as SQL text.
        /// Only used when Type is Check.
        /// </summary>
        [MemoryPackOrder(3)]
        public string? CheckExpression { get; init; }

        /// <summary>
        /// The constraint's condition, stored as a tree. <see cref="CheckExpression"/> is a
        /// rendering of it for <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        [MemoryPackOrder(5)]
        public WitSqlExpression? Check { get; init; }

        /// <summary>The condition as a tree, falling back to the stored text before 9.0.0.</summary>
        /// <summary>The condition as SQL for <c>INFORMATION_SCHEMA</c>, rendered from the tree.</summary>
        public string? DisplayCheck() => SchemaText.Render(Check) ?? CheckExpression;

        public WitSqlExpression? ResolveCheck() =>
            Check ?? (string.IsNullOrEmpty(CheckExpression)
                ? null
                : WitSql.ParseExpression(CheckExpression));

        /// <summary>
        /// Gets the foreign key definition.
        /// Only used when Type is ForeignKey.
        /// </summary>
        [MemoryPackOrder(4)]
        public DefinitionForeignKey? ForeignKey { get; init; }

        #endregion
    }

    /// <summary>
    /// Type of table constraint.
    /// </summary>
    public enum ConstraintType
    {
        /// <summary>
        /// Primary key constraint.
        /// </summary>
        PrimaryKey,

        /// <summary>
        /// Unique constraint.
        /// </summary>
        Unique,

        /// <summary>
        /// Foreign key constraint.
        /// </summary>
        ForeignKey,

        /// <summary>
        /// Check constraint.
        /// </summary>
        Check
    }
}
