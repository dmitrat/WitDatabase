using Antlr4.Runtime;

namespace OutWit.Database.Parser;

/// <summary>
/// ANTLR error listener that collects parse errors.
/// </summary>
internal class WitSqlParsingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
{
    #region Fields

    private readonly List<WitSqlParsingError> m_errors = new();

    #endregion

    #region Functions

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        m_errors.Add(new WitSqlParsingError
        {
            Line = line,
            Column = charPositionInLine,
            Message = msg
        });
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        m_errors.Add(new WitSqlParsingError
        {
            Line = line,
            Column = charPositionInLine,
            Message = msg
        });
    }

    #endregion

    #region Properties

    public IReadOnlyList<WitSqlParsingError> Errors => m_errors;

    #endregion
}