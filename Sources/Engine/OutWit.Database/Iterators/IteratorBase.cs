using OutWit.Database.Interfaces;

namespace OutWit.Database.Iterators
{
    /// <summary>
    /// Base class for all query execution iterators.
    /// Implements the Volcano/Iterator model for query execution.
    /// </summary>
    public abstract class IteratorBase : IResultIterator
    {
        #region Fields

        private bool m_isOpen;

        #endregion

        #region IResultIterator

        /// <inheritdoc/>
        public virtual void Open()
        {
            m_isOpen = true;
        }

        /// <inheritdoc/>
        public abstract bool MoveNext();

        /// <inheritdoc/>
        public virtual void Reset()
        {
            m_isOpen = false;
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public virtual void Dispose()
        {
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether the iterator is open.
        /// </summary>
        protected bool IsOpen => m_isOpen;

        /// <inheritdoc/>
        public abstract IReadOnlyList<WitSqlColumnInfo> Schema { get; }

        /// <inheritdoc/>
        public abstract WitSqlRow Current { get; }

        /// <inheritdoc/>
        public virtual long EstimatedRowCount => -1;

        #endregion
    }
}