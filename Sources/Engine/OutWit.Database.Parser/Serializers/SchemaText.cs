using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Analysis;

namespace OutWit.Database.Parser.Serializers;

/// <summary>
/// Renders schema back to SQL text for <c>INFORMATION_SCHEMA</c> to report - and refuses to render
/// anything it cannot render faithfully.
/// </summary>
/// <remarks>
/// <para>
/// From 9.0.0 the catalog stores schema as trees, so this text executes nothing; it exists so the
/// standard catalog columns have something to show. That makes a gap in the renderer cosmetic rather
/// than corrupting - <b>but only if the renderer stops claiming to have rendered things it dropped</b>.
/// A view over <c>SELECT … UNION SELECT …</c> renders as its first branch alone, and reporting that
/// as the view's definition is a plain untruth in a column people read to understand a database.
/// </para>
/// <para>
/// So every rendering is verified the way the phase's own instrument verifies a round trip: render
/// it, read it back, and compare the trees. If they differ, the rendering lost something and
/// <c>null</c> is returned. The column then reports nothing, which is a true statement about a
/// database, where the alternative is a false one.
/// </para>
/// <para>
/// Not a placeholder comment, and not a best-effort string. Both read as SQL to whatever consumes
/// the column, and "some SQL came out" is exactly the impression to avoid.
/// </para>
/// <para>
/// The cost is one extra parse per DDL statement executed. DDL is cold, and this replaced code that
/// was parsing schema on the row path.
/// </para>
/// </remarks>
public static class SchemaText
{
    #region Functions

    /// <summary>
    /// The statement as SQL, or <c>null</c> if it cannot be written down without loss.
    /// </summary>
    public static string? Render(WitSqlStatement? statement)
    {
        if (statement is null)
            return null;

        string text;

        try
        {
            text = WitSqlStatementSerializer.Serialize(statement);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        return Verify(text, () => WitSql.ParseStatement(text), statement);
    }

    /// <summary>
    /// The expression as SQL, or <c>null</c> if it cannot be written down without loss - which is
    /// what happens to every subquery, since the expression renderer writes those as the literal
    /// text <c>SELECT ...</c>.
    /// </summary>
    public static string? Render(WitSqlExpression? expression)
    {
        if (expression is null)
            return null;

        string text;

        try
        {
            text = WitSqlExpressionSerializer.Serialize(expression);
        }
        catch (NotSupportedException)
        {
            return null;
        }

        return Verify(text, () => WitSql.ParseExpression(text), expression);
    }

    /// <summary>
    /// Renders several statements as one string, or <c>null</c> if any of them cannot be rendered.
    /// All or nothing: half a trigger body reads as a whole one.
    /// </summary>
    public static string? Render(IReadOnlyList<WitSqlStatement>? statements)
    {
        if (statements is null)
            return null;

        var parts = new string[statements.Count];

        for (var i = 0; i < statements.Count; i++)
        {
            var part = Render(statements[i]);

            if (part is null)
                return null;

            parts[i] = part;
        }

        return string.Join("; ", parts);
    }

    #endregion

    #region Verification

    /// <summary>
    /// Returns <paramref name="text"/> only if reading it back produces the same tree.
    /// </summary>
    /// <remarks>
    /// Positions are ignored. <c>Is</c> compares <c>Line</c>/<c>Column</c>, and a fragment
    /// re-parsed on its own necessarily sits at different ones from the same fragment parsed
    /// inside the statement it came from - so comparing with <c>Is</c> answers "not faithful" for
    /// every rendering, including the faithful ones. Measured: it emptied
    /// <c>INFORMATION_SCHEMA.VIEWS.VIEW_DEFINITION</c> for a plain <c>SELECT * FROM Users</c>.
    /// </remarks>
    private static string? Verify<T>(string text, Func<T> reparse, T original)
        where T : Common.Abstract.ModelBase
    {
        try
        {
            return WitSqlTrees.SameIgnoringPositions(original, reparse()) ? text : null;
        }
        catch
        {
            // It does not re-parse, so it is certainly not a faithful rendering.
            return null;
        }
    }

    #endregion
}
