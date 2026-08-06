using System.Text.RegularExpressions;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// One value bound to a statement by name.
/// </summary>
public sealed record SqlParameter(string Name, object? Value);

/// <summary>
/// A statement and the values it binds - what Studio sends to the engine when it writes.
///
/// Studio used to build every write by putting formatted literals into the text
/// (<c>SqlValueFormatter.FormatForSql</c>). That is wrong in three ways, in rising order of
/// seriousness: a value the formatter has no case for falls through to <c>ToString()</c> and lands in
/// the statement unquoted; a DateTime is written to whole seconds, so the value read back is not the
/// value stored; and a value the user typed becomes part of the statement's syntax, where a quote
/// breaks it and a semicolon runs something else. Parameters remove all three - the value never
/// passes through the language.
///
/// <see cref="SqlValueFormatter"/> keeps its job on the other side: showing a person the SQL for what
/// they are about to do (WS-32), where a literal is exactly what should be shown.
/// </summary>
/// <param name="ExpectedRows">
/// How many rows this statement must affect, or null when it does not matter.
///
/// This is the whole mechanism behind WS-37, and it is the only one available: the engine has no
/// optimistic concurrency of its own. A statement that names its row by primary key AND by the values
/// it was read with affects one row if nothing has changed and - measured 2026-08-06 - <b>zero</b> if
/// somebody else got there first. The count is the difference between "applied" and "applied to a row
/// that is no longer the one the user was looking at".
/// </param>
public sealed partial record SqlStatement(
    string Text,
    IReadOnlyList<SqlParameter> Parameters,
    int? ExpectedRows = null)
{
    #region Constants

    [GeneratedRegex(@"@[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ParameterToken { get; }

    #endregion

    #region Creation

    /// <summary>
    /// A statement with nothing to bind - DDL, or SQL a person typed.
    /// </summary>
    public static SqlStatement Of(string text) => new(text, []);

    #endregion

    #region Functions

    /// <summary>
    /// The same statement written out with its values as literals, for showing a person what will run
    /// (WS-32). NEVER for execution: this is the string form the parameters exist to avoid.
    /// </summary>
    public string ToDisplaySql()
    {
        if (Parameters.Count == 0)
            return Text;

        var byName = Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        return ParameterToken.Replace(Text, match =>
            byName.TryGetValue(match.Value, out var value)
                ? SqlValueFormatter.FormatForSql(value)
                : match.Value);
    }

    public override string ToString() => ToDisplaySql();

    #endregion
}
