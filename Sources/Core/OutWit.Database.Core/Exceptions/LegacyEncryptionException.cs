namespace OutWit.Database.Core.Exceptions;

/// <summary>
/// Thrown when a database written under the encryption scheme that preceded the crypto preamble is
/// opened without asking for that scheme by name.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="InvalidOperationException"/> because that is what it used to be, and
/// code that catches the base type keeps working. It has a type of its own so that a CALLER can act
/// on it - Studio recognises it and offers the conversion - <b>without matching the message text</b>,
/// which is the shape that breaks the moment a word changes.
/// </para>
/// <para>
/// The way out is on the exception rather than only in the sentence: <see cref="IsDirectory"/> says
/// whether it was a paged database or an LSM directory, and the message names both routes - open it
/// with the version that wrote it, or convert it by changing its password.
/// </para>
/// </remarks>
public sealed class LegacyEncryptionException : InvalidOperationException
{
    #region Constructors

    public LegacyEncryptionException(string message, bool isDirectory)
        : base(message)
    {
        IsDirectory = isDirectory;
    }

    #endregion

    #region Properties

    /// <summary>True for an LSM directory, false for a paged database file.</summary>
    public bool IsDirectory { get; }

    #endregion
}
