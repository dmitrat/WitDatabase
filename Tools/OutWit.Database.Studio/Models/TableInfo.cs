using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Information about a database table.
/// </summary>
public sealed class TableInfo : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not TableInfo other)
            return false;

        return Name.Is(other.Name)
            && RowCount.Is(other.RowCount);
    }

    public override TableInfo Clone()
    {
        return new TableInfo
        {
            Name = Name,
            RowCount = RowCount
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the estimated row count.
    /// </summary>
    public long RowCount { get; set; }

    #endregion
}
