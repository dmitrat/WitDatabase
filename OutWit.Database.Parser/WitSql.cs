using Antlr4.Runtime;
using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Visitor;

namespace OutWit.Database.Parser;

/// <summary>
/// Entry point for parsing WitSQL statements.
/// </summary>
public static class WitSql
{
    /// <summary>
    /// Tries to parse SQL text. Returns result with errors if parsing fails.
    /// </summary>
    public static WitSqlParsingResult TryParse(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var inputStream = new AntlrInputStream(sql);
        var lexer = new WitSqlLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new WitSqlParser(tokenStream);

        var errorListener = new WitSqlParsingErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var context = parser.script();

        if (errorListener.Errors.Count > 0)
            return new WitSqlParsingResult { Errors = errorListener.Errors };

        var visitor = new WitSqlVisitor();
        var statements = visitor.VisitScript(context);

        return new WitSqlParsingResult { Statements = statements };
    }

    /// <summary>
    /// Parses SQL text into AST. Throws WitSqlParsingException on error.
    /// </summary>
    /// <param name="sql">SQL text to parse</param>
    /// <returns>List of parsed statements</returns>
    /// <exception cref="WitSqlParsingException">When parsing fails</exception>
    public static IReadOnlyList<WitSqlStatement> Parse(string sql)
    {
        var result = TryParse(sql);

        return result.IsSuccess
            ? result.Statements
            : throw new WitSqlParsingException(result.Errors);
    }

    /// <summary>
    /// Parses a single SQL statement. Throws if there are multiple statements.
    /// </summary>
    public static WitSqlStatement ParseStatement(string sql)
    {
        var statements = Parse(sql);
        if (statements.Count == 0)
        {
            throw new WitSqlParsingException([new WitSqlParsingError { Line = 1, Column = 0, Message = "No statement found" }]);
        }
        if (statements.Count > 1)
        {
            throw new WitSqlParsingException([new WitSqlParsingError { Line = 1, Column = 0, Message = $"Expected single statement, got {statements.Count}" }]);
        }
        return statements[0];
    }

    /// <summary>
    /// Parses a SQL expression.
    /// </summary>
    public static WitSqlExpression ParseExpression(string expr)
    {
        // Wrap in SELECT to parse as expression
        var statements = Parse($"SELECT {expr}");

        if (statements.Count != 1 || statements[0] is not WitSqlStatementSelect select)
        {
            throw new WitSqlParsingException([new WitSqlParsingError { Line = 1, Column = 0, Message = "Failed to parse expression" }]);
        }
        if (select.SelectList.Count != 1 || select.SelectList[0].Expression == null)
        {
            throw new WitSqlParsingException([new WitSqlParsingError { Line = 1, Column = 0, Message = "Invalid expression" }]);
        }
        return select.SelectList[0].Expression;
    }


}