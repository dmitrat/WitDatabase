using OutWit.Database.Interfaces;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that excludes internal columns (like _rowid) from the result.
/// Used for SELECT * to hide internal implementation details from users.
/// </summary>
internal sealed class IteratorExcludeInternal : IteratorBase
{
    #region Constants

    /// <summary>
    /// Internal row ID column name that should be excluded from SELECT * results.
    /// </summary>
    public const string INTERNAL_ROWID_COLUMN = "_rowid";

    #endregion

    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
    private readonly int[] m_columnMapping; // Maps output index to source index
    private readonly string[] m_columnNames;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new iterator that excludes internal columns.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    public IteratorExcludeInternal(IResultIterator source)
    {
        m_source = source;
        
        // Build schema and mapping excluding internal columns
        var sourceSchema = source.Schema;
        var filteredSchema = new List<WitSqlColumnInfo>();
        var mapping = new List<int>();
        
        for (int i = 0; i < sourceSchema.Count; i++)
        {
            var col = sourceSchema[i];
            if (!IsInternalColumn(col.Name))
            {
                mapping.Add(i);
                filteredSchema.Add(col);
            }
        }
        
        m_schema = filteredSchema;
        m_columnMapping = mapping.ToArray();
        m_columnNames = filteredSchema.Select(c => c.Name).ToArray();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks if a column name is an internal column that should be hidden.
    /// </summary>
    /// <param name="columnName">The column name to check.</param>
    /// <returns>True if the column is internal and should be excluded.</returns>
    public static bool IsInternalColumn(string columnName)
    {
        return columnName.Equals(INTERNAL_ROWID_COLUMN, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the source schema contains any internal columns that need filtering.
    /// </summary>
    /// <param name="schema">The schema to check.</param>
    /// <returns>True if filtering is needed.</returns>
    public static bool NeedsFiltering(IReadOnlyList<WitSqlColumnInfo> schema)
    {
        for (int i = 0; i < schema.Count; i++)
        {
            if (IsInternalColumn(schema[i].Name))
                return true;
        }
        return false;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (!m_source.MoveNext())
            return false;

        var sourceRow = m_source.Current;
        var values = new WitSqlValue[m_columnMapping.Length];
        
        for (int i = 0; i < m_columnMapping.Length; i++)
        {
            values[i] = sourceRow[m_columnMapping[i]];
        }

        m_current = new WitSqlRow(values, m_columnNames);
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}
