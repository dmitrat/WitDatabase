namespace OutWit.Database.Parser.Schema.Types;

/// <summary>
/// Types of SQL set operations.
/// </summary>
public enum SetOperationType
{
    /// <summary>
    /// UNION - combines results, removes duplicates (unless ALL is specified).
    /// </summary>
    Union,

    /// <summary>
    /// INTERSECT - returns only rows that appear in both result sets.
    /// </summary>
    Intersect,

    /// <summary>
    /// EXCEPT - returns rows from the first set that are not in the second set.
    /// </summary>
    Except
}
