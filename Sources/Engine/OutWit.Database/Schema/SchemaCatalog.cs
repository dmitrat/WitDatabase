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
    private const string ROWID_PREFIX = "$schema:_rowid:";
    private const string ROWVERSION_KEY = "$schema:_rowversion";

    public const string INFORMATION_SCHEMA_NAME = "INFORMATION_SCHEMA";

    // Pre-computed UTF8 bytes for frequently used keys
    private static readonly byte[] TABLES_KEY_BYTES = Encoding.UTF8.GetBytes(TABLES_KEY);
    private static readonly byte[] INDEXES_KEY_BYTES = Encoding.UTF8.GetBytes(INDEXES_KEY);
    private static readonly byte[] VIEWS_KEY_BYTES = Encoding.UTF8.GetBytes(VIEWS_KEY);
    private static readonly byte[] TRIGGERS_KEY_BYTES = Encoding.UTF8.GetBytes(TRIGGERS_KEY);
    private static readonly byte[] SEQUENCES_KEY_BYTES = Encoding.UTF8.GetBytes(SEQUENCES_KEY);
    private static readonly byte[] ROWID_PREFIX_BYTES = Encoding.UTF8.GetBytes(ROWID_PREFIX);
    private static readonly byte[] ROWVERSION_KEY_BYTES = Encoding.UTF8.GetBytes(ROWVERSION_KEY);

    #endregion

    #region Fields

    private readonly IKeyValueStore m_store;
    private readonly ReaderWriterLockSlim m_lock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, DefinitionTable> m_tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionIndex> m_indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionView> m_views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionTrigger> m_triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionSequence> m_sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> m_tableRowIds = new(StringComparer.OrdinalIgnoreCase);
    private ulong m_globalRowVersion;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new SchemaCatalog backed by the specified store.
    /// </summary>
    public SchemaCatalog(IKeyValueStore store)
    {
        m_store = store;
        LoadSchema();
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

    private void SaveTableRowId(string tableName, long rowId, ITransaction? transaction)
    {
        // Build key: "$schema:_rowid:{tableName}"
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        Span<byte> rowIdBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(rowIdBytes, rowId);
        
        if (transaction != null)
        {
            transaction.Put(keyBytes.AsSpan(), rowIdBytes);
        }
        else
        {
            m_store.Put(keyBytes.AsSpan(), rowIdBytes);
        }
    }

    private void LoadTableRowId(string tableName)
    {
        var tableNameBytes = Encoding.UTF8.GetBytes(tableName);
        var keyBytes = new byte[ROWID_PREFIX_BYTES.Length + tableNameBytes.Length];
        ROWID_PREFIX_BYTES.CopyTo(keyBytes, 0);
        tableNameBytes.CopyTo(keyBytes, ROWID_PREFIX_BYTES.Length);

        var rowIdData = m_store.Get(keyBytes.AsSpan());
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

        m_store.Delete(keyBytes.AsSpan());
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
        
        if (transaction != null)
        {
            transaction.Put(ROWVERSION_KEY_BYTES.AsSpan(), valueBytes);
        }
        else
        {
            m_store.Put(ROWVERSION_KEY_BYTES.AsSpan(), valueBytes);
        }
    }

    private void LoadRowVersion()
    {
        var data = m_store.Get(ROWVERSION_KEY_BYTES.AsSpan());
        if (data != null && data.Length == 8)
        {
            m_globalRowVersion = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data);
        }
    }

    #endregion
}
