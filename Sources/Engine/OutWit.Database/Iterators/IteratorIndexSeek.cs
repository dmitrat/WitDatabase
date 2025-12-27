using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for performing index seek (equality lookup) operations.
/// Reads rows from a table that match a specific index key value.
/// </summary>
internal sealed class IteratorIndexSeek : IteratorBase
{
    #region Fields

    private readonly ITransaction? m_transaction;
    private readonly IKeyValueStore m_store;
    private readonly ISecondaryIndex m_index;
    private readonly DefinitionTable m_table;
    private readonly DefinitionIndex m_indexDefinition;
    private readonly byte[] m_keyValue;
    private readonly byte[] m_tablePrefix;
    private IReadOnlyList<WitSqlColumnInfo>? m_schema;
    private IEnumerator<byte[]>? m_primaryKeyEnumerator;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new index seek iterator.
    /// </summary>
    /// <param name="transaction">The active transaction (if any).</param>
    /// <param name="store">The key-value store.</param>
    /// <param name="index">The secondary index to seek.</param>
    /// <param name="table">The table definition.</param>
    /// <param name="indexDefinition">The index definition.</param>
    /// <param name="keyValue">The serialized index key value to seek.</param>
    public IteratorIndexSeek(
        ITransaction? transaction,
        IKeyValueStore store,
        ISecondaryIndex index,
        DefinitionTable table,
        DefinitionIndex indexDefinition,
        byte[] keyValue)
    {
        m_transaction = transaction;
        m_store = store;
        m_index = index;
        m_table = table;
        m_indexDefinition = indexDefinition;
        m_keyValue = keyValue;
        m_tablePrefix = SchemaCatalog.GetTableDataPrefix(table.Name);
    }

    #endregion

    #region Functions

    private IReadOnlyList<WitSqlColumnInfo> BuildSchema()
    {
        var schema = new List<WitSqlColumnInfo>(m_table.Columns.Count + 1);

        // Add _rowid as first column (hidden but accessible)
        schema.Add(new WitSqlColumnInfo
        {
            Name = "_rowid",
            Type = WitSqlType.Integer,
            IsNullable = false,
            TableName = m_table.Name
        });

        // Add table columns
        foreach (var col in m_table.Columns)
        {
            schema.Add(new WitSqlColumnInfo
            {
                Name = col.Name,
                Type = WitTypeConverter.ToSqlType(col.Type),
                IsNullable = col.Nullable,
                TableName = m_table.Name
            });
        }

        return schema;
    }

    private WitSqlRow? FetchRowByPrimaryKey(byte[] primaryKey)
    {
        // Build full key: table prefix + primary key
        var fullKey = new byte[m_tablePrefix.Length + primaryKey.Length];
        m_tablePrefix.CopyTo(fullKey, 0);
        primaryKey.CopyTo(fullKey, m_tablePrefix.Length);

        // Get value from store or transaction
        byte[]? value;
        if (m_transaction != null)
        {
            value = m_transaction.Get(fullKey);
        }
        else
        {
            value = m_store.Get(fullKey);
        }

        if (value == null)
            return null;

        // Parse row ID from primary key (it's the last 8 bytes in BigEndian)
        var rowId = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(primaryKey.AsSpan(primaryKey.Length - 8));

        // Deserialize row and prepend _rowid
        var dataRow = m_table.DeserializeRow(value);

        // Create new row with _rowid as first column
        var values = new WitSqlValue[dataRow.ColumnCount + 1];
        var names = new string[dataRow.ColumnCount + 1];

        values[0] = WitSqlValue.FromInt(rowId);
        names[0] = "_rowid";

        for (int i = 0; i < dataRow.ColumnCount; i++)
        {
            values[i + 1] = dataRow[i];
            names[i + 1] = dataRow.ColumnNames[i];
        }

        return new WitSqlRow(values, names);
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();

        // Find all primary keys matching the index key
        var primaryKeys = m_index.Find(m_keyValue);
        m_primaryKeyEnumerator = primaryKeys.GetEnumerator();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        while (m_primaryKeyEnumerator != null && m_primaryKeyEnumerator.MoveNext())
        {
            var primaryKey = m_primaryKeyEnumerator.Current;
            var row = FetchRowByPrimaryKey(primaryKey);
            
            if (row != null)
            {
                m_current = row.Value;
                return true;
            }
            // Row might have been deleted but index not yet cleaned up - skip it
        }

        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_primaryKeyEnumerator?.Dispose();
        m_primaryKeyEnumerator = null;
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_primaryKeyEnumerator?.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema ??= BuildSchema();

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    /// <inheritdoc/>
    public override long EstimatedRowCount
    {
        get
        {
            // For unique index, at most 1 row
            if (m_indexDefinition.IsUnique)
                return 1;
            
            // For non-unique, we don't know without scanning
            return -1;
        }
    }

    #endregion
}
