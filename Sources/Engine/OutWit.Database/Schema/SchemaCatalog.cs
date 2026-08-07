using System.Text;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Manages database schema (tables, indexes, views, triggers, sequences) stored in the key-value store.
/// Uses special keys with "$schema:" prefix for metadata storage.
/// Thread-safe for concurrent read/write access.
/// </summary>
public sealed partial class SchemaCatalog : IDisposable
{
    #region Constants

    private const string SCHEMA_PREFIX = "$schema:";
    private const string TABLES_KEY = "$schema:_tables";
    private const string INDEXES_KEY = "$schema:_indexes";
    private const string VIEWS_KEY = "$schema:_views";
    private const string TRIGGERS_KEY = "$schema:_triggers";
    private const string SEQUENCES_KEY = "$schema:_sequences";
    private const string FUNCTIONS_KEY = "$schema:_functions";
    private const string PROCEDURES_KEY = "$schema:_procedures";
    private const string ROWID_PREFIX = "$schema:_rowid:";
    private const string ROWVERSION_KEY = "$schema:_rowversion";
    private const string ROWCOUNT_PREFIX = "$schema:_rowcount:";

    public const string INFORMATION_SCHEMA_NAME = "INFORMATION_SCHEMA";

    // Pre-computed UTF8 bytes for frequently used keys
    private static readonly byte[] TABLES_KEY_BYTES = Encoding.UTF8.GetBytes(TABLES_KEY);
    private static readonly byte[] INDEXES_KEY_BYTES = Encoding.UTF8.GetBytes(INDEXES_KEY);
    private static readonly byte[] VIEWS_KEY_BYTES = Encoding.UTF8.GetBytes(VIEWS_KEY);
    private static readonly byte[] TRIGGERS_KEY_BYTES = Encoding.UTF8.GetBytes(TRIGGERS_KEY);
    private static readonly byte[] SEQUENCES_KEY_BYTES = Encoding.UTF8.GetBytes(SEQUENCES_KEY);
    private static readonly byte[] FUNCTIONS_KEY_BYTES = Encoding.UTF8.GetBytes(FUNCTIONS_KEY);
    private static readonly byte[] PROCEDURES_KEY_BYTES = Encoding.UTF8.GetBytes(PROCEDURES_KEY);
    private static readonly byte[] ROWID_PREFIX_BYTES = Encoding.UTF8.GetBytes(ROWID_PREFIX);
    private static readonly byte[] ROWVERSION_KEY_BYTES = Encoding.UTF8.GetBytes(ROWVERSION_KEY);
    private static readonly byte[] ROWCOUNT_PREFIX_BYTES = Encoding.UTF8.GetBytes(ROWCOUNT_PREFIX);

    #endregion

    #region Fields

    private readonly IKeyValueStore m_store;
    private readonly AsyncLocal<ITransaction?> m_ambientTransaction = new();
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, DefinitionTable> m_tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionIndex> m_indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionView> m_views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionTrigger> m_triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionSequence> m_sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionFunction> m_functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionProcedure> m_procedures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> m_tableRowIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> m_tableRowCounts = new(StringComparer.OrdinalIgnoreCase);
    private ulong m_globalRowVersion;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new SchemaCatalog backed by the specified store.
    /// </summary>
    /// <param name="store">The key-value store holding the schema records.</param>
    public SchemaCatalog(IKeyValueStore store)
    {
        m_store = store;
        LoadSchema();
    }

    #endregion

    #region Transaction Routing

    /// <summary>
    /// The transaction the calling flow currently has open, or null for an autocommit caller.
    /// Schema writes are routed through it - see <see cref="PutSchemaRecord"/> for why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ambient, and per execution flow, for two measured reasons.</b> A catalog is a property of
    /// the <i>database</i> and is deliberately shared between sessions, while a transaction belongs
    /// to <i>one</i> session - so a single field on the catalog would let one session's DDL be
    /// written into another session's transaction. And under MVCC, which is the ADO and EF default,
    /// <c>BeginTransaction</c> takes no write lock at all, so two sessions really are inside
    /// transactions at the same time. This is the same shape as
    /// <c>System.Transactions.Transaction.Current</c>, and for the same reason.
    /// </para>
    /// <para>
    /// The limit, stated rather than discovered later: a caller that opens its transaction on one
    /// execution flow and runs DDL on an unrelated one is not covered - the value does not flow
    /// there, and such a caller falls back to the direct store write, which is what every caller did
    /// before. An <c>ITransaction</c> is not thread-safe and neither is a <c>DbConnection</c>, so
    /// that caller is already outside the contract.
    /// </para>
    /// </remarks>
    public ITransaction? AmbientTransaction
    {
        get => m_ambientTransaction.Value;
        set => m_ambientTransaction.Value = value;
    }

    /// <summary>
    /// Writes a schema record through the caller's open transaction when there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole of <c>AUDIT-2026-07.md</c> finding 19 sits behind this method. The catalog is built
    /// over the <c>TransactionalStore</c> itself, so <c>m_store.Put</c> is an <b>autocommit</b> write
    /// that asks for the database write lock. A transaction holds that lock for its lifetime and
    /// <c>DatabaseLock</c> refuses same-thread re-entry, so every schema write inside a transaction
    /// threw <c>LockRecursionException</c> - <b>after</b> the in-memory dictionary had already been
    /// changed, so the caller was told the statement failed while the change was permanent.
    /// </para>
    /// <para>
    /// Routing through the transaction fixes both halves at once: the write reaches the
    /// transaction's buffer rather than the lock, and a rollback discards it along with the rows it
    /// describes. It is the same treatment <c>SaveTableRowId</c> and <c>SaveTableRowCount</c> were
    /// given when the row counters had the same fault; the schema-blob writers were simply never
    /// included.
    /// </para>
    /// <para>
    /// Which transaction that is comes from <see cref="AmbientTransaction"/> rather than from a
    /// parameter, so that no schema writer can be added later that forgets to accept one - the way
    /// the blob writers were left out when the row counters were given theirs.
    /// </para>
    /// </remarks>
    private void PutSchemaRecord(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var transaction = m_ambientTransaction.Value;

        // The ambient transaction when the caller did not name one: a DML path passes the engine's
        // transaction explicitly and gets the same object either way, while a DDL path - which never
        // had a parameter to pass - would otherwise write straight to the store and ask for the
        // write lock its own transaction is holding.
        transaction ??= m_ambientTransaction.Value;

        if (transaction != null)
        {
            transaction.Put(key, value);
            return;
        }

        m_store.Put(key, value);
        MakeDurable();
    }

    /// <summary>
    /// Makes an autocommit schema write reach the disk, which nothing else does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>KnownIssues issue 10, the engine half.</b> A page reaches the disk either in a
    /// <c>Flush</c> - which writes the header - or on its own when the cache evicts it, which does
    /// not. So the header is only ever brought up to date by a flush, and the only thing that
    /// flushes is a commit. Every DML statement runs in a transaction and therefore commits; a DDL
    /// statement in autocommit reaches this method and used to commit nothing at all. Measured
    /// 2026-08-07 with the same workload three ways, each abandoned by <c>Environment.Exit(0)</c>:
    /// 400 inserts left the header counting 52 pages of 52; 240 DDL statements left it counting 1 of
    /// 591 and the database read back EMPTY; the same 240 inside an explicit transaction left 200 of
    /// 200 and read back whole.
    /// </para>
    /// <para>
    /// <b>A flush and not a transaction of our own</b>, though the measurement above was made with a
    /// transaction. The two produce the same durability - the commit's only contribution here is the
    /// flush at the end of it - and opening one would cost more than it buys: on the non-MVCC store a
    /// transaction takes the database write lock for its lifetime, so the documented out-of-contract
    /// caller (one that opens a transaction on one execution flow and runs DDL on another, where the
    /// <c>AsyncLocal</c> does not reach) would move from a <c>LockRecursionException</c> to a
    /// deadlock. A hang is worse than a throw.
    /// </para>
    /// <para>
    /// <b>What this does not fix:</b> a process that dies in the MIDDLE of a DDL statement can still
    /// leave a header of one vintage beside pages of another, because eviction happens while the
    /// statement runs. That window is identical for DML, and closing it needs a journal that can be
    /// replayed at open - which the MVCC store currently refuses to have.
    /// </para>
    /// </remarks>
    private void MakeDurable()
    {
        m_store.Flush();
    }

    /// <summary>
    /// Deletes a schema record through the caller's open transaction when there is one.
    /// </summary>
    private void DeleteSchemaRecord(ReadOnlySpan<byte> key)
    {
        var transaction = m_ambientTransaction.Value;

        if (transaction != null)
        {
            transaction.Delete(key);
            return;
        }

        m_store.Delete(key);
        MakeDurable();
    }

    /// <summary>
    /// Reads a schema record through the caller's open transaction when there is one.
    /// </summary>
    /// <remarks>
    /// A plain <c>m_store.Get</c> takes a <b>read</b> lock, which the same recursion guard refuses to
    /// a thread already holding the write lock - measured as
    /// <c>ALTER TABLE ADD COLUMN</c> failing with <i>"Cannot acquire read lock"</i> rather than the
    /// write-lock message its neighbours gave. Reading through the transaction also means a reload
    /// sees what the transaction itself has written.
    /// </remarks>
    private byte[]? GetSchemaRecord(ReadOnlySpan<byte> key)
    {
        var transaction = m_ambientTransaction.Value;

        return transaction == null
            ? m_store.Get(key)
            : transaction.Get(key);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the schema catalog and releases the lock.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_lock.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets all table names.
    /// </summary>
    public IEnumerable<string> TableNames
    {
        get
        {
            m_lock.EnterReadLock();
            try
            {
                return m_tables.Keys.ToList();
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets all tables.
    /// </summary>
    public IEnumerable<DefinitionTable> Tables
    {
        get
        {
            m_lock.EnterReadLock();
            try
            {
                return m_tables.Values.ToList();
            }
            finally
            {
                m_lock.ExitReadLock();
            }
        }
    }

    #endregion

    #region Row ID Management

    /// <summary>
    /// Gets the next row ID for a table.
    /// Each table maintains its own independent sequence.
    /// Note: For bulk inserts, use GetNextRowIdBatch for better performance.
    /// </summary>
    public long GetNextRowId(string tableName)
    {
        return GetNextRowId(tableName, transaction: null);
    }

    /// <summary>
    /// Gets the next row ID for a table, optionally using an active transaction.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="transaction">The active transaction (if any) to use for persisting the row ID.</param>
    /// <returns>The next row ID.</returns>
    public long GetNextRowId(string tableName, ITransaction? transaction)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            if (!m_tableRowIds.TryGetValue(tableName, out var currentId))
                currentId = 0;

            var nextId = currentId + 1;
            m_tableRowIds[tableName] = nextId;
            SaveTableRowId(tableName, nextId, transaction);

            return nextId;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Reserves a batch of row IDs for bulk insert operations.
    /// Returns the first ID in the batch; use IDs from firstId to firstId + count - 1.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="count">Number of IDs to reserve.</param>
    /// <returns>The first row ID in the reserved batch.</returns>
    public long GetNextRowIdBatch(string tableName, int count)
    {
        return GetNextRowIdBatch(tableName, count, transaction: null);
    }

    /// <summary>
    /// Reserves a batch of row IDs for bulk insert operations, optionally using an active transaction.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="count">Number of IDs to reserve.</param>
    /// <param name="transaction">The active transaction (if any).</param>
    /// <returns>The first row ID in the reserved batch.</returns>
    public long GetNextRowIdBatch(string tableName, int count, ITransaction? transaction)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive");

        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            if (!m_tableRowIds.TryGetValue(tableName, out var currentId))
                currentId = 0;

            var firstId = currentId + 1;
            var lastId = currentId + count;
            m_tableRowIds[tableName] = lastId;
            SaveTableRowId(tableName, lastId, transaction);

            return firstId;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the current max row ID for a table without incrementing.
    /// </summary>
    public long GetCurrentRowId(string tableName)
    {
        m_lock.EnterReadLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            return m_tableRowIds.TryGetValue(tableName, out var currentId) ? currentId : 0;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Resets the row ID counter for a table (e.g., after TRUNCATE).
    /// </summary>
    public void ResetRowId(string tableName, long startFrom = 0)
    {
        ResetRowId(tableName, startFrom, transaction: null);
    }

    /// <summary>
    /// Resets the row ID counter for a table, optionally using an active transaction.
    /// </summary>
    public void ResetRowId(string tableName, long startFrom, ITransaction? transaction)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            m_tableRowIds[tableName] = startFrom;
            SaveTableRowId(tableName, startFrom, transaction);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Ensures the row ID counter is at least the specified value.
    /// This is called when a row is inserted with an explicit ID value
    /// to prevent future auto-increment values from colliding.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="minValue">The minimum value the counter should be at.</param>
    /// <param name="transaction">The active transaction (if any).</param>
    public void EnsureRowIdAtLeast(string tableName, long minValue, ITransaction? transaction)
    {
        // Fast path: check with read lock first to avoid write lock overhead
        // in the common case where counter is already high enough
        m_lock.EnterReadLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table '{tableName}' not found");

            if (m_tableRowIds.TryGetValue(tableName, out var currentId) && minValue <= currentId)
            {
                // Counter is already >= minValue, no update needed
                return;
            }
        }
        finally
        {
            m_lock.ExitReadLock();
        }

        // Slow path: need to update - acquire write lock
        m_lock.EnterWriteLock();
        try
        {
            // Double-check after acquiring write lock (another thread may have updated)
            if (!m_tableRowIds.TryGetValue(tableName, out var currentId))
                currentId = 0;

            // Only update if the new value is greater than current
            if (minValue > currentId)
            {
                m_tableRowIds[tableName] = minValue;
                SaveTableRowId(tableName, minValue, transaction);
            }
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveTableRowId(string tableName, long rowId, ITransaction? transaction)
    {
        // Build key: "$schema:_rowid:{tableName}"
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        Span<byte> rowIdBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(rowIdBytes, rowId);
        
        // Written through the transaction when there is one, so the counter becomes durable in the
        // same atomic step as the rows it names. It used to update only the in-memory cache and
        // leave persistence to PersistRowIdsToStore() after the commit had already returned - which
        // put it outside the flush the commit performed, so a crash in that window left the rows on
        // the media with the counter behind them, and the next insert reused a live identity.
        //
        // Only this table's counter, and only when it is actually allocated from: persisting every
        // table's counter at commit time is what made each commit collide with the previous one on
        // the MVCC write set.
        //
        // Note it goes to the transaction's buffer, not to the store, so it does not reach for a
        // write lock the transactional store already holds.
        // The ambient transaction when the caller did not name one: a DML path passes the engine's
        // transaction explicitly and gets the same object either way, while a DDL path - which never
        // had a parameter to pass - would otherwise write straight to the store and ask for the
        // write lock its own transaction is holding.
        transaction ??= m_ambientTransaction.Value;

        if (transaction == null)
            m_store.Put(keyBytes.AsSpan(), rowIdBytes);
        else
            transaction.Put(keyBytes.AsSpan(), rowIdBytes);
    }

    private void LoadTableRowId(string tableName)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        var rowIdData = GetSchemaRecord(keyBytes.AsSpan());
        if (rowIdData != null && rowIdData.Length == 8)
        {
            m_tableRowIds[tableName] = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(rowIdData);
        }
    }

    private void DeleteTableRowId(string tableName)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        DeleteSchemaRecord(keyBytes.AsSpan());
        m_tableRowIds.Remove(tableName);
    }

    /// <summary>
    /// Gets the key prefix for storing table data.
    /// </summary>
    public static byte[] GetTableDataPrefix(string tableName)
    {
        return Encoding.UTF8.GetBytes($"t:{tableName}:");
    }

    /// <summary>
    /// Gets the end key prefix for scanning table data (exclusive).
    /// This is the prefix that comes immediately after all table rows.
    /// </summary>
    public static byte[] GetTableDataEndPrefix(string tableName)
    {
        // Use the same prefix but with the last byte incremented
        // This creates an exclusive upper bound for the scan
        var prefix = Encoding.UTF8.GetBytes($"t:{tableName}:");
        var endPrefix = new byte[prefix.Length];
        prefix.CopyTo(endPrefix, 0);
        
        // Increment the last byte to create an exclusive end key
        // Since ':' is 0x3A, incrementing gives 0x3B (';')
        endPrefix[^1]++;
        return endPrefix;
    }

    /// <summary>
    /// Creates a key for a table row.
    /// </summary>
    public static byte[] CreateRowKey(string tableName, long rowId)
    {
        var prefix = GetTableDataPrefix(tableName);
        var key = new byte[prefix.Length + 8];
        prefix.CopyTo(key, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(prefix.Length), rowId);
        return key;
    }

    /// <summary>
    /// Parses a row ID from a key.
    /// </summary>
    public static long ParseRowId(byte[] key, string tableName)
    {
        var prefix = GetTableDataPrefix(tableName);
        if (key.Length != prefix.Length + 8)
            throw new ArgumentException("Invalid key length");
        
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(prefix.Length));
    }

    #endregion

    #region RowVersion Management

    /// <summary>
    /// Gets the next global row version value.
    /// ROWVERSION is a database-wide auto-incrementing counter.
    /// </summary>
    /// <param name="transaction">The active transaction (if any) to use for persisting the value.</param>
    /// <returns>The next row version value.</returns>
    public ulong GetNextRowVersion(ITransaction? transaction = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            m_globalRowVersion++;
            SaveRowVersion(m_globalRowVersion, transaction);
            return m_globalRowVersion;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveRowVersion(ulong rowVersion, ITransaction? transaction)
    {
        Span<byte> valueBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(valueBytes, rowVersion);
        
        // Through the transaction when there is one - same reasoning as the row counts and the row-id
        // counters above, and it keeps all three consistent with each other.
        // The ambient transaction when the caller did not name one: a DML path passes the engine's
        // transaction explicitly and gets the same object either way, while a DDL path - which never
        // had a parameter to pass - would otherwise write straight to the store and ask for the
        // write lock its own transaction is holding.
        transaction ??= m_ambientTransaction.Value;

        if (transaction == null)
            m_store.Put(ROWVERSION_KEY_BYTES.AsSpan(), valueBytes);
        else
            transaction.Put(ROWVERSION_KEY_BYTES.AsSpan(), valueBytes);
    }
    
    /// <summary>
    /// Persists the global row version to the store.
    /// This should be called after a transaction commit.
    /// </summary>
    public void PersistRowVersionToStore()
    {
        // Take a snapshot under read lock
        ulong version;
        m_lock.EnterReadLock();
        try
        {
            version = m_globalRowVersion;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
        
        // Persist outside of lock
        Span<byte> valueBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(valueBytes, version);
        m_store.Put(ROWVERSION_KEY_BYTES.AsSpan(), valueBytes);
    }

    private void LoadRowVersion()
    {
        var data = GetSchemaRecord(ROWVERSION_KEY_BYTES.AsSpan());
        if (data != null && data.Length == 8)
        {
            m_globalRowVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data);
        }
    }

    #endregion

    #region Row Count Management

    /// <summary>
    /// Gets the current row count for a table.
    /// This is an O(1) operation using cached metadata.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>The row count, or -1 if the table doesn't exist or count is unknown.</returns>
    public long GetRowCount(string tableName)
    {
        m_lock.EnterReadLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return -1;

            return m_tableRowCounts.TryGetValue(tableName, out var count) ? count : 0;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Increments the row count for a table by the specified amount.
    /// Called after INSERT operations.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="delta">Amount to add (usually 1).</param>
    /// <param name="transaction">The active transaction (if any).</param>
    public void IncrementRowCount(string tableName, long delta = 1, ITransaction? transaction = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return;

            if (!m_tableRowCounts.TryGetValue(tableName, out var count))
                count = 0;

            var newCount = count + delta;
            m_tableRowCounts[tableName] = newCount;
            SaveTableRowCount(tableName, newCount, transaction);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Decrements the row count for a table by the specified amount.
    /// Called after DELETE operations.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="delta">Amount to subtract (usually 1).</param>
    /// <param name="transaction">The active transaction (if any).</param>
    public void DecrementRowCount(string tableName, long delta = 1, ITransaction? transaction = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return;

            if (!m_tableRowCounts.TryGetValue(tableName, out var count))
                count = 0;

            var newCount = Math.Max(0, count - delta);
            m_tableRowCounts[tableName] = newCount;
            SaveTableRowCount(tableName, newCount, transaction);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Resets the row count for a table (e.g., after TRUNCATE).
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="count">The new count (default 0).</param>
    /// <param name="transaction">The active transaction (if any).</param>
    public void ResetRowCount(string tableName, long count = 0, ITransaction? transaction = null)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return;

            m_tableRowCounts[tableName] = count;
            SaveTableRowCount(tableName, count, transaction);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adjusts the in-memory row count cache without persisting to store.
    /// Used for reverting row counts after ROLLBACK TO SAVEPOINT.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="delta">The adjustment (+/-).</param>
    public void AdjustRowCountCache(string tableName, long delta)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return;

            if (!m_tableRowCounts.TryGetValue(tableName, out var count))
                count = 0;

            m_tableRowCounts[tableName] = Math.Max(0, count + delta);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Persists all in-memory row counts to the store.
    /// This should be called after a transaction commit to ensure
    /// the persisted metadata matches the in-memory cache.
    /// </summary>
    public void PersistRowCountsToStore()
    {
        // Take a snapshot under read lock to avoid holding lock during I/O
        KeyValuePair<string, long>[] snapshot;
        m_lock.EnterReadLock();
        try
        {
            snapshot = m_tableRowCounts.ToArray();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
        
        // Persist outside of lock
        foreach (var (tableName, count) in snapshot)
        {
            PersistRowCountInternal(tableName, count);
        }
    }

    /// <summary>
    /// Persists all in-memory row IDs to the store.
    /// This should be called after a transaction commit to ensure
    /// the persisted metadata matches the in-memory cache.
    /// </summary>
    public void PersistRowIdsToStore()
    {
        // Take a snapshot under read lock to avoid holding lock during I/O
        KeyValuePair<string, long>[] snapshot;
        m_lock.EnterReadLock();
        try
        {
            snapshot = m_tableRowIds.ToArray();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
        
        // Persist outside of lock
        foreach (var (tableName, rowId) in snapshot)
        {
            PersistRowIdInternal(tableName, rowId);
        }
    }
    
    private void PersistRowIdInternal(string tableName, long rowId)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        Span<byte> rowIdBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(rowIdBytes, rowId);
        m_store.Put(keyBytes.AsSpan(), rowIdBytes);
    }
    
    private void PersistRowCountInternal(string tableName, long count)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWCOUNT_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWCOUNT_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWCOUNT_PREFIX_BYTES.Length);

        Span<byte> countBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(countBytes, count);
        m_store.Put(keyBytes.AsSpan(), countBytes);
    }

    /// <summary>
    /// Reloads all row counts and row IDs from the store.
    /// This should be called after a transaction rollback to ensure
    /// the in-memory cache reflects the actual persisted state.
    /// </summary>
    public void ReloadMetadataFromStore()
    {
        m_lock.EnterWriteLock();
        try
        {
            foreach (var tableName in m_tables.Keys)
            {
                LoadTableRowCount(tableName);
                LoadTableRowId(tableName);
            }
            
            // Also reload global row version
            LoadRowVersion();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Recalculates and updates the row count for a table by scanning the actual data.
    /// This is used after ROLLBACK TO SAVEPOINT when the in-memory cache may be out of sync.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="transaction">The active transaction to use for scanning.</param>
    public void RecalculateRowCount(string tableName, ITransaction transaction)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_tables.ContainsKey(tableName))
                return;

            // Scan the table data through the transaction to count actual rows
            var prefix = GetTableDataPrefix(tableName);
            var endPrefix = GetTableDataEndPrefix(tableName);
            
            long count = 0;
            foreach (var _ in transaction.Scan(prefix, endPrefix))
            {
                count++;
            }

            // Update both the in-memory cache and persist to store (through transaction)
            m_tableRowCounts[tableName] = count;
            SaveTableRowCount(tableName, count, transaction);
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Recalculates row counts for all tables by scanning the actual data.
    /// </summary>
    /// <param name="transaction">The active transaction to use for scanning.</param>
    public void RecalculateAllRowCounts(ITransaction transaction)
    {
        m_lock.EnterWriteLock();
        try
        {
            foreach (var tableName in m_tables.Keys)
            {
                var prefix = GetTableDataPrefix(tableName);
                var endPrefix = GetTableDataEndPrefix(tableName);
                
                long count = 0;
                foreach (var _ in transaction.Scan(prefix, endPrefix))
                {
                    count++;
                }

                m_tableRowCounts[tableName] = count;
                SaveTableRowCount(tableName, count, transaction);
            }
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveTableRowCount(string tableName, long count, ITransaction? transaction)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWCOUNT_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWCOUNT_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWCOUNT_PREFIX_BYTES.Length);

        Span<byte> countBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(countBytes, count);
        
        // Through the transaction when there is one, for the same reason as the row-id counter: the
        // count has to become durable in the same atomic step as the rows it counts. Persisting it
        // after the commit left SELECT returning every row while COUNT(*) reported none.
        //
        // Writing it inside the transaction also makes the rollback case simpler rather than harder
        // than the old comment feared - a discarded transaction discards the count with it, instead
        // of needing the cache reloaded from the store afterwards.
        //
        // Repeated writes to the same key inside one transaction collapse to a single entry in its
        // buffer, so this costs one write-set entry per table touched, not one per row.
        // The ambient transaction when the caller did not name one: a DML path passes the engine's
        // transaction explicitly and gets the same object either way, while a DDL path - which never
        // had a parameter to pass - would otherwise write straight to the store and ask for the
        // write lock its own transaction is holding.
        transaction ??= m_ambientTransaction.Value;

        if (transaction == null)
            m_store.Put(keyBytes.AsSpan(), countBytes);
        else
            transaction.Put(keyBytes.AsSpan(), countBytes);
    }

    private void LoadTableRowCount(string tableName)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWCOUNT_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWCOUNT_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWCOUNT_PREFIX_BYTES.Length);

        var countData = GetSchemaRecord(keyBytes.AsSpan());
        if (countData != null && countData.Length == 8)
        {
            m_tableRowCounts[tableName] = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(countData);
        }
    }

    private void DeleteTableRowCount(string tableName)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWCOUNT_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWCOUNT_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWCOUNT_PREFIX_BYTES.Length);

        DeleteSchemaRecord(keyBytes.AsSpan());
        m_tableRowCounts.Remove(tableName);
    }

    #endregion
}
