using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for performing index range scan operations.
/// Reads rows from a table within a range of index key values.
/// </summary>
internal sealed class IteratorIndexRangeScan : IteratorBase
{
    #region Fields

    private readonly ITransaction? m_transaction;
    private readonly IKeyValueStore m_store;
    private readonly ISecondaryIndex m_index;
    private readonly DefinitionTable m_table;
    private readonly DefinitionIndex m_indexDefinition;
    private readonly byte[]? m_startKey;
    private readonly byte[]? m_endKey;
    private readonly bool m_startInclusive;
    private readonly bool m_endInclusive;
    private readonly byte[] m_tablePrefix;
    private IReadOnlyList<WitSqlColumnInfo>? m_schema;
    private IEnumerator<(byte[] IndexKey, byte[] PrimaryKey)>? m_rangeEnumerator;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new index range scan iterator.
    /// </summary>
    /// <param name="transaction">The active transaction (if any).</param>
    /// <param name="store">The key-value store.</param>
    /// <param name="index">The secondary index to scan.</param>
    /// <param name="table">The table definition.</param>
    /// <param name="indexDefinition">The index definition.</param>
    /// <param name="startKey">The start key (null for unbounded start).</param>
    /// <param name="startInclusive">Whether start key is inclusive.</param>
    /// <param name="endKey">The end key (null for unbounded end).</param>
    /// <param name="endInclusive">Whether end key is inclusive.</param>
    public IteratorIndexRangeScan(
        ITransaction? transaction,
        IKeyValueStore store,
        ISecondaryIndex index,
        DefinitionTable table,
        DefinitionIndex indexDefinition,
        byte[]? startKey,
        bool startInclusive,
        byte[]? endKey,
        bool endInclusive)
    {
        m_transaction = transaction;
        m_store = store;
        m_index = index;
        m_table = table;
        m_indexDefinition = indexDefinition;
        m_startKey = startKey;
        m_startInclusive = startInclusive;
        m_endKey = endKey;
        m_endInclusive = endInclusive;
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

    /// <summary>
    /// Compares two index keys.
    /// </summary>
    private static int CompareKeys(byte[] a, byte[] b)
    {
        var minLen = Math.Min(a.Length, b.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();

        // FindRange returns entries in range [startKey, endKey)
        // We need to handle inclusivity by adjusting keys or filtering
        var range = m_index.FindRange(m_startKey, m_endKey);
        m_rangeEnumerator = range.GetEnumerator();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        while (m_rangeEnumerator != null && m_rangeEnumerator.MoveNext())
        {
            var (indexKey, primaryKey) = m_rangeEnumerator.Current;

            // Check start inclusivity
            if (m_startKey != null && !m_startInclusive)
            {
                var cmp = CompareKeys(indexKey, m_startKey);
                if (cmp == 0)
                    continue; // Skip if equal and not inclusive
            }

            // Check end inclusivity
            // FindRange already uses exclusive end, so we need to include end if m_endInclusive
            // But ISecondaryIndex.FindRange may vary - let's be defensive
            if (m_endKey != null)
            {
                var cmp = CompareKeys(indexKey, m_endKey);
                if (cmp > 0 || (!m_endInclusive && cmp == 0))
                    break; // Past end
            }

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
        m_rangeEnumerator?.Dispose();
        m_rangeEnumerator = null;
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_rangeEnumerator?.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema ??= BuildSchema();

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    /// <inheritdoc/>
    public override long EstimatedRowCount => -1; // Unknown for range scan

    #endregion
}
