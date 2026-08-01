using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Statements;

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
               && Subquery.Is(subquery.Subquery, tolerance);
    }

    public override ModelBase Clone()
    {
        return new TableSourceSubquery
        {
            Subquery = Subquery.Clone(),
            Alias = Alias
        };
    }

    #endregion

    #region Properties

    public required WitSqlStatementSelect Subquery { get; init; }

    #endregion
}
