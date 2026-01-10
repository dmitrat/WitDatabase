using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.Values;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Represents a column definition for table creation/editing.
/// </summary>
public sealed class ColumnDefinition : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not ColumnDefinition other)
            return false;

        return Name.Is(other.Name)
            && DataType.Is(other.DataType)
            && IsNullable.Is(other.IsNullable)
            && IsPrimaryKey.Is(other.IsPrimaryKey)
            && IsAutoIncrement.Is(other.IsAutoIncrement)
            && IsUnique.Is(other.IsUnique)
            && DefaultValue.Is(other.DefaultValue)
            && CheckConstraint.Is(other.CheckConstraint);
    }

    public override ColumnDefinition Clone()
    {
        return new ColumnDefinition
        {
            Name = Name,
            DataType = DataType,
            IsNullable = IsNullable,
            IsPrimaryKey = IsPrimaryKey,
            IsAutoIncrement = IsAutoIncrement,
            IsUnique = IsUnique,
            DefaultValue = DefaultValue,
            CheckConstraint = CheckConstraint
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    [Notify]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data type.
    /// </summary>
    [Notify]
    public string DataType { get; set; } = "TEXT";

    /// <summary>
    /// Gets or sets whether the column allows NULL values.
    /// </summary>
    [Notify]
    public bool IsNullable { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the column is part of the primary key.
    /// </summary>
    [Notify]
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets whether the column is auto-incremented.
    /// </summary>
    [Notify]
    public bool IsAutoIncrement { get; set; }

    /// <summary>
    /// Gets or sets whether the column has a UNIQUE constraint.
    /// </summary>
    [Notify]
    public bool IsUnique { get; set; }

    /// <summary>
    /// Gets or sets the default value expression.
    /// </summary>
    [Notify]
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the CHECK constraint expression.
    /// </summary>
    [Notify]
    public string? CheckConstraint { get; set; }

    #endregion
}
