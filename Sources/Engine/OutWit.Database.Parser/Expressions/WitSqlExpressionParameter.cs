using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Expressions;

/// <summary>
/// Represents a parameter placeholder in a SQL statement.
/// Supports named (@param, :param, $param), positional (?), and numbered ($1) parameters.
/// </summary>
public class WitSqlExpressionParameter : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionParameter(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionParameter param)
            return false;

        return base.Is(param, tolerance)
               && ParameterType.Is(param.ParameterType)
               && Name.Is(param.Name)
               && Position.Is(param.Position);
    }

    public override WitSqlExpressionParameter Clone()
    {
        return new WitSqlExpressionParameter
        {
            Line = Line,
            Column = Column,
            ParameterType = ParameterType,
            Name = Name,
            Position = Position
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The type of parameter placeholder.
    /// </summary>
    [ToString]
    public required ParameterType ParameterType { get; init; }

    /// <summary>
    /// The parameter name (for named and colon parameters).
    /// </summary>
    [ToString]
    public string? Name { get; init; }

    /// <summary>
    /// The parameter position (for numbered parameters like $1, $2).
    /// </summary>
    public int? Position { get; init; }

    #endregion
}
