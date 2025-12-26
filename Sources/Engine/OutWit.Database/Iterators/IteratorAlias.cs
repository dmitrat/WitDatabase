using OutWit.Database.Interfaces;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators
{
    /// <summary>
    /// Wraps an iterator and replaces the table name in schema columns with an alias.
    /// This is needed for proper resolution of table-qualified column references like "u.Id".
    /// </summary>
    public sealed class IteratorAlias : IteratorBase
    {
        #region Fields

        private readonly IResultIterator m_source;
        private readonly string m_alias;
        private WitSqlRow m_current;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new alias iterator.
        /// </summary>
        /// <param name="source">The source iterator.</param>
        /// <param name="alias">The alias to apply to table names in the schema.</param>
        public IteratorAlias(IResultIterator source, string alias)
        {
            m_source = source;
            m_alias = alias;
            Schema = BuildSchema();
        }

        #endregion

        #region Functions

        private IReadOnlyList<WitSqlColumnInfo> BuildSchema()
        {
            var schema = new List<WitSqlColumnInfo>(m_source.Schema.Count);
            foreach (var columnInfo in m_source.Schema)
            {
                schema.Add(new WitSqlColumnInfo
                {
                    Name = columnInfo.Name,
                    Type = columnInfo.Type,
                    IsNullable = columnInfo.IsNullable,
                    TableName = m_alias // Override table name with alias
                });
            }
            return schema;
        }

        /// <summary>
        /// Creates a new row with aliased column names (e.g., "Id" -> "A.Id").
        /// Also keeps the original column names for unqualified access.
        /// </summary>
        private WitSqlRow CreateAliasedRow(WitSqlRow sourceRow)
        {
            var sourceNames = sourceRow.ColumnNames;
            var sourceValues = sourceRow.Values;
            
            // Create array with both aliased and original column names
            // This allows both "A.Id" and "Id" to work
            var count = sourceNames.Count;
            var names = new string[count * 2];
            var values = new WitSqlValue[count * 2];
            
            for (int i = 0; i < count; i++)
            {
                var originalName = sourceNames[i];
                
                // Aliased name (e.g., "A.Id")
                names[i] = $"{m_alias}.{originalName}";
                values[i] = sourceValues[i];
                
                // Original name (e.g., "Id") for unqualified access
                names[count + i] = originalName;
                values[count + i] = sourceValues[i];
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
            
            m_current = CreateAliasedRow(m_source.Current);
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
}
