namespace OutWit.Database.Studio.Services;

/// <summary>
/// What a piece of SQL text is, lexically.
/// </summary>
public enum SqlTokenKind
{
    Whitespace,
    LineComment,
    BlockComment,
    String,
    Number,
    Word,
    QuotedName,
    Parameter,
    Punctuation
}

/// <param name="Start">Offset in the text the token was read from.</param>
public readonly record struct SqlToken(SqlTokenKind Kind, int Start, string Text)
{
    public int End => Start + Text.Length;

    public bool IsTrivia => Kind is SqlTokenKind.Whitespace or SqlTokenKind.LineComment or SqlTokenKind.BlockComment;

    public bool Is(string text) => string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Cuts SQL text into tokens - and nothing more than that.
///
/// This is deliberately NOT a parser and never decides what a statement means: <see cref="SqlScript"/>
/// asks the real one for that, and the reason it exists at all is that the real parser cannot answer
/// questions about text that is still being typed. Half a statement has no AST; it still has tokens,
/// and "what is the word in front of the caret" is a question about tokens.
///
/// The one property it has to get right is that a keyword inside a string or a comment is not a
/// keyword - which is the mistake every hand-written SQL splitter makes, and the reason Studio's
/// statement boundaries come from the parser instead.
/// </summary>
public static class SqlLexer
{
    #region Functions

    public static List<SqlToken> Tokenize(string text)
    {
        var tokens = new List<SqlToken>();

        if (string.IsNullOrEmpty(text))
            return tokens;

        var i = 0;

        while (i < text.Length)
        {
            var start = i;
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;

                Add(tokens, SqlTokenKind.Whitespace, text, start, i);
                continue;
            }

            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;

                Add(tokens, SqlTokenKind.LineComment, text, start, i);
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;

                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    i++;

                i = Math.Min(text.Length, i + 2);

                Add(tokens, SqlTokenKind.BlockComment, text, start, i);
                continue;
            }

            if (c == '\'')
            {
                i++;

                while (i < text.Length)
                {
                    if (text[i] == '\'')
                    {
                        // Two quotes are one quote inside the literal, not the end of it.
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                Add(tokens, SqlTokenKind.String, text, start, i);
                continue;
            }

            if (c is '"' or '[' or '`')
            {
                var closing = c switch { '"' => '"', '[' => ']', _ => '`' };

                i++;

                while (i < text.Length && text[i] != closing)
                    i++;

                i = Math.Min(text.Length, i + 1);

                Add(tokens, SqlTokenKind.QuotedName, text, start, i);
                continue;
            }

            if (c is '@' or ':' or '$' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '_'))
            {
                i++;

                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;

                Add(tokens, SqlTokenKind.Parameter, text, start, i);
                continue;
            }

            if (char.IsDigit(c))
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.'))
                    i++;

                Add(tokens, SqlTokenKind.Number, text, start, i);
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;

                Add(tokens, SqlTokenKind.Word, text, start, i);
                continue;
            }

            i++;
            Add(tokens, SqlTokenKind.Punctuation, text, start, i);
        }

        return tokens;
    }

    /// <summary>
    /// Whether the text carries a comment anywhere in it. Asked by the formatter, which rebuilds a
    /// statement from its syntax tree - and the tree has no comments in it, because the grammar skips
    /// them at the lexer. Reformatting a commented statement would therefore delete the comment.
    /// </summary>
    public static bool HasComment(string text)
    {
        return Tokenize(text).Any(token =>
            token.Kind is SqlTokenKind.LineComment or SqlTokenKind.BlockComment);
    }

    /// <summary>
    /// The last token that starts at or before <paramref name="offset"/>, ignoring trivia. This is
    /// "the word behind the caret", which is what decides what completion offers.
    /// </summary>
    public static SqlToken? Previous(IReadOnlyList<SqlToken> tokens, int offset, int skip = 0)
    {
        var seen = 0;

        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i];

            if (token.Start >= offset || token.IsTrivia)
                continue;

            if (seen++ < skip)
                continue;

            return token;
        }

        return null;
    }

    /// <summary>
    /// Whether the offset is inside a string or a comment - where nothing should be suggested and no
    /// keyword means anything.
    /// </summary>
    public static bool IsInsideTextOrComment(IReadOnlyList<SqlToken> tokens, int offset)
    {
        foreach (var token in tokens)
        {
            if (token.Kind is not (SqlTokenKind.String or SqlTokenKind.LineComment or SqlTokenKind.BlockComment))
                continue;

            if (offset > token.Start && offset <= token.End)
                return true;
        }

        return false;
    }

    private static void Add(List<SqlToken> tokens, SqlTokenKind kind, string text, int start, int end)
    {
        tokens.Add(new SqlToken(kind, start, text[start..end]));
    }

    #endregion
}
