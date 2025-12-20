using System.Text;
using OutWit.Common.Json;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Sequences management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Sequences

    /// <summary>
    /// Gets a sequence by name.
    /// </summary>
    public DefinitionSequence? GetSequence(string name)
    {
        return m_sequences.TryGetValue(name, out var seq) ? seq : null;
    }

    /// <summary>
    /// Creates a new sequence.
    /// </summary>
    public void CreateSequence(DefinitionSequence sequence)
    {
        if (m_sequences.ContainsKey(sequence.Name))
            throw new InvalidOperationException($"Sequence '{sequence.Name}' already exists");
        
        // Initialize CurrentValue to one less than StartWith so first NextVal returns StartWith
        var seqWithInit = new DefinitionSequence
        {
            Name = sequence.Name,
            StartWith = sequence.StartWith,
            IncrementBy = sequence.IncrementBy,
            CurrentValue = sequence.StartWith - sequence.IncrementBy,
            MinValue = sequence.MinValue,
            MaxValue = sequence.MaxValue,
            Cycle = sequence.Cycle
        };
        m_sequences[sequence.Name] = seqWithInit;
        SaveSequences();
    }

    /// <summary>
    /// Drops a sequence.
    /// </summary>
    public void DropSequence(string name)
    {
        if (!m_sequences.Remove(name))
            throw new InvalidOperationException($"Sequence '{name}' not found");
        SaveSequences();
    }

    /// <summary>
    /// Gets the next value from a sequence.
    /// </summary>
    public long NextVal(string sequenceName)
    {
        if (!m_sequences.TryGetValue(sequenceName, out var seq))
            throw new InvalidOperationException($"Sequence '{sequenceName}' not found");
        
        seq.CurrentValue += seq.IncrementBy;
        SaveSequences();
        return seq.CurrentValue;
    }

    /// <summary>
    /// Gets the current value of a sequence.
    /// </summary>
    public long CurrVal(string sequenceName)
    {
        if (!m_sequences.TryGetValue(sequenceName, out var seq))
            throw new InvalidOperationException($"Sequence '{sequenceName}' not found");
        
        return seq.CurrentValue;
    }

    /// <summary>
    /// Restarts a sequence.
    /// </summary>
    public void RestartSequence(string sequenceName, long? restartWith = null)
    {
        if (!m_sequences.TryGetValue(sequenceName, out var seq))
            throw new InvalidOperationException($"Sequence '{sequenceName}' not found");
        
        var newValue = restartWith ?? seq.StartWith;
        seq.CurrentValue = newValue - seq.IncrementBy;
        SaveSequences();
    }

    private void SaveSequences()
    {
        var sequences = m_sequences.Values.ToList();
        m_store.Put(Encoding.UTF8.GetBytes(SEQUENCES_KEY).AsSpan(), sequences.ToJsonBytes());
    }

    private void LoadSequences()
    {
        var data = m_store.Get(Encoding.UTF8.GetBytes(SEQUENCES_KEY).AsSpan());
        if (data == null || data.Length == 0) 
            return;

        var sequences = data.FromJsonBytes<List<DefinitionSequence>>();
        if (sequences == null) 
            return;

        foreach (var sequence in sequences)
        {
            m_sequences[sequence.Name] = sequence;
        }
    }

    #endregion
}
