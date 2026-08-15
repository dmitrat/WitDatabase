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

            // Two ways the old one-liner went wrong, and only the first was reported.
            // `Directory.CreateDirectory(Path.GetDirectoryName(basePath) ?? basePath)`:
            // for a bare relative name GetDirectoryName returns the EMPTY STRING rather than null,
            // so the `??` never fired and CreateDirectory("") threw. And when it does return null -
            // a path at a root - the fallback created a *directory* named after the journal file,
            // which is worse than doing nothing.
            var directory = Path.GetDirectoryName(basePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
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

        /// <summary>
        /// Applies what the journals hold, and REPORTS what it could not apply rather than deleting
        /// the evidence.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This used to be <c>try { … } catch { }</c> per file, under the comment "Skip corrupted
        /// journals", with the file deleted at the end of the successful path only - so a journal
        /// that threw half way left the database carrying half a transaction and told nobody, and one
        /// whose ENTRIES stopped reading part way through looked exactly like one that ended there.
        /// </para>
        /// <para>
        /// The intent is kept: one bad file does not stop a database opening. What changes is that
        /// the failure reaches <see cref="RecoveryFailures"/> and the file survives.
        /// </para>
        /// </remarks>
        public int Recover(IKeyValueStore store)
        {
            ThrowIfDisposed();

            int recoveredCount = 0;
            var failures = new List<JournalRecoveryFailure>();
            var pattern = Path.GetFileName(m_basePath) + "_*.rollback";
            var dir = Path.GetDirectoryName(m_basePath) ?? ".";

            foreach (var journalPath in Directory.GetFiles(dir, pattern))
            {
                try
                {
                    // Read and apply rollback entries (restore original values)
                    var entries = ReadJournalFile(journalPath, out var readToTheEnd);

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

                    if (readToTheEnd)
                    {
                        // Applied in full, so the file has nothing left to say.
                        File.Delete(journalPath);
                    }
                    else
                    {
                        // The prefix went in; the rest is still in the file, which is why the file
                        // stays. A torn tail is what an interrupted write leaves, and it is not the
                        // same thing as a journal that ended there.
                        failures.Add(new JournalRecoveryFailure(journalPath,
                            "the journal stopped reading before its end, so the entries after that "
                            + "point were not applied. The file is kept."));
                    }
                }
                catch (Exception ex)
                {
                    // One unreadable journal must not stop the database opening - that part of the
                    // old behaviour is deliberate and kept. What is new is that it is reported, and
                    // that the file is not deleted: it is the only remaining evidence of whatever
                    // happened, and something has to be able to look at it.
                    failures.Add(new JournalRecoveryFailure(journalPath,
                        $"{ex.GetType().Name}: {ex.Message}"));
                }
            }

            RecoveryFailures = failures;

            return recoveredCount;
        }

        /// <inheritdoc/>
        public IReadOnlyList<JournalRecoveryFailure> RecoveryFailures { get; private set; } = [];

        /// <summary>
        /// A rollback journal has nothing to checkpoint.
        /// </summary>
        /// <remarks>
        /// The comment here used to say it "ensures no orphan journals exist", which it never did -
        /// and must not: since recovery keeps the files it could not apply, deleting orphans would
        /// throw away exactly the evidence that was just deliberately preserved. A journal that WAS
        /// applied is already gone, removed by <see cref="Recover"/>.
        /// </remarks>
        public void Checkpoint()
        {
            ThrowIfDisposed();
        }

        private string GetJournalPath(long transactionId)
        {
            return $"{m_basePath}_{transactionId}.rollback";
        }

        /// <summary>
        /// Reads a journal's entries, and says whether it reached the end of the file.
        /// </summary>
        /// <param name="path">The journal.</param>
        /// <param name="readToTheEnd">
        /// False when reading stopped early - a torn tail, a damaged entry, an encryption mismatch,
        /// or a header this build does not recognise. The caller keeps such a file and reports it;
        /// without this flag "stopped at the damage" and "ended here" were the same answer.
        /// </param>
        private List<(RollbackEntryType Type, byte[] Key, byte[] OldValue)> ReadJournalFile(
            string path, out bool readToTheEnd)
        {
            var entries = new List<(RollbackEntryType, byte[], byte[])>();
            readToTheEnd = false;

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

            // If committed, no need to rollback. Nothing is left to apply, so the file has been read
            // for everything it is worth.
            if (committed)
            {
                readToTheEnd = true;
                return entries;
            }

            long entryId = 0;

            // Set at every exit that is not "the file ended". It cannot be inferred from the position:
            // BinaryReader.ReadBytes on a truncated entry returns a SHORT array and leaves the stream
            // at its end, so a torn tail looks exactly like a clean end to anything counting bytes.
            // That is how a damaged journal came to be deleted as though it had been applied in full.
            var damaged = false;

            while (stream.Position < stream.Length)
            {
                try
                {
                    if (m_isEncrypted)
                    {
                        // Read encrypted entry
                        var encLen = reader.ReadInt32();
                        if (encLen < 0 || encLen > 100 * 1024 * 1024) { damaged = true; break; }
                        var encrypted = reader.ReadBytes(encLen);

                        if (encrypted.Length != encLen) { damaged = true; break; }

                        var decrypted = m_encryptor!.Decrypt(encrypted, entryId++);
                        if (decrypted == null) { damaged = true; break; }

                        // Parse decrypted entry
                        if (decrypted.Length < 9) { damaged = true; break; }
                        var type = (RollbackEntryType)decrypted[0];
                        var keyLen = BinaryPrimitives.ReadInt32LittleEndian(decrypted.AsSpan(1));
                        if (keyLen < 0 || 5 + keyLen + 4 > decrypted.Length) { damaged = true; break; }
                        var key = decrypted.AsSpan(5, keyLen).ToArray();
                        var valueLen = BinaryPrimitives.ReadInt32LittleEndian(decrypted.AsSpan(5 + keyLen));
                        var value = decrypted.AsSpan(9 + keyLen, valueLen).ToArray();
                        entries.Add((type, key, value));
                    }
                    else
                    {
                        var type = (RollbackEntryType)reader.ReadByte();
                        var keyLen = reader.ReadInt32();
                        if (keyLen < 0 || keyLen > 1024 * 1024) { damaged = true; break; }
                        var key = reader.ReadBytes(keyLen);

                        if (key.Length != keyLen) { damaged = true; break; }

                        var valueLen = reader.ReadInt32();
                        if (valueLen < 0 || valueLen > 100 * 1024 * 1024) { damaged = true; break; }
                        var value = reader.ReadBytes(valueLen);

                        if (value.Length != valueLen) { damaged = true; break; }

                        entries.Add((type, key, value));
                    }
                }
                catch
                {
                    damaged = true;
                    break;
                }
            }

            readToTheEnd = !damaged;

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
