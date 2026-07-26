namespace OutWit.Database.Exceptions;

/// <summary>
/// Exception thrown when a persisted schema record cannot be read back.
/// </summary>
/// <remarks>
/// The catalog used to deserialize its records through a helper that swallows every exception and
/// returns <c>default</c>. Any failure — a torn write, a definition shape changed between library
/// versions, a format break, a wrong decryption key — therefore produced an *empty* catalog rather
/// than an error, so every statement failed with "Table 'X' not found" while the row data sat intact
/// on disk. Worse, the next DDL statement called <c>SaveSchema()</c> and overwrote the record with the
/// near-empty list, destroying the schema permanently. Failing loudly here is the difference between
/// a recoverable file and a lost one.
/// </remarks>
public sealed class WitSchemaCorruptException : Exception
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitSchemaCorruptException"/> class.
    /// </summary>
    /// <param name="recordName">The schema record that could not be read, e.g. <c>tables</c>.</param>
    /// <param name="byteCount">Size of the stored record, useful for telling truncation apart from a shape change.</param>
    public WitSchemaCorruptException(string recordName, int byteCount)
        : base(FormatMessage(recordName, byteCount))
    {
        RecordName = recordName;
        ByteCount = byteCount;
    }

    #endregion

    #region Functions

    private static string FormatMessage(string recordName, int byteCount)
    {
        return $"The database schema record '{recordName}' ({byteCount} bytes) could not be " +
               $"deserialized. The file may have been written by an incompatible version of " +
               $"WitDatabase, truncated by an interrupted write, or opened with the wrong " +
               $"encryption key. The schema has NOT been modified - do not run DDL against this " +
               $"database until the cause is known, because saving the schema would overwrite the " +
               $"stored record.";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the name of the schema record that failed to deserialize.
    /// </summary>
    public string RecordName { get; }

    /// <summary>
    /// Gets the size in bytes of the stored record.
    /// </summary>
    public int ByteCount { get; }

    #endregion
}
