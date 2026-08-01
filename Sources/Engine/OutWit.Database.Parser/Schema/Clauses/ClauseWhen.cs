using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.Clauses;

[MemoryPackable]
public sealed partial class ClauseWhen : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase? modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ClauseWhen clause)
            return false;

        return When.Is(clause.When, tolerance) 
               && Then.Is(clause.Then, tolerance);
    }

    public override ClauseWhen Clone()
    {
        return new ClauseWhen
        {
            When = (WitSqlExpression)When.Clone(),
            Then = (WitSqlExpression)Then.Clone()
        };
    }

    #endregion

    #region Properties

    [ToString]
    public required WitSqlExpression When { get; init; }

    [ToString]
    public required WitSqlExpression Then { get; init; }

    #endregion
}
