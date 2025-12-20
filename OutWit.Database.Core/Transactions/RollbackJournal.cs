using OutWit.Database.Core.Interfaces;
using System.Buffers.Binary;

namespace OutWit.Database.Core.Transactions
{
    /// <summary>
    /// Rollback Journal implementation.
    /// Original values are saved BEFORE modification.
    /// On rollback, original values are restored.
    /// On commit, journal is deleted.
    /// Supports optional encryption via IBlockEncryptor.
    /// </summary>
    public sealed class RollbackJournal : ITransactionJournal
    {
        #region Constants

        internal const uint MAGIC = 0x524F4C4A; // "ROLJ"
        internal const uint MAGIC_ENCRYPTED = 0x524A4345; // "RJCE" - encrypted rollback journal
        private const int HEADER_SIZE = 4; // Magic only (each file has its own header)

        /// <summary>
        /// Provider key for rollback journal.
        /// </summary>
        public const string PROVIDER_KEY = "rollback";

        #endregion

        #region Fields

        private readonly string m_basePath;
        private readonly IBlockEncryptor? m_encryptor;
        private readonly bool m_isEncrypted;
        private readonly object m_writeLock = new();
        private readonly Dictionary<long, TransactionJournalFile> m_activeJournals = new();
        private bool m_disposed;

        #endregion

        #region Functions

        /// <summary>
        /// Creates a rollback journal manager for the specified directory.
        /// </summary>
        /// <param name="basePath">Base path for journal files.</param>
        /// <param name="encryptor">Optional encryptor for encrypting entries.</param>
        public RollbackJournal(string basePath, IBlockEncryptor? encryptor = null)
        {
            m_basePath = basePath;
            m_encryptor = encryptor;
            m_isEncrypted = encryptor != null;
            Directory.CreateDirectory(Path.GetDirectoryName(basePath) ?? basePath);
        }

        /// <inheritdoc/>
        public void BeginTransaction(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                var journalPath = GetJournalPath(transactionId);
                var journal = new TransactionJournalFile(journalPath, transactionId, m_encryptor);
                m_activeJournals[transactionId] = journal;
            }
        }

        /// <inheritdoc/>
        public void LogPut(long transactionId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ReadOnlySpan<byte> oldValue)
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                if (m_activeJournals.TryGetValue(transactionId, out var journal))
                {
                    // For rollback journal, we save the OLD value so we can restore it
                    journal.WriteEntry(RollbackEntryType.Put, key, oldValue);
                }
            }
        }

        /// <inheritdoc/>
        public void LogDelete(long transactionId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> oldValue)
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                if (m_activeJournals.TryGetValue(transactionId, out var journal))
                {
                    // Save the old value that was deleted (for restoration)
                    journal.WriteEntry(RollbackEntryType.Delete, key, oldValue);
                }
            }
        }

        /// <inheritdoc/>
        public void CommitTransaction(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                if (m_activeJournals.TryGetValue(transactionId, out var journal))
                {
                    m_activeJournals.Remove(transactionId);
                    journal.MarkCommitted();
                    journal.Dispose();

                    // Delete the journal file on successful commit
                    try { File.Delete(journal.FilePath); } catch { }
                }
            }
        }

        /// <inheritdoc/>
        public void RollbackTransaction(long transactionId)
        {
            ThrowIfDisposed();

            // Note: actual rollback is handled by the Transaction class
            // which reads back changes from its in-memory buffer
            lock (m_writeLock)
            {
                if (m_activeJournals.TryGetValue(transactionId, out var journal))
                {
                    m_activeJournals.Remove(transactionId);
                    journal.Dispose();

                    // Delete the journal file
                    try { File.Delete(journal.FilePath); } catch { }
                }
            }
        }

        /// <inheritdoc/>
        public void Sync()
        {
            ThrowIfDisposed();

            lock (m_writeLock)
            {
                foreach (var journal in m_activeJournals.Values)
                {
                    journal.Sync();
                }
            }
        }

        /// <inheritdoc/>
        public int Recover(IKeyValueStore store)
        {
            ThrowIfDisposed();

            int recoveredCount = 0;
            var pattern = Path.GetFileName(m_basePath) + "_*.rollback";
            var dir = Path.GetDirectoryName(m_basePath) ?? ".";

            foreach (var journalPath in Directory.GetFiles(dir, pattern))
            {
                try
                {
                    // Read and apply rollback entries (restore original values)
                    var entries = ReadJournalFile(journalPath);
                
                    foreach (var (type, key, oldValue) in entries)
                    {
                        switch (type)
                        {
                            case RollbackEntryType.Put:
                                // Restore original value (or delete if it was new)
                                if (oldValue.Length > 0)
                                    store.Put(key, oldValue);
                                else
                                    store.Delete(key);
                                recoveredCount++;
                                break;

                            case RollbackEntryType.Delete:
                                // Restore deleted value
                                if (oldValue.Length > 0)
                                    store.Put(key, oldValue);
                                recoveredCount++;
                                break;
                        }
                    }

                    // Delete processed journal
                    File.Delete(journalPath);
                }
                catch
                {
                    // Skip corrupted journals
                }
            }

            return recoveredCount;
        }

        /// <inheritdoc/>
        public void Checkpoint()
        {
            // For rollback journal, checkpoint just ensures no orphan journals exist
            ThrowIfDisposed();
        }

        private string GetJournalPath(long transactionId)
        {
            return $"{m_basePath}_{transactionId}.rollback";
        }

        private List<(RollbackEntryType Type, byte[] Key, byte[] OldValue)> ReadJournalFile(string path)
        {
            var entries = new List<(RollbackEntryType, byte[], byte[])>();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            // Read and validate header
            if (stream.Length < 13) return entries; // Magic(4) + TxId(8) + Committed(1)

            var magic = reader.ReadUInt32();
            bool fileIsEncrypted = magic == MAGIC_ENCRYPTED;
        
            if (magic != MAGIC && magic != MAGIC_ENCRYPTED) return entries;
        
            // Check encryption mismatch
            if (m_isEncrypted && !fileIsEncrypted) return entries;
            if (!m_isEncrypted && fileIsEncrypted) return entries;

            var txId = reader.ReadInt64();
            var committed = reader.ReadBoolean();

            // If committed, no need to rollback
            if (committed) return entries;

            long entryId = 0;
            while (stream.Position < stream.Length)
            {
                try
                {
                    if (m_isEncrypted)
                    {
                        // Read encrypted entry
                        var encLen = reader.ReadInt32();
                        if (encLen < 0 || encLen > 100 * 1024 * 1024) break;
                        var encrypted = reader.ReadBytes(encLen);
                    
                        var decrypted = m_encryptor!.Decrypt(encrypted, entryId++);
                        if (decrypted == null) break;

                        // Parse decrypted entry
                        if (decrypted.Length < 9) break;
                        var type = (RollbackEntryType)decrypted[0];
                        var keyLen = BinaryPrimitives.ReadInt32LittleEndian(decrypted.AsSpan(1));
                        if (keyLen < 0 || 5 + keyLen + 4 > decrypted.Length) break;
                        var key = decrypted.AsSpan(5, keyLen).ToArray();
                        var valueLen = BinaryPrimitives.ReadInt32LittleEndian(decrypted.AsSpan(5 + keyLen));
                        var value = decrypted.AsSpan(9 + keyLen, valueLen).ToArray();
                        entries.Add((type, key, value));
                    }
                    else
                    {
                        var type = (RollbackEntryType)reader.ReadByte();
                        var keyLen = reader.ReadInt32();
                        if (keyLen < 0 || keyLen > 1024 * 1024) break;
                        var key = reader.ReadBytes(keyLen);

                        var valueLen = reader.ReadInt32();
                        if (valueLen < 0 || valueLen > 100 * 1024 * 1024) break;
                        var value = reader.ReadBytes(valueLen);

                        entries.Add((type, key, value));
                    }
                }
                catch
                {
                    break;
                }
            }

            return entries;
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            lock (m_writeLock)
            {
                foreach (var journal in m_activeJournals.Values)
                {
                    journal.Dispose();
                }
                m_activeJournals.Clear();
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether this journal is encrypted.
        /// </summary>
        public bool IsEncrypted => m_isEncrypted;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion

    }

    /// <summary>
    /// Rollback journal entry types.
    /// </summary>
    internal enum RollbackEntryType : byte
    {
        Put = 1,
        Delete = 2
    }
}
