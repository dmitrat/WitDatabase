using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Types;

namespace OutWit.Database;

/// <summary>
/// Schema information for a result set column.
/// </summary>
public sealed class WitSqlColumnInfo : ModelBase
{
    #region ModelBase

    /// <inheritdoc/>
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitSqlColumnInfo info)
            return false;

        return Name.Is(info.Name)
            && Type.Is(info.Type)
            && IsNullable.Is(info.IsNullable)
            && TableName.Is(info.TableName);
    }

    /// <inheritdoc/>
    public override WitSqlColumnInfo Clone()
    {
        return new WitSqlColumnInfo
        {
            Name = Name,
            Type = Type,
            IsNullable = IsNullable,
            TableName = TableName
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the column name.
    /// </summary>
    [ToString]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the SQL type of the column.
    /// </summary>
    [ToString]
    public required WitSqlType Type { get; init; }

    /// <summary>
    /// Gets whether the column allows NULL values.
    /// </summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>
    /// Gets the source table name, if known.
    /// </summary>
    public string? TableName { get; init; }

    #endregion
}
