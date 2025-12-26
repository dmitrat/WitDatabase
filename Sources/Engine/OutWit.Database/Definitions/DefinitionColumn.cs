using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Types;

namespace OutWit.Database.Definitions;

/// <summary>
/// Defines a column in a database table.
/// </summary>
[MemoryPackable]
public sealed partial class DefinitionColumn : ModelBase
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
            && ForeignKey.Check(other.ForeignKey)
            && MaxLength.Is(other.MaxLength)
            && Precision.Is(other.Precision)
            && Scale.Is(other.Scale)
            && ComputedExpression.Is(other.ComputedExpression)
            && IsStored.Is(other.IsStored)
            && Collation.Is(other.Collation)
            && ConstraintName.Is(other.ConstraintName);
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
            ForeignKey = ForeignKey?.Clone(),
            MaxLength = MaxLength,
            Precision = Precision,
            Scale = Scale,
            ComputedExpression = ComputedExpression,
            IsStored = IsStored,
            Collation = Collation,
            ConstraintName = ConstraintName
        };
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        var type = Type.ToString();
        if (MaxLength.HasValue)
            type += $"({MaxLength.Value})";
        else if (Precision.HasValue)
            type += Scale.HasValue ? $"({Precision.Value},{Scale.Value})" : $"({Precision.Value})";

        return $"{Name} {type}{(Nullable ? "" : " NOT NULL")}{(IsPrimaryKey ? " PRIMARY KEY" : "")}";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the column name.
    /// </summary>
    [MemoryPackOrder(0)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the column type.
    /// </summary>
    [MemoryPackOrder(1)]
    public required WitDataType Type { get; init; }

    /// <summary>
    /// Gets whether this column allows NULL values.
    /// </summary>
    [MemoryPackOrder(2)]
    public bool Nullable { get; set; } = true;

    /// <summary>
    /// Gets whether this column is part of the primary key.
    /// </summary>
    [MemoryPackOrder(3)]
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Gets whether this column auto-increments.
    /// </summary>
    [MemoryPackOrder(4)]
    public bool IsAutoIncrement { get; set; }

    /// <summary>
    /// Gets whether this column has a UNIQUE constraint.
    /// </summary>
    [MemoryPackOrder(5)]
    public bool IsUnique { get; set; }

    /// <summary>
    /// Gets the default value expression (if any).
    /// </summary>
    [MemoryPackOrder(6)]
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Gets the column ordinal (0-based position).
    /// </summary>
    [MemoryPackOrder(7)]
    public int Ordinal { get; set; }

    /// <summary>
    /// Gets the CHECK constraint expression as SQL text (if any).
    /// </summary>
    [MemoryPackOrder(8)]
    public string? CheckExpression { get; set; }

    /// <summary>
    /// Gets the foreign key definition (if this column references another table).
    /// </summary>
    [MemoryPackOrder(9)]
    public DefinitionForeignKey? ForeignKey { get; set; }

    /// <summary>
    /// Gets the maximum length for CHAR(n), VARCHAR(n), BINARY(n), VARBINARY(n) types.
    /// Null for types without length specification.
    /// </summary>
    [MemoryPackOrder(10)]
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the precision for DECIMAL(p,s) type (total number of digits).
    /// Null for types without precision specification.
    /// </summary>
    [MemoryPackOrder(11)]
    public int? Precision { get; init; }

    /// <summary>
    /// Gets the scale for DECIMAL(p,s) type (digits after decimal point).
    /// Null for types without scale specification.
    /// </summary>
    [MemoryPackOrder(12)]
    public int? Scale { get; init; }

    /// <summary>
    /// Gets the computed column expression (for columns defined as "AS (expression)").
    /// Null for regular columns.
    /// </summary>
    [MemoryPackOrder(13)]
    public string? ComputedExpression { get; init; }

    /// <summary>
    /// Gets whether a computed column is STORED (persisted) or VIRTUAL (calculated on access).
    /// Only meaningful when ComputedExpression is not null.
    /// </summary>
    [MemoryPackOrder(14)]
    public bool IsStored { get; init; }

    /// <summary>
    /// Gets the collation for string columns (BINARY, NOCASE, UNICODE, UNICODE_CI).
    /// Null means default collation.
    /// </summary>
    [MemoryPackOrder(15)]
    public string? Collation { get; init; }

    /// <summary>
    /// Gets the name of the constraint (for EF Core migrations).
    /// Used for PRIMARY KEY, UNIQUE, CHECK, and FOREIGN KEY constraints.
    /// </summary>
    [MemoryPackOrder(16)]
    public string? ConstraintName { get; init; }

    /// <summary>
    /// Gets whether this is a computed column.
    /// </summary>
    [MemoryPackIgnore]
    public bool IsComputed => ComputedExpression != null;

    #endregion
}