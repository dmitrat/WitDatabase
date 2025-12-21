using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Parser;

/// <summary>
/// Parsing error with location information.
/// </summary>
public sealed class WitSqlParsingError : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if(modelBase is not WitSqlParsingError other)
            return false;

        return Line.Is(other.Line)
               && Column.Is(other.Column)
               && Message.Is(other.Message);
    }

    public override WitSqlParsingError Clone()
    {
        return new WitSqlParsingError
        {
            Line = Line,
            Column = Column,
            Message = Message
        };
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"Line {Line}:{Column} - {Message}";
    }

    #endregion

    #region Properties

    public required int Line { get; init; }

    public required int Column { get; init; }

    public required string Message { get; init; }

    #endregion


}