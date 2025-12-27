using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for performing full table scans.
/// Reads all rows from a table in storage order.
/// Supports transaction-aware scanning when a transaction is active.
/// </summary>
internal sealed class IteratorTableScan : IteratorBase
{
    #region Fields

    private readonly ITransaction? m_transaction;
    private readonly IKeyValueStore m_store;
    private readonly DefinitionTable m_table;
    private readonly ContextExecution? m_context;
    private readonly byte[] m_prefix;
    private IReadOnlyList<WitSqlColumnInfo>? m_schema;
    private IEnumerator<(byte[] Key, byte[] Value)>? m_enumerator;
    private WitSqlRow m_current;
    
    // Cached info about virtual computed columns
    private readonly List<(int Index, string Expression)>? m_virtualComputedColumns;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new table scan iterator.
    /// </summary>
    /// <param name="transaction">The active transaction (if any).</param>
    /// <param name="store">The key-value store to scan (used when no transaction is active).</param>
    /// <param name="table">The table definition.</param>
    /// <param name="context">The execution context (optional, for evaluating VIRTUAL computed columns).</param>
    public IteratorTableScan(ITransaction? transaction, IKeyValueStore store, DefinitionTable table, ContextExecution? context = null)
    {
        m_transaction = transaction;
        m_store = store;
        m_table = table;
        m_context = context;
        m_prefix = SchemaCatalog.GetTableDataPrefix(table.Name);
        
        // Cache virtual computed columns info
        m_virtualComputedColumns = null;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (col.IsComputed && !col.IsStored && !string.IsNullOrEmpty(col.ComputedExpression))
            {
                m_virtualComputedColumns ??= new List<(int, string)>();
                m_virtualComputedColumns.Add((i, col.ComputedExpression));
            }
        }
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

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();

        // Calculate end key (prefix + max value)
        var endKey = new byte[m_prefix.Length];
        m_prefix.CopyTo(endKey, 0);
        endKey[^1]++; // Increment last byte to get "next" prefix

        // Use transaction's Scan if available, otherwise use store directly
        IEnumerable<(byte[] Key, byte[] Value)> results;
        if (m_transaction != null)
        {
            // Transaction-aware scan - sees uncommitted changes
            results = m_transaction.Scan(m_prefix, endKey);
        }
        else
        {
            // Direct store scan - only sees committed data
            results = m_store.Scan(m_prefix, endKey);
        }

        m_enumerator = results.GetEnumerator();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_enumerator == null || !m_enumerator.MoveNext())
            return false;

        var (key, value) = m_enumerator.Current;

        // Parse row ID from key
        var rowId = SchemaCatalog.ParseRowId(key, m_table.Name);

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

        // Evaluate VIRTUAL computed columns on-the-fly
        if (m_virtualComputedColumns != null && m_context != null)
        {
            // Create a row without _rowid for expression evaluation (column indices match table definition)
            var rowForEval = new WitSqlRow(dataRow.Values.ToArray(), dataRow.ColumnNames.ToArray());
            var evaluator = new ExpressionEvaluator(m_context);

            foreach (var (colIndex, expression) in m_virtualComputedColumns)
            {
                try
                {
                    var expr = WitSql.ParseExpression(expression);
                    var computedValue = evaluator.Evaluate(expr, rowForEval);
                    values[colIndex + 1] = computedValue; // +1 because of _rowid
                }
                catch
                {
                    // If evaluation fails, keep NULL
                    values[colIndex + 1] = WitSqlValue.Null;
                }
            }
        }

        m_current = new WitSqlRow(values, names);
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_enumerator?.Dispose();
        m_enumerator = null;
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_enumerator?.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema ??= BuildSchema();

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}
