using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutWit.Database;

/// <summary>
/// Represents the result of executing a SQL statement.
/// </summary>
/// <remarks>
/// Supports three types of results:
/// <list type="bullet">
/// <item><description>SELECT queries - iterate rows using <see cref="Read"/> or <see cref="ReadAll"/></description></item>
/// <item><description>DML statements (INSERT, UPDATE, DELETE) - check <see cref="RowsAffected"/></description></item>
/// <item><description>DDL statements (CREATE, DROP, ALTER) - no data returned</description></item>
/// </list>
/// </remarks>
public sealed class WitSqlResult : IDisposable
{
    #region Fields

    private readonly IEnumerator<WitSqlRow>? m_rowEnumerator;
    private bool m_disposed;

    #endregion

    #region Constructors

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
        if (m_disposed || m_rowEnumerator == null)
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
