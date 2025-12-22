namespace OutWit.Database.Parser.Schema.Types
{
    /// <summary>
    /// Represents the quantifier type for quantified comparison expressions.
    /// </summary>
    public enum QuantifierType
    {
        /// <summary>
        /// ANY quantifier - returns true if any row in the subquery satisfies the condition.
        /// </summary>
        Any,

        /// <summary>
        /// SOME quantifier - alias for ANY.
        /// </summary>
        Some,

        /// <summary>
        /// ALL quantifier - returns true if all rows in the subquery satisfy the condition.
        /// </summary>
        All
    }
}
