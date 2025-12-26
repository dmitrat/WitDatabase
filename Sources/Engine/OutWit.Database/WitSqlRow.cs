using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// Represents a row of SQL values with column name lookup support.
/// </summary>
/// <remarks>
/// This is a struct for performance - rows are typically created in large numbers
/// during query execution and benefit from stack allocation and cache locality.
/// </remarks>
public readonly struct WitSqlRow
{
    #region Fields

    private readonly WitSqlValue[] m_values;
    private readonly string[] m_columnNames;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new SQL row with the specified values and column names.
    /// </summary>
    /// <param name="values">The values for each column.</param>
    /// <param name="columnNames">The names of each column.</param>
    /// <exception cref="ArgumentException">If values and column names have different lengths.</exception>
    public WitSqlRow(WitSqlValue[] values, string[] columnNames)
    {
        if (values.Length != columnNames.Length)
            throw new ArgumentException("Values and column names must have the same length");

        m_values = values;
        m_columnNames = columnNames;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Gets the column name at the specified index.
    /// </summary>
    /// <param name="index">Zero-based column index.</param>
    /// <returns>The column name.</returns>
    public string GetColumnName(int index) => m_columnNames[index];

    /// <summary>
    /// Tries to get a value by column name.
    /// </summary>
    /// <param name="columnName">The column name (case-insensitive).</param>
    /// <param name="value">The value if found, otherwise NULL.</param>
    /// <returns>True if the column was found.</returns>
    public bool TryGetValue(string columnName, out WitSqlValue value)
    {
        var index = FindColumnIndex(columnName);
        if (index >= 0)
        {
            value = m_values[index];
            return true;
        }
        value = WitSqlValue.Null;
        return false;
    }

    /// <summary>
    /// Case-insensitive column name lookup (SQL standard behavior).
    /// Supports both exact match and match on column name suffix (e.g., "Name" matches "Users.Name").
    /// </summary>
    private int FindColumnIndex(string columnName)
    {
        // First try exact match
        for (int i = 0; i < m_columnNames.Length; i++)
        {
            if (m_columnNames[i].Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        // If not found and no dot in search, try matching the suffix (column name without table prefix)
        if (columnName.Contains('.'))
            return -1;

        var suffix = "." + columnName;
        for (int i = 0; i < m_columnNames.Length; i++)
        {
            if (m_columnNames[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => m_values.Length;

    /// <summary>
    /// Gets the value at the specified column index.
    /// </summary>
    /// <param name="index">Zero-based column index.</param>
    public WitSqlValue this[int index] => m_values[index];

    /// <summary>
    /// Gets the value for the specified column name.
    /// </summary>
    /// <param name="columnName">The column name (case-insensitive).</param>
    /// <exception cref="KeyNotFoundException">If the column is not found.</exception>
    public WitSqlValue this[string columnName]
    {
        get
        {
            var index = FindColumnIndex(columnName);
            if (index < 0)
                throw new KeyNotFoundException($"Column '{columnName}' not found");
            return m_values[index];
        }
    }

    /// <summary>
    /// Gets the list of column names.
    /// </summary>
    public IReadOnlyList<string> ColumnNames => m_columnNames;

    /// <summary>
    /// Gets the list of values.
    /// </summary>
    public IReadOnlyList<WitSqlValue> Values => m_values;

    #endregion
}
