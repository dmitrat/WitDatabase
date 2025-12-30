using OutWit.Database.Context;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Schema;
using OutWit.Database.Sql;
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
    
    // Cached info about virtual computed columns with pre-parsed expressions
    private readonly List<(int Index, WitSqlExpression Expression)>? m_virtualComputedColumns;
    
    // Cached column names array (shared across all rows)
    private readonly string[] m_columnNames;
    
    // Column names for deserialization (without _rowid)
    private readonly string[] m_dataColumnNames;
    
    // Cached evaluator (reused across rows)
    private ExpressionEvaluator? m_evaluator;

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
        
        // Cache virtual computed columns info with pre-parsed expressions
        m_virtualComputedColumns = null;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            if (col.IsComputed && !col.IsStored && !string.IsNullOrEmpty(col.ComputedExpression))
            {
                try
                {
                    var parsedExpr = WitSql.ParseExpression(col.ComputedExpression);
                    m_virtualComputedColumns ??= new List<(int, WitSqlExpression)>();
                    m_virtualComputedColumns.Add((i, parsedExpr));
                }
                catch
                {
                    // If parsing fails, skip this computed column
                }
            }
        }
        
        // Pre-create column names array (shared across all rows - strings are immutable)
        // This array includes _rowid as first column
        var bufferSize = table.Columns.Count + 1;
        m_columnNames = new string[bufferSize];
        m_columnNames[0] = "_rowid";
        for (int i = 0; i < table.Columns.Count; i++)
        {
            m_columnNames[i + 1] = table.Columns[i].Name;
        }
        
        // Pre-create data column names (without _rowid) for computed column evaluation
        m_dataColumnNames = new string[table.Columns.Count];
        for (int i = 0; i < table.Columns.Count; i++)
        {
            m_dataColumnNames[i] = table.Columns[i].Name;
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
        
        // Create evaluator once if we have computed columns
        if (m_virtualComputedColumns != null && m_context != null)
        {
            m_evaluator = new ExpressionEvaluator(m_context);
        }
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_enumerator == null || !m_enumerator.MoveNext())
            return false;

        var (key, value) = m_enumerator.Current;

        // Parse row ID from key
        var rowId = SchemaCatalog.ParseRowId(key, m_table.Name);

        // Deserialize row values only (without creating new names array)
        var dataValues = m_table.DeserializeRowValues(value, m_dataColumnNames);

        // Create new values array for this row (includes _rowid as first element)
        var values = new WitSqlValue[m_columnNames.Length];
        values[0] = WitSqlValue.FromInt(rowId);

        // Copy values from deserialized data
        for (int i = 0; i < dataValues.Length; i++)
        {
            values[i + 1] = dataValues[i];
        }

        // Evaluate VIRTUAL computed columns on-the-fly using cached expressions
        if (m_virtualComputedColumns != null && m_evaluator != null)
        {
            // Create row for evaluation using cached column names
            var rowForEval = new WitSqlRow(dataValues, m_dataColumnNames);
            
            foreach (var (colIndex, expr) in m_virtualComputedColumns)
            {
                try
                {
                    var computedValue = m_evaluator.Evaluate(expr, rowForEval);
                    values[colIndex + 1] = computedValue; // +1 because of _rowid
                }
                catch
                {
                    // If evaluation fails, keep NULL
                    values[colIndex + 1] = WitSqlValue.Null;
                }
            }
        }

        // Create row - column names array is shared (safe because strings are immutable)
        m_current = new WitSqlRow(values, m_columnNames);
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_enumerator?.Dispose();
        m_enumerator = null;
        m_current = default;
        m_evaluator = null;
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
