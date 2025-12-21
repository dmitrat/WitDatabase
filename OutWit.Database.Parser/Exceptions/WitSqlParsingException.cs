namespace OutWit.Database.Parser.Exceptions
{
    /// <summary>
    /// Exception thrown when SQL parsing fails.
    /// </summary>
    public class WitSqlParsingException : Exception
    {
        #region Constructors

        public WitSqlParsingException(IReadOnlyCollection<WitSqlParsingError> errors)
            : base(FormatMessage(errors))
        {
            Errors = errors.ToList().AsReadOnly();
        }

        #endregion

        #region Functions

        private static string FormatMessage(IReadOnlyCollection<WitSqlParsingError> errors)
        {;
            if (errors.Count == 1)
                return errors.Single().ToString();
            
            return $"SQL parsing failed with {errors.Count} errors:\n" +
                   string.Join("\n", errors.Select(error => $"  • {error}"));
        }

        #endregion

        #region Properties

        public IReadOnlyCollection<WitSqlParsingError> Errors { get; }

        #endregion
    }
}