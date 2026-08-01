using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Statements;
using OutWit.Common.Collections;

namespace OutWit.Database.Parser.Schema.TableSources;

[MemoryPackable]
public sealed partial class TableSourceSubquery : TableSource
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not TableSourceSubquery subquery)
            return false;

        return base.Is(subquery, tolerance) 
               && Subquery.Is(subquery.Subquery, tolerance)
               && ColumnAliases.Is(subquery.ColumnAliases);
    }

    public override ModelBase Clone()
    {
        return new TableSourceSubquery
        {
            Subquery = Subquery.Clone(),
            ColumnAliases = ColumnAliases?.ToArray(),
            Alias = Alias
        };
    }

    #endregion

    #region Properties

    public required WitSqlStatementSelect Subquery { get; init; }

    /// <summary>
    /// The derived column list of <c>AS V (a, b)</c>, renaming the subquery's output columns
    /// positionally, or null when none was given.
    /// </summary>
    /// <remarks>
    /// Supported by PostgreSQL and SQL Server and rejected by SQLite - which is why the SQLite
    /// oracle could not answer whether it was worth building, and the dialect oracle could.
    /// </remarks>
    public IReadOnlyList<string>? ColumnAliases { get; init; }

    #endregion
}
