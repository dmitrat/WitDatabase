using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Schema.Clauses;

/// <summary>
/// Represents a single CTE (Common Table Expression) definition in a WITH clause.
/// </summary>
public class ClauseCteDefinition : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not ClauseCteDefinition cte)
            return false;

        return Name.Is(cte.Name)
               && ColumnNames.Is(cte.ColumnNames)
               && Query.Is(cte.Query, tolerance);
    }

    public override ClauseCteDefinition Clone()
    {
        return new ClauseCteDefinition
        {
            Name = Name,
            ColumnNames = ColumnNames?.ToList(),
            Query = Query.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The name of the CTE.
    /// </summary>
    [ToString]
    public required string Name { get; init; }

    /// <summary>
    /// Optional list of column names for the CTE.
    /// </summary>
    public IReadOnlyList<string>? ColumnNames { get; init; }

    /// <summary>
    /// The query that defines the CTE.
    /// </summary>
    public required WitSqlStatementSelect Query { get; init; }

    #endregion
}
