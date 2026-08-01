using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Schema.TableSources;

/// <summary>
/// A subquery in <c>FROM</c> that may read the row beside it.
/// </summary>
/// <remarks>
/// <para>
/// One capability, two spellings. PostgreSQL writes <c>LATERAL</c> and puts it in the <c>FROM</c>
/// list; SQL Server writes <c>CROSS APPLY</c> or <c>OUTER APPLY</c> and puts it after the source it
/// correlates with. The dialect oracle measured both targets as having it, which is what turned it
/// from "skip if hard" into work worth doing - and the engine already evaluated a subquery per outer
/// row in <c>EXISTS</c>, <c>IN</c> and a scalar position, so what was missing was reaching that
/// machinery from a table source rather than from an expression.
/// </para>
/// <para>
/// <see cref="IsOuter"/> is the difference between the two forms: <c>CROSS APPLY</c> drops an outer
/// row whose subquery returned nothing, <c>OUTER APPLY</c> keeps it with nulls -
/// <c>LEFT JOIN LATERAL … ON TRUE</c> in PostgreSQL.
/// </para>
/// </remarks>
[MemoryPackable]
public sealed partial class TableSourceLateral : TableSource
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not TableSourceLateral lateral)
            return false;

        return base.Is(lateral, tolerance)
               && Subquery.Is(lateral.Subquery, tolerance)
               && IsOuter.Is(lateral.IsOuter)
               && Left.Check(lateral.Left)
               && ColumnAliases.Is(lateral.ColumnAliases);
    }

    public override ModelBase Clone()
    {
        return new TableSourceLateral
        {
            Subquery = Subquery.Clone(),
            IsOuter = IsOuter,
            Left = Left?.Clone() as TableSource,
            ColumnAliases = ColumnAliases?.ToArray(),
            Alias = Alias
        };
    }

    #endregion

    #region Properties

    /// <summary>The correlated subquery.</summary>
    public required WitSqlStatementSelect Subquery { get; init; }

    /// <summary>
    /// The source this one correlates with, for the <c>APPLY</c> spelling, which writes it on the
    /// left. Null for <c>LATERAL</c>, which takes its left from the <c>FROM</c> list.
    /// </summary>
    public TableSource? Left { get; init; }

    /// <summary>
    /// <c>true</c> for <c>OUTER APPLY</c>: an outer row whose subquery returned nothing is kept,
    /// with nulls, rather than dropped.
    /// </summary>
    public bool IsOuter { get; init; }

    /// <summary>The derived column list, if one was given.</summary>
    public IReadOnlyList<string>? ColumnAliases { get; init; }

    #endregion
}
