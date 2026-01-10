using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Information about a table column.
/// </summary>
public sealed class ColumnInfo : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not ColumnInfo other)
            return false;

        return Name.Is(other.Name)
               && OrdinalPosition.Is(other.OrdinalPosition)
               && DataType.Is(other.DataType)
               && IsNullable.Is(other.IsNullable)
               && IsPrimaryKey.Is(other.IsPrimaryKey)
               && DefaultValue.Is(other.DefaultValue);
    }

    public override ColumnInfo Clone()
    {
        return new ColumnInfo
        {
            Name = Name,
            OrdinalPosition = OrdinalPosition,
            DataType = DataType,
            IsNullable = IsNullable,
            IsPrimaryKey = IsPrimaryKey,
            DefaultValue = DefaultValue
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column position (1-based).
    /// </summary>
    public int OrdinalPosition { get; set; }

    /// <summary>
    /// Gets or sets the data type.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the column is nullable.
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Gets or sets whether the column is part of the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets the default value expression.
    /// </summary>
    public string? DefaultValue { get; set; }

    #endregion
}
