using OutWit.Common.Abstract;
using OutWit.Common.Aspects;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Information about a database connection.
/// </summary>
public sealed class ConnectionInfo : ModelBase
{
    #region Constants

    private const string DEFAULT_STORAGE_ENGINE = "btree";

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not ConnectionInfo other)
            return false;

        return FilePath == other.FilePath
            && IsEncrypted == other.IsEncrypted
            && Password == other.Password
            && IsReadOnly == other.IsReadOnly
            && StorageEngine == other.StorageEngine;
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
            DisplayName = DisplayName
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Builds the connection string from this connection info.
    /// </summary>
    public string BuildConnectionString()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append($"Data Source={FilePath}");

        if (IsReadOnly)
            builder.Append(";Mode=ReadOnly");

        if (IsEncrypted && !string.IsNullOrEmpty(Password))
        {
            builder.Append(";Encryption=aes-gcm");
            builder.Append($";Password={Password}");
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
