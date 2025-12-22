namespace OutWit.Database.Parser.Schema.Types
{
    /// <summary>
    /// Represents the action to take when a conflict occurs in an ON CONFLICT clause.
    /// </summary>
    public enum ConflictActionType
    {
        /// <summary>
        /// Do nothing - ignore the conflicting row.
        /// </summary>
        Nothing,

        /// <summary>
        /// Update the existing row with new values.
        /// </summary>
        Update
    }
}
