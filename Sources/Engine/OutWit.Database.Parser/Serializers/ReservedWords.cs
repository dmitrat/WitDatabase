using System.Collections.Concurrent;

namespace OutWit.Database.Parser.Serializers;

/// <summary>
/// Whether an identifier has to be quoted, answered by the grammar rather than by a list.
/// </summary>
/// <remarks>
/// <para>
/// There used to be a hand-written set of 68 words here. The grammar reserves <b>170</b>, so 103
/// were missing - <c>USING</c>, <c>WITH</c>, <c>ROW</c>, <c>COLUMN</c>, <c>CROSS</c>,
/// <c>INTERVAL</c>, <c>PARTITION</c> and 96 more were emitted unquoted and then failed to re-parse.
/// The drift ran the other way too: <c>KEY</c> was on the list after the grammar had deliberately
/// made it usable as a column name, so it was being quoted for no reason.
/// </para>
/// <para>
/// A second list, however carefully generated, drifts again the first time the grammar gains a
/// keyword and nobody remembers this file. So there is no list. The question
/// <i>"must this identifier be quoted?"</i> has an exact operational meaning - <b>can the parser
/// read it as an identifier?</b> - and that is asked of the parser directly. The grammar cannot
/// disagree with itself.
/// </para>
/// <para>
/// Two positions are probed, not one: a word may be accepted as a column name and rejected as a
/// table name. Quoting is the conservative answer, so a word that fails in <i>either</i> position is
/// treated as reserved. Answers are memoised, so a given word is probed once per process.
/// </para>
/// </remarks>
public static class ReservedWords
{
    #region Fields

    private static readonly ConcurrentDictionary<string, bool> CACHE = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Functions

    /// <summary>
    /// Whether <paramref name="identifier"/> must be quoted to survive being written out and read
    /// back.
    /// </summary>
    public static bool NeedsQuoting(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return true;

        if (char.IsDigit(identifier[0]))
            return true;

        foreach (var character in identifier)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
                return true;
        }

        return IsReserved(identifier);
    }

    /// <summary>
    /// Whether the grammar refuses <paramref name="word"/> as an identifier in any position the
    /// serializer writes one.
    /// </summary>
    public static bool IsReserved(string word) => CACHE.GetOrAdd(word, Probe);

    #endregion

    #region Probing

    private static bool Probe(string word) =>
        !Parses($"SELECT {word} FROM T") || !Parses($"SELECT * FROM {word}");

    private static bool Parses(string sql)
    {
        try
        {
            return WitSql.Parse(sql).Count > 0;
        }
        catch
        {
            // Any refusal means the word cannot stand unquoted there, which is the only thing the
            // caller asked. Why it was refused is not this type's business.
            return false;
        }
    }

    #endregion
}
