using OutWit.Database.Interfaces;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Wraps an iterator and renames columns according to a specified mapping.
/// Used for CTE column aliasing when WITH clause specifies explicit column names.
/// </summary>
public sealed class IteratorColumnRename : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<string> m_newColumnNames;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new column rename iterator.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    /// <param name="newColumnNames">The new column names to apply (in order).</param>
    public IteratorColumnRename(IResultIterator source, IReadOnlyList<string> newColumnNames)
    {
        m_source = source;
        m_newColumnNames = newColumnNames;
        Schema = BuildSchema();
    }

    #endregion

    #region Functions

    private IReadOnlyList<WitSqlColumnInfo> BuildSchema()
    {
        var sourceSchema = m_source.Schema;
        var schema = new List<WitSqlColumnInfo>(sourceSchema.Count);
        
        for (int i = 0; i < sourceSchema.Count; i++)
        {
            var newName = i < m_newColumnNames.Count 
                ? m_newColumnNames[i] 
                : sourceSchema[i].Name;
                
            schema.Add(new WitSqlColumnInfo
            {
                Name = newName,
                Type = sourceSchema[i].Type,
                IsNullable = sourceSchema[i].IsNullable,
                TableName = sourceSchema[i].TableName
            });
        }
        
        return schema;
    }

    private WitSqlRow CreateRenamedRow(WitSqlRow sourceRow)
    {
        var sourceValues = sourceRow.Values;
        var count = sourceValues.Count;
        var names = new string[count];
        var values = new WitSqlValue[count];
        
        for (int i = 0; i < count; i++)
        {
            names[i] = i < m_newColumnNames.Count 
                ? m_newColumnNames[i] 
                : sourceRow.ColumnNames[i];
            values[i] = sourceValues[i];
        }
        
        return new WitSqlRow(values, names);
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
        
        m_current = CreateRenamedRow(m_source.Current);
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
    public override IReadOnlyList<WitSqlColumnInfo> Schema { get; }

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}
