namespace OutWit.Database.Core.Exceptions
{
    /// <summary>
    /// A unique index was asked to hold two entries for one key.
    /// </summary>
    /// <remarks>
    /// It derives from <see cref="InvalidOperationException"/> because that is what this was for
    /// every release up to now, so nothing that catches the base type stops working. The reason it
    /// needs a type of its own is that the index build could not tell this apart from any other
    /// failure: it read <b>every</b> <see cref="InvalidOperationException"/> as a duplicate, so an
    /// exhausted page cache was reported to the user as "UNIQUE constraint failed" - and the
    /// cleanup written for one of those two cases ran for both.
    /// </remarks>
    public class UniqueIndexViolationException : InvalidOperationException
    {
        #region Constructors

        public UniqueIndexViolationException(string message) : base(message)
        {
        }

        public UniqueIndexViolationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        #endregion
    }
}
