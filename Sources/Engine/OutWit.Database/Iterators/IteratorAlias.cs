using OutWit.Database.Interfaces;

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
            return m_source.MoveNext();
        }

        /// <inheritdoc/>
        public override void Reset()
        {
            base.Reset();
            m_source.Reset();
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
        public override WitSqlRow Current => m_source.Current;

        #endregion
    }
}
