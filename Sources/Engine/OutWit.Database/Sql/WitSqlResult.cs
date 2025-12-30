using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Database.Interfaces;

namespace OutWit.Database.Sql;

/// <summary>
/// Represents the result of executing a SQL statement.
/// </summary>
/// <remarks>
/// Supports three types of results:
/// <list type="bullet">
/// <item><description>SELECT queries - iterate rows using <see cref="Read"/> or <see cref="ReadAll"/></description></item>
/// <item><description>DML statements (INSERT, UPDATE, DELETE) - check <see cref="RowsAffected"/></description></item>
/// <item><description>DML with RETURNING - both <see cref="RowsAffected"/> and rows available</description></item>
/// <item><description>DDL statements (CREATE, DROP, ALTER) - no data returned</description></item>
/// </list>
/// </remarks>
public sealed class WitSqlResult : IDisposable
{
    #region Fields

    private readonly IEnumerator<WitSqlRow>? m_rowEnumerator;
    private readonly IResultIterator? m_iterator;
    private readonly Action? m_onDispose;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a result for queries that return rows (SELECT) with streaming support.
    /// This is the preferred constructor for SELECT queries as it does not materialize all rows.
    /// </summary>
    /// <param name="iterator">The result iterator (will be disposed when result is disposed).</param>
    /// <param name="onDispose">Optional cleanup action to run on dispose.</param>
    public WitSqlResult(IResultIterator iterator, Action? onDispose = null)
    {
        m_iterator = iterator ?? throw new ArgumentNullException(nameof(iterator));
        Columns = iterator.Schema;
        HasRows = true;
        m_onDispose = onDispose;
    }

    /// <summary>
    /// Creates a result for queries that return rows (SELECT).
    /// </summary>
    /// <param name="rows">Enumerable of result rows.</param>
    /// <param name="columns">Column schema information.</param>
    public WitSqlResult(IEnumerable<WitSqlRow> rows, IReadOnlyList<WitSqlColumnInfo> columns)
    {
        Columns = columns;
        m_rowEnumerator = rows.GetEnumerator();
        HasRows = true;
    }

    /// <summary>
    /// Creates a result for queries that don't return rows (INSERT, UPDATE, DELETE).
    /// </summary>
    /// <param name="rowsAffected">Number of rows affected by the statement.</param>
    public WitSqlResult(int rowsAffected)
    {
        RowsAffected = rowsAffected;
        Columns = [];
        HasRows = false;
    }

    /// <summary>
    /// Creates a result for DML statements with RETURNING clause.
    /// </summary>
    /// <param name="rowsAffected">Number of rows affected by the statement.</param>
    /// <param name="rows">Enumerable of returned rows.</param>
    /// <param name="columns">Column schema information.</param>
    public WitSqlResult(int rowsAffected, IEnumerable<WitSqlRow> rows, IReadOnlyList<WitSqlColumnInfo> columns)
    {
        RowsAffected = rowsAffected;
        Columns = columns;
        m_rowEnumerator = rows.GetEnumerator();
        HasRows = true;
    }

    /// <summary>
    /// Creates a result for DDL statements (CREATE, DROP, ALTER).
    /// </summary>
    public WitSqlResult()
    {
        Columns = [];
        HasRows = false;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Advances to the next row in the result set.
    /// </summary>
    /// <returns>True if there is another row; false when no more rows are available.</returns>
    public bool Read()
    {
        if (m_disposed)
            return false;

        // Streaming mode - use iterator directly
        if (m_iterator != null)
        {
            if (m_iterator.MoveNext())
            {
                CurrentRow = m_iterator.Current;
                return true;
            }
            return false;
        }

        // Legacy mode - use enumerator
        if (m_rowEnumerator == null)
            return false;

        if (m_rowEnumerator.MoveNext())
        {
            CurrentRow = m_rowEnumerator.Current;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads all remaining rows into a list.
    /// </summary>
    /// <returns>A list containing all remaining rows.</returns>
    public List<WitSqlRow> ReadAll()
    {
        var rows = new List<WitSqlRow>();
        while (Read())
        {
            rows.Add(CurrentRow);
        }
        return rows;
    }

    /// <summary>
    /// Reads all remaining rows asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list containing all remaining rows.</returns>
    public Task<List<WitSqlRow>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        // For now, just wrap sync version - can be optimized later for async enumeration
        return Task.FromResult(ReadAll());
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) 
            return;
        
        m_disposed = true;
        m_rowEnumerator?.Dispose();
        m_iterator?.Dispose();
        
        // Run cleanup action (e.g., clear CTE cache)
        m_onDispose?.Invoke();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the column schema for the result set.
    /// </summary>
    /// <remarks>
    /// Empty for non-SELECT statements.
    /// </remarks>
    public IReadOnlyList<WitSqlColumnInfo> Columns { get; }

    /// <summary>
    /// Gets the number of rows affected by DML statements (INSERT, UPDATE, DELETE).
    /// </summary>
    /// <remarks>
    /// Returns 0 for SELECT and DDL statements.
    /// </remarks>
    public int RowsAffected { get; }

    /// <summary>
    /// Gets whether this result has rows to read.
    /// </summary>
    /// <remarks>
    /// True for SELECT queries, false for DML/DDL statements.
    /// </remarks>
    public bool HasRows { get; }

    /// <summary>
    /// Gets the current row after calling <see cref="Read"/>.
    /// </summary>
    /// <remarks>
    /// Default value before first <see cref="Read"/> call.
    /// </remarks>
    public WitSqlRow CurrentRow { get; private set; }

    #endregion
}
