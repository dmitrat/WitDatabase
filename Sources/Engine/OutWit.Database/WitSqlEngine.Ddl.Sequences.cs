using OutWit.Database.Definitions;

namespace OutWit.Database;

/// <summary>
/// DDL (Data Definition Language) operations for sequences in WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Get Sequence

    /// <summary>
    /// Get a sequence by name.
    /// </summary>
    /// <param name="sequenceName">The sequence name.</param>
    /// <returns>The sequence definition or null if not found.</returns>
    public DefinitionSequence? GetSequence(string sequenceName)
    {
        return m_schema.GetSequence(sequenceName);
    }

    #endregion

    #region Create Sequence

    /// <summary>
    /// Create a sequence.
    /// </summary>
    /// <param name="name">The sequence name.</param>
    /// <param name="startWith">The starting value for the sequence.</param>
    public void CreateSequence(string name, long startWith)
    {
        m_schema.CreateSequence(new DefinitionSequence
        {
            Name = name,
            StartWith = startWith
        });
    }

    #endregion

    #region Drop Sequence

    /// <summary>
    /// Drop a sequence.
    /// </summary>
    /// <param name="name">The sequence name to drop.</param>
    public void DropSequence(string name)
    {
        m_schema.DropSequence(name);
    }

    #endregion

    #region Sequence Values

    /// <summary>
    /// Get next value from sequence (and increment it).
    /// </summary>
    /// <param name="sequenceName">The sequence name.</param>
    /// <returns>The next sequence value.</returns>
    public long NextVal(string sequenceName)
    {
        return m_schema.NextVal(sequenceName);
    }

    /// <summary>
    /// Get current value of sequence (without incrementing).
    /// </summary>
    /// <param name="sequenceName">The sequence name.</param>
    /// <returns>The current sequence value.</returns>
    public long CurrVal(string sequenceName)
    {
        return m_schema.CurrVal(sequenceName);
    }

    /// <summary>
    /// Restart sequence with a new value.
    /// </summary>
    /// <param name="name">The sequence name.</param>
    /// <param name="restartWith">The new starting value (or null to restart from original start).</param>
    public void RestartSequence(string name, long? restartWith)
    {
        m_schema.RestartSequence(name, restartWith);
    }

    #endregion
}
