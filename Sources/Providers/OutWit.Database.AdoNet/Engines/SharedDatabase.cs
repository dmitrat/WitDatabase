using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Schema;

namespace OutWit.Database.AdoNet.Engines;

/// <summary>
/// One database and one schema catalog, shared by every connection addressing that database in this
/// process, reference-counted.
/// </summary>
/// <remarks>
/// <b>Why.</b> The supported deployment is an ASP.NET Core service: one host process, many scoped
/// <c>DbContext</c>s, therefore many concurrent connections to one database. Until 5.0.0 each
/// connection built its own <see cref="WitDatabase"/>, and a database admits one engine - so the second
/// concurrent connection simply failed, and the shape the project targets did not work.
///
/// <b>What is shared and what is not.</b> A <see cref="WitSqlEngine"/> holds the current transaction, so
/// it is a <i>session</i> and each connection keeps its own. The <see cref="WitDatabase"/> and the
/// <see cref="SchemaCatalog"/> are properties of the <i>database</i> and are shared. That division was
/// measured rather than assumed: with a per-session catalog, a table created by one session was
/// <c>Table not found</c> to another, and a row inserted by one was visible to the other's scan while
/// that other's <c>COUNT(*)</c> still said zero - see
/// <c>SharedDatabaseTwoEnginesProbeTests</c>.
///
/// <b>Not a connection pool.</b> <see cref="Pool.ConnectionPool"/> pools connection <i>objects</i>, each
/// of which used to bring its own engine - which is why it collided with itself over a file-backed
/// database. The expensive thing was always the engine, and this shares it; connections are now cheap
/// handles.
///
/// <b>Locking.</b> One lock covers the registry, so two connections opening two <i>different</i>
/// databases are serialised for the duration of the open. Deliberate: opening is rare, and per-key
/// locking would be a second thing to get right for no measurable gain. If opening ever becomes hot,
/// this is where to shard.
/// </remarks>
internal static class SharedDatabase
{
    #region Fields

    private static readonly Lock s_lock = new();

    private static readonly Dictionary<string, Entry> s_entries = new(StringComparer.Ordinal);

    #endregion

    #region Acquire

    /// <summary>
    /// Takes a lease on the database for <paramref name="key"/>, building it if this is the first.
    /// </summary>
    /// <param name="key">Canonical identity of the database - see <see cref="SharedDatabaseKey"/>.</param>
    /// <param name="signature">
    /// Canonical form of the options that shape the database. Two connections may share only if these
    /// match; otherwise the second would silently get a database built to somebody else's
    /// configuration.
    /// </param>
    /// <param name="factory">Builds the database when no engine exists for the key yet.</param>
    public static SharedDatabaseLease Acquire(string key, string signature, Func<WitDatabase> factory)
    {
        lock (s_lock)
        {
            if (s_entries.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Signature, signature, StringComparison.Ordinal))
                    throw new InvalidOperationException(BuildSignatureMismatchMessage(key, existing.Signature, signature));

                existing.Leases++;
                return new SharedDatabaseLease(key, existing.Database, existing.Schema);
            }

            // Built inside the lock so two connections racing to open the same database cannot both
            // construct one - the loser would then fail on the exclusive database lock, reporting
            // "already open" about a database nobody had finished opening.
            var database = factory();

            SchemaCatalog schema;

            try
            {
                schema = new SchemaCatalog(database.Store);
            }
            catch
            {
                // Otherwise the database - and its exclusive lock - would be orphaned with no lease to
                // release them, leaving the file unopenable for the rest of the process's life.
                database.Dispose();
                throw;
            }

            s_entries[key] = new Entry(database, schema, signature);
            return new SharedDatabaseLease(key, database, schema);
        }
    }

    #endregion

    #region Release

    /// <summary>
    /// Gives back a lease, disposing the database when the last one goes.
    /// </summary>
    internal static void Release(string key)
    {
        WitDatabase? database = null;
        SchemaCatalog? schema = null;

        lock (s_lock)
        {
            if (!s_entries.TryGetValue(key, out var entry))
                return;

            entry.Leases--;

            if (entry.Leases > 0)
                return;

            s_entries.Remove(key);
            database = entry.Database;
            schema = entry.Schema;
        }

        // Outside the lock: disposing flushes, may compact, and releases the exclusive database lock,
        // none of which should hold up a connection opening an unrelated database.
        schema?.Dispose();
        database?.Dispose();
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// Number of databases with a live engine in this process. For tests and diagnostics.
    /// </summary>
    internal static int LiveDatabaseCount
    {
        get
        {
            lock (s_lock)
                return s_entries.Count;
        }
    }

    /// <summary>
    /// Number of connections currently sharing <paramref name="key"/>, or zero if none.
    /// </summary>
    internal static int LeaseCount(string key)
    {
        lock (s_lock)
            return s_entries.TryGetValue(key, out var entry) ? entry.Leases : 0;
    }

    #endregion

    #region Tools

    private static string BuildSignatureMismatchMessage(string key, string open, string requested) =>
        $"The database '{key}' is already open in this process with different options, and one database "
        + "has one engine. Connections that share a database must agree on the options that shape it - "
        + "store, encryption, journal, MVCC, transactions, parallel mode and provider parameters. Either "
        + "use the same connection string everywhere for this database, or close the existing "
        + $"connections first.{Environment.NewLine}"
        + $"  already open with: {open}{Environment.NewLine}"
        + $"  requested:         {requested}";

    #endregion

    #region Entry

    private sealed class Entry(WitDatabase database, SchemaCatalog schema, string signature)
    {
        public WitDatabase Database { get; } = database;

        public SchemaCatalog Schema { get; } = schema;

        public string Signature { get; } = signature;

        public int Leases { get; set; } = 1;
    }

    #endregion
}
