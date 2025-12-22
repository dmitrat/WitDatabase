namespace OutWit.Database.Parser.Schema.Types
{
    /// <summary>
    /// Represents the conflict resolution strategy for INSERT statements.
    /// </summary>
    public enum ConflictResolutionType
    {
        /// <summary>
        /// No conflict resolution specified.
        /// </summary>
        None,

        /// <summary>
        /// Replace existing row on conflict (INSERT OR REPLACE).
        /// </summary>
        Replace,

        /// <summary>
        /// Ignore the insert on conflict (INSERT OR IGNORE).
        /// </summary>
        Ignore
    }
}
