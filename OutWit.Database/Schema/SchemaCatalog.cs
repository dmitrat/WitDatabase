using System.Text;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Manages database schema (tables, indexes, views, triggers, sequences) stored in the key-value store.
/// Uses special keys with "$schema:" prefix for metadata storage.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Constants

    private const string SCHEMA_PREFIX = "$schema:";
    private const string TABLES_KEY = "$schema:_tables";
    private const string INDEXES_KEY = "$schema:_indexes";
    private const string VIEWS_KEY = "$schema:_views";
    private const string TRIGGERS_KEY = "$schema:_triggers";
    private const string SEQUENCES_KEY = "$schema:_sequences";
    private const string ROWID_KEY = "$schema:_rowid";

    #endregion

    #region Fields

    private readonly IKeyValueStore m_store;
    private readonly Dictionary<string, DefinitionTable> m_tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionIndex> m_indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionView> m_views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionTrigger> m_triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DefinitionSequence> m_sequences = new(StringComparer.OrdinalIgnoreCase);
    private long m_nextRowId = 0;

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

    #region Properties

    /// <summary>
    /// Gets all table names.
    /// </summary>
    public IEnumerable<string> TableNames => m_tables.Keys;

    /// <summary>
    /// Gets all tables.
    /// </summary>
    public IEnumerable<DefinitionTable> Tables => m_tables.Values;

    #endregion

    #region Row ID Management

    /// <summary>
    /// Gets the next row ID for a table.
    /// </summary>
    public long GetNextRowId(string tableName)
    {
        // In a real implementation, this would be per-table
        return Interlocked.Increment(ref m_nextRowId);
    }

    /// <summary>
    /// Gets the key prefix for storing table data.
    /// </summary>
    public static byte[] GetTableDataPrefix(string tableName)
    {
        return Encoding.UTF8.GetBytes($"t:{tableName}:");
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
}
