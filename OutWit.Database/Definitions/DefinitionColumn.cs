using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Types;

namespace OutWit.Database.Definitions;

/// <summary>
/// Defines a column in a database table.
/// </summary>
public sealed class DefinitionColumn : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if(modelBase is not DefinitionColumn other)
            return false;

        return Name.Is(other.Name)
            && Type.Is(other.Type)
            && Nullable.Is(other.Nullable)
            && IsPrimaryKey.Is(other.IsPrimaryKey)
            && IsAutoIncrement.Is(other.IsAutoIncrement)
            && IsUnique.Is(other.IsUnique)
            && DefaultValue.Is(other.DefaultValue)
            && Ordinal.Is(other.Ordinal)
            && CheckExpression.Is(other.CheckExpression)
            && ForeignKey.Check(other.ForeignKey);
    }

    public override DefinitionColumn Clone()
    {
        return new DefinitionColumn
        {
            Name = Name,
            Type = Type,
            Nullable = Nullable,
            IsPrimaryKey = IsPrimaryKey,
            IsAutoIncrement = IsAutoIncrement,
            IsUnique = IsUnique,
            DefaultValue = DefaultValue,
            Ordinal = Ordinal,
            CheckExpression = CheckExpression,
            ForeignKey = ForeignKey?.Clone()
        };
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"{Name} {Type}{(Nullable ? "" : " NOT NULL")}{(IsPrimaryKey ? " PRIMARY KEY" : "")}";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the column type.
    /// </summary>
    public required WitDataType Type { get; init; }

    /// <summary>
    /// Gets whether this column allows NULL values.
    /// </summary>
    public bool Nullable { get; init; } = true;

    /// <summary>
    /// Gets whether this column is part of the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// Gets whether this column auto-increments.
    /// </summary>
    public bool IsAutoIncrement { get; init; }

    /// <summary>
    /// Gets whether this column has a UNIQUE constraint.
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// Gets the default value expression (if any).
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets the column ordinal (0-based position).
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets the CHECK constraint expression as SQL text (if any).
    /// </summary>
    public string? CheckExpression { get; init; }

    /// <summary>
    /// Gets the foreign key definition (if this column references another table).
    /// </summary>
    public DefinitionForeignKey? ForeignKey { get; init; }

    #endregion
}