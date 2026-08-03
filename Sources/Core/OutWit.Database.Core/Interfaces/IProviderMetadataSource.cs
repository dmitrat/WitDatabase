namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Interface for stores that keep the provider metadata a database was created with, and can hand
    /// it back after the store is built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The metadata is written into the database header when the file is created and loaded from it on
    /// every subsequent open, so it is the only record of the configuration that <b>wrote</b> the data.
    /// Comparing it against the configuration now asking to open is what turns a mismatch into a
    /// refusal instead of an empty-looking database - see
    /// <c>WitDatabaseBuilder.ValidateStoredConfiguration</c>.
    /// </para>
    /// <para>
    /// It is a separate interface rather than a member of <see cref="IKeyValueStore"/> because most
    /// stores have no header to keep it in: the LSM store is a directory of files and the in-memory
    /// store outlives nothing. A store that cannot answer simply does not implement this.
    /// </para>
    /// </remarks>
    public interface IProviderMetadataSource
    {
        /// <summary>
        /// The provider metadata read from the database this store opened, or <c>null</c> when the
        /// store has none to report.
        /// </summary>
        /// <remarks>
        /// For a database this build has just created, this is the metadata that was written a moment
        /// ago - so a comparison against the current configuration is trivially satisfied, which is
        /// exactly what a new database should do.
        /// </remarks>
        ProviderMetadata? StoredMetadata { get; }
    }
}
