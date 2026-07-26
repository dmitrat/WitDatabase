using System.Text;
using OutWit.Common.MemoryPack;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Sequences management part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants (Sequences)

    private const string SEQUENCE_PREFIX = "$schema:seq:";
    private static readonly byte[] SEQUENCE_PREFIX_BYTES = Encoding.UTF8.GetBytes(SEQUENCE_PREFIX);

    #endregion

    #region Sequences

    /// <summary>
    /// Gets a sequence by name.
    /// </summary>
    public DefinitionSequence? GetSequence(string name)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_sequences.TryGetValue(name, out var seq) ? seq : null;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a new sequence.
    /// </summary>
    public void CreateSequence(DefinitionSequence sequence)
    {
        m_lock.EnterWriteLock();
        try
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
            SaveSequence(seqWithInit);
            SaveSequencesList();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a sequence.
    /// </summary>
    public void DropSequence(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_sequences.Remove(name))
                throw new InvalidOperationException($"Sequence '{name}' not found");
            
            DeleteSequence(name);
            SaveSequencesList();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the next value from a sequence.
    /// Respects MinValue, MaxValue, and Cycle settings.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when sequence not found or when sequence exceeds bounds without Cycle enabled.
    /// </exception>
    public long NextVal(string sequenceName)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_sequences.TryGetValue(sequenceName, out var seq))
                throw new InvalidOperationException($"Sequence '{sequenceName}' not found");

            long nextValue = seq.CurrentValue + seq.IncrementBy;

            // Check bounds for ascending sequence (positive increment)
            if (seq.IncrementBy > 0)
            {
                if (seq.MaxValue.HasValue && nextValue > seq.MaxValue.Value)
                {
                    if (seq.Cycle)
                        nextValue = seq.MinValue ?? seq.StartWith;
                    else
                        throw new InvalidOperationException($"Sequence '{sequenceName}' exceeded maximum value {seq.MaxValue.Value}");
                }
            }
            // Check bounds for descending sequence (negative increment)
            else if (seq.IncrementBy < 0)
            {
                if (seq.MinValue.HasValue && nextValue < seq.MinValue.Value)
                {
                    if (seq.Cycle)
                        nextValue = seq.MaxValue ?? seq.StartWith;
                    else
                        throw new InvalidOperationException($"Sequence '{sequenceName}' went below minimum value {seq.MinValue.Value}");
                }
            }

            seq.CurrentValue = nextValue;
            SaveSequenceCurrentValue(sequenceName, nextValue);
            return seq.CurrentValue;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the current value of a sequence.
    /// </summary>
    public long CurrVal(string sequenceName)
    {
        m_lock.EnterReadLock();
        try
        {
            if (!m_sequences.TryGetValue(sequenceName, out var seq))
                throw new InvalidOperationException($"Sequence '{sequenceName}' not found");

            return seq.CurrentValue;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Restarts a sequence.
    /// </summary>
    public void RestartSequence(string sequenceName, long? restartWith = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_sequences.TryGetValue(sequenceName, out var seq))
                throw new InvalidOperationException($"Sequence '{sequenceName}' not found");

            var newValue = restartWith ?? seq.StartWith;
            seq.CurrentValue = newValue - seq.IncrementBy;
            SaveSequenceCurrentValue(sequenceName, seq.CurrentValue);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Saves only the current value of a sequence (optimized for NextVal).
    /// Key: "$schema:seq:{name}:val" = 8 bytes (long)
    /// </summary>
    private void SaveSequenceCurrentValue(string name, long currentValue)
    {
        var key = $"{SEQUENCE_PREFIX}{name}:val";
        Span<byte> valueBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(valueBytes, currentValue);
        m_store.Put(Encoding.UTF8.GetBytes(key).AsSpan(), valueBytes);
    }

    /// <summary>
    /// Saves full sequence definition.
    /// Key: "$schema:seq:{name}:def" = JSON
    /// </summary>
    private void SaveSequence(DefinitionSequence sequence)
    {
        var key = $"{SEQUENCE_PREFIX}{sequence.Name}:def";
        m_store.Put(Encoding.UTF8.GetBytes(key).AsSpan(), sequence.ToMemoryPackBytes());
    }

    /// <summary>
    /// Deletes a sequence from storage.
    /// </summary>
    private void DeleteSequence(string name)
    {
        var defKey = $"{SEQUENCE_PREFIX}{name}:def";
        var valKey = $"{SEQUENCE_PREFIX}{name}:val";
        m_store.Delete(Encoding.UTF8.GetBytes(defKey).AsSpan());
        m_store.Delete(Encoding.UTF8.GetBytes(valKey).AsSpan());
    }

    /// <summary>
    /// Saves the list of sequence names (for enumeration on load).
    /// </summary>
    private void SaveSequencesList()
    {
        var names = m_sequences.Keys.ToList();
        m_store.Put(SEQUENCES_KEY_BYTES.AsSpan(), names.ToMemoryPackBytes());
    }

    private void LoadSequences()
    {
        // Load list of sequence names
        var namesData = m_store.Get(SEQUENCES_KEY_BYTES.AsSpan());
        if (namesData == null || namesData.Length == 0)
            return;

        var names = ReadSchemaRecord<List<string>>(namesData, "sequences");

        // Load each sequence
        foreach (var name in names)
        {
            var defKey = $"{SEQUENCE_PREFIX}{name}:def";
            var defData = m_store.Get(Encoding.UTF8.GetBytes(defKey).AsSpan());
            if (defData == null)
                continue;

            var sequence = ReadSchemaRecord<DefinitionSequence>(defData, $"sequence '{name}'");

            // Load current value separately (may have been updated)
            var valKey = $"{SEQUENCE_PREFIX}{name}:val";
            var valData = m_store.Get(Encoding.UTF8.GetBytes(valKey).AsSpan());
            if (valData != null && valData.Length == 8)
            {
                sequence.CurrentValue = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(valData);
            }

            m_sequences[sequence.Name] = sequence;
        }
    }

    #endregion
}
