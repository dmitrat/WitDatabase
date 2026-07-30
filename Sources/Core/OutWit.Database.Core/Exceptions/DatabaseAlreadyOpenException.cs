namespace OutWit.Database.Core.Exceptions;

/// <summary>
/// Thrown when a database is already open and a second engine tries to open it.
/// </summary>
/// <remarks>
/// WitDatabase is <b>single-process, one engine per database</b> by design - it is a file database,
/// and two engines over one file each keep their own cache, memtable and log with nothing
/// coordinating them. Phase 5 measured what that costs when it is allowed: two engines over one LSM
/// directory diverged, one seeing a row the other could not.
///
/// Before 5.0.0 the limit was enforced only as a side effect of file-sharing modes, which meant it
/// was enforced inconsistently: a B+Tree database refused the second opener on every platform, but an
/// LSM database refused it only on Windows and only when the write-ahead log was switched on. This
/// exception replaces that accident with a deliberate guard, and replaces the raw
/// <see cref="IOException"/> - which carried an operating-system sharing message - with something a
/// caller can catch and act on.
///
/// <b>Several connections in one process are a supported shape</b> and do not produce this exception:
/// they share the one engine. What this exception reports is a second <i>engine</i>, which in practice
/// means a second process.
/// </remarks>
public sealed class DatabaseAlreadyOpenException : Exception
{
    #region Constructors

    /// <summary>
    /// Creates the exception for a database that could not be locked.
    /// </summary>
    /// <param name="databasePath">Path of the database file or directory.</param>
    /// <param name="innerException">The failure that prevented the lock being taken, if any.</param>
    public DatabaseAlreadyOpenException(string databasePath, Exception? innerException = null)
        : base(FormatMessage(databasePath), innerException)
    {
        DatabasePath = databasePath;
    }

    #endregion

    #region Functions

    private static string FormatMessage(string databasePath) =>
        $"The database '{databasePath}' is already open. WitDatabase allows one engine per database: "
        + "it is single-process by design, and two engines over one database keep separate caches and "
        + "logs with nothing coordinating them. Several connections in the same process are supported "
        + "and share one engine; a second process is not. If the previous owner did not shut down "
        + "cleanly, the lock is released by the operating system when that process exits.";

    #endregion

    #region Properties

    /// <summary>
    /// Gets the path of the database that is already open.
    /// </summary>
    public string DatabasePath { get; }

    #endregion
}
