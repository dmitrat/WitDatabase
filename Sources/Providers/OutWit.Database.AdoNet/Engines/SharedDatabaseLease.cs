using OutWit.Database.Core.Builder;
using OutWit.Database.Schema;

namespace OutWit.Database.AdoNet.Engines;

/// <summary>
/// A connection's share of a database held by <see cref="SharedDatabase"/>.
/// </summary>
/// <remarks>
/// Disposing the lease gives the share back; the database itself is disposed only when the last lease
/// goes, which is also when its exclusive lock is released. Disposal is idempotent, because a connection
/// can be closed and then disposed and must not release two shares for one.
/// </remarks>
internal sealed class SharedDatabaseLease : IDisposable
{
    #region Fields

    private readonly string m_key;

    private bool m_released;

    #endregion

    #region Constructors

    internal SharedDatabaseLease(string key, WitDatabase database, SchemaCatalog schema)
    {
        m_key = key;
        Database = database;
        Schema = schema;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        // Close() then Dispose() on a DbConnection is ordinary, and releasing twice would drop the
        // reference count below the number of live connections - the database would be disposed
        // underneath somebody still using it.
        if (m_released)
            return;

        m_released = true;
        SharedDatabase.Release(m_key);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The shared database. Do not dispose it: the lease owns the share, not the database.
    /// </summary>
    public WitDatabase Database { get; }

    /// <summary>
    /// The schema catalog shared by every session on this database. Passed to each connection's own
    /// engine, which is what keeps two connections agreeing about tables and row counts.
    /// </summary>
    public SchemaCatalog Schema { get; }

    #endregion
}
