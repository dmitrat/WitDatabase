namespace OutWit.Database.Parser.Schema.Types
{
    /// <summary>
    /// Represents the type of row-level lock for SELECT statements.
    /// </summary>
    public enum LockingType
    {
        /// <summary>
        /// No locking specified.
        /// </summary>
        None,

        /// <summary>
        /// FOR UPDATE - exclusive lock for modification.
        /// </summary>
        ForUpdate,

        /// <summary>
        /// FOR SHARE - shared lock for reading.
        /// </summary>
        ForShare
    }
}
