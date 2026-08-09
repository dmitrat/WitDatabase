using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// A store that can describe the database it has open - the whole configuration, not just the
    /// provider metadata of <see cref="IProviderMetadataSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists, measured 2026-08-09.</b> The only way to ask a database what it was created
    /// with was <c>StorageDetector.ReadStoredConfiguration</c>, which opens the file and reads the
    /// header - and since 12.2.0 an open paged database holds an exclusive lock, so that answers
    /// <c>null</c> for the one database a caller is most likely to be asking about: the one it has
    /// open. Studio worked around it by reading the header a moment BEFORE connecting.
    /// </para>
    /// <para>
    /// A store that is open does not need the file: the paged store keeps the header in memory, so it
    /// can answer from behind its own lock. An LSM directory never had the problem - its sidecar is a
    /// separate file the lock does not cover, and reading it while open was measured to work.
    /// </para>
    /// <para>
    /// Everything here is decided when the database is CREATED and cannot change while it is open, so
    /// there is no question of the answer going stale.
    /// </para>
    /// </remarks>
    public interface IStoredConfigurationSource
    {
        /// <summary>
        /// The configuration the open database was created with, or <c>null</c> when this store has
        /// none to report - an in-memory database, or a store that keeps no header.
        /// </summary>
        StoredConfiguration? StoredConfiguration { get; }
    }
}
