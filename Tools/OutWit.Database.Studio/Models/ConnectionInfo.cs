using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.Values;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Information about a database connection.
/// </summary>
public sealed class ConnectionInfo : ModelBase
{
    #region Constants

    private const string DEFAULT_STORAGE_ENGINE = "btree";
    public const string DEFAULT_ENCRYPTION = "aes-gcm";
    public const string CHACHA20 = "chacha20-poly1305";

    #endregion

    #region Functions

    public void Reset()
    {
        FilePath = string.Empty;
        IsEncrypted = false;
        Password = null;
        IsReadOnly = false;
        StorageEngine = DEFAULT_STORAGE_ENGINE;
        EncryptionProvider = DEFAULT_ENCRYPTION;
        DisplayName = null;
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not ConnectionInfo other)
            return false;

        return FilePath.Is(other.FilePath)
            && IsEncrypted.Is(other.IsEncrypted)
            && Password.Is(other.Password)
            && IsReadOnly.Is(other.IsReadOnly)
            && StorageEngine.Is(other.StorageEngine)
            && EncryptionProvider.Is(other.EncryptionProvider)
            && DisplayName.Is(other.DisplayName);
    }

    public override ConnectionInfo Clone()
    {
        return new ConnectionInfo
        {
            FilePath = FilePath,
            IsEncrypted = IsEncrypted,
            Password = Password,
            IsReadOnly = IsReadOnly,
            StorageEngine = StorageEngine,
            EncryptionProvider = EncryptionProvider,
            DisplayName = DisplayName
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Builds the connection string from this connection info. Carries the password, so it belongs to
    /// the engine and nowhere else - never to a log, a message, a history entry or a saved list. Use
    /// <see cref="ToLogString"/> for anything a human or a file will see.
    /// </summary>
    public string BuildConnectionString()
    {
        return Build(redactSecrets: false);
    }

    /// <summary>
    /// The same connection string with the password replaced. Everything that makes a failed open
    /// diagnosable - the data source, read-only, whether encryption was asked for, the store - is kept:
    /// a log that says nothing about the attempt is a different defect from one that says too much.
    /// </summary>
    public string ToLogString()
    {
        return Build(redactSecrets: true);
    }

    private string Build(bool redactSecrets)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            throw new InvalidOperationException("FilePath must be specified before building connection string.");

        var builder = new System.Text.StringBuilder();
        builder.Append($"Data Source={FilePath}");

        if (IsReadOnly)
            builder.Append(";Mode=ReadOnly");

        if (IsEncrypted && !string.IsNullOrEmpty(Password))
        {
            // The algorithm used to be written as a constant "aes-gcm", which meant a database created
            // with ChaCha20-Poly1305 could not be opened by Studio at all - the provider was handed the
            // wrong key and answered that the password was wrong.
            builder.Append($";Encryption={EncryptionProvider}");
            builder.Append(redactSecrets ? ";Password=***" : $";Password={Password}");
        }

        if (!string.IsNullOrEmpty(StorageEngine) && StorageEngine != DEFAULT_STORAGE_ENGINE)
            builder.Append($";Store={StorageEngine}");

        return builder.ToString();
    }

    public override string ToString()
    {
        return FilePath ?? "(no database)";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the database file path.
    /// </summary>
    [Notify]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the database is encrypted.
    /// </summary>
    [Notify]
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Gets or sets the encryption password (if encrypted).
    /// </summary>
    [Notify]
    public string? Password { get; set; }

    /// <summary>
    /// The encryption provider key - <c>aes-gcm</c> or <c>chacha20-poly1305</c>.
    ///
    /// <para>
    /// It exists because it was a constant. Studio wrote <c>Encryption=aes-gcm</c> into every
    /// connection string, so a ChaCha20 database - which the engine creates perfectly well - could not
    /// be opened by Studio at all, and the failure looked like a wrong password.
    /// </para>
    /// <para>
    /// It cannot be DETECTED for an existing database: the header naming the provider is inside the
    /// encrypted page. It is a choice at creation and a remembered property of a saved connection.
    /// </para>
    /// </summary>
    [Notify]
    public string EncryptionProvider { get; set; } = DEFAULT_ENCRYPTION;

    /// <summary>
    /// Gets or sets whether to open the database in read-only mode.
    /// </summary>
    [Notify]
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the storage engine (btree, lsm).
    /// </summary>
    [Notify]
    public string StorageEngine { get; set; } = DEFAULT_STORAGE_ENGINE;

    /// <summary>
    /// Gets or sets the display name for this connection.
    /// </summary>
    [Notify]
    public string? DisplayName { get; set; }

    #endregion
}
