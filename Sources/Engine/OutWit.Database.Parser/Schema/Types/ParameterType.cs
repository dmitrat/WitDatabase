namespace OutWit.Database.Parser.Schema.Types;

/// <summary>
/// Types of SQL parameter placeholders.
/// </summary>
public enum ParameterType
{
    /// <summary>
    /// Named parameter with @ prefix: @paramName
    /// </summary>
    Named,

    /// <summary>
    /// Named parameter with : prefix: :paramName
    /// </summary>
    Colon,

    /// <summary>
    /// Positional parameter: ?
    /// </summary>
    Positional,

    /// <summary>
    /// SQLite-style named parameter with $ prefix: $paramName (not $1, $2).
    /// </summary>
    DollarNamed,

    /// <summary>
    /// Numbered parameter: $1, $2, etc.
    /// </summary>
    Numbered
}
