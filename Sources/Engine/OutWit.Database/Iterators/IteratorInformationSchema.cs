using OutWit.Database.Interfaces;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for INFORMATION_SCHEMA virtual tables.
/// Provides read-only access to database metadata.
/// </summary>
public sealed class IteratorInformationSchema : IResultIterator
{
    #region Fields

    private readonly IEnumerable<WitSqlRow> m_rowSource;
    private IEnumerator<WitSqlRow>? m_enumerator;
    private WitSqlRow m_currentRow;
    private bool m_disposed;

    #endregion

    #region Constructors

    public IteratorInformationSchema(
        IEnumerable<WitSqlRow> rows,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<WitSqlType> columnTypes)
    {
        m_rowSource = rows;
        
        // Build schema from column names and types
        var schema = new WitSqlColumnInfo[columnNames.Count];
        for (int i = 0; i < columnNames.Count; i++)
        {
            schema[i] = new WitSqlColumnInfo
            {
                Name = columnNames[i],
                Type = columnTypes[i],
                IsNullable = true,
                TableName = SchemaCatalog.INFORMATION_SCHEMA_NAME
            };
        }
        Schema = schema;
    }

    #endregion

    #region IResultIterator
    
    /// <inheritdoc />
    public void Open()
    {
        m_enumerator?.Dispose();
        m_enumerator = m_rowSource.GetEnumerator();
    }

    /// <inheritdoc />
    public bool MoveNext()
    {
        if (m_disposed || m_enumerator == null)
            return false;

        if (m_enumerator.MoveNext())
        {
            m_currentRow = m_enumerator.Current;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Reset()
    {
        m_enumerator?.Dispose();
        m_enumerator = null;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_enumerator?.Dispose();
    }

    #endregion


    #region Properties

    /// <inheritdoc />
    public IReadOnlyList<WitSqlColumnInfo> Schema { get; }


    /// <inheritdoc />
    public WitSqlRow Current
    {
        get
        {
            if (m_enumerator == null)
                throw new InvalidOperationException("No current row. Call Open() and MoveNext() first.");
            return m_currentRow;
        }
    }


    /// <inheritdoc />
    public long EstimatedRowCount => -1;

    #endregion

}
