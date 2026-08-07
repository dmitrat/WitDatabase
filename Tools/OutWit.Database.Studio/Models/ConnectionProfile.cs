using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.Values;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// A saved connection (WS-68): what "recent files" becomes once several databases can be open at once.
///
/// <para>
/// <b>There is no password here, and there is no field for one.</b> The list is an ordinary JSON file
/// beside the settings; a password in it would be a password on disk in clear text, which is the same
/// defect as B1 - the one that put one in the log file - with a longer life. What is kept is
/// <see cref="PasswordIsStored"/>, a note that one is in the operating system's credential store, so
/// the list can say "this one has a password saved" without holding it.
/// </para>
/// <para>
/// The credential store itself is still deferred - see the phase document. Until it exists this flag
/// is only ever false, and <c>ConnectionProfileTests</c> asserts that no password can reach the file
/// whatever anyone sets.
/// </para>
/// </summary>
public sealed class ConnectionProfile : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not ConnectionProfile other)
            return false;

        return Name.Is(other.Name)
            && Path.Is(other.Path)
            && ColorIndex.Is(other.ColorIndex)
            && IsReadOnly.Is(other.IsReadOnly)
            && StorageEngine.Is(other.StorageEngine)
            && EncryptionProvider.Is(other.EncryptionProvider)
            && IsEncrypted.Is(other.IsEncrypted)
            && PasswordIsStored.Is(other.PasswordIsStored);
    }

    public override ConnectionProfile Clone()
    {
        return new ConnectionProfile
        {
            Name = Name,
            Path = Path,
            ColorIndex = ColorIndex,
            IsReadOnly = IsReadOnly,
            StorageEngine = StorageEngine,
            EncryptionProvider = EncryptionProvider,
            IsEncrypted = IsEncrypted,
            PasswordIsStored = PasswordIsStored
        };
    }

    #endregion

    #region Functions

    /// <summary>Everything the dialogs need, minus the password, which is asked for at the time.</summary>
    public ConnectionInfo ToConnectionInfo()
    {
        return new ConnectionInfo
        {
            FilePath = Path,
            DisplayName = Name,
            ColorIndex = ColorIndex,
            IsReadOnly = IsReadOnly,
            StorageEngine = StorageEngine,
            EncryptionProvider = EncryptionProvider,
            IsEncrypted = IsEncrypted
        };
    }

    /// <summary>The profile a connection that was just opened leaves behind.</summary>
    public static ConnectionProfile From(ConnectionInfo connection)
    {
        return new ConnectionProfile
        {
            Name = connection.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(connection.FilePath),
            Path = connection.FilePath,
            ColorIndex = connection.ColorIndex,
            IsReadOnly = connection.IsReadOnly,
            StorageEngine = connection.StorageEngine,
            EncryptionProvider = connection.EncryptionProvider,
            IsEncrypted = connection.IsEncrypted
        };
    }

    #endregion

    #region Properties

    [Notify]
    public string Name { get; set; } = string.Empty;

    [Notify]
    public string Path { get; set; } = string.Empty;

    [Notify]
    public int ColorIndex { get; set; } = ConnectionInfo.NO_COLOUR;

    [Notify]
    public bool IsReadOnly { get; set; }

    [Notify]
    public string StorageEngine { get; set; } = "btree";

    [Notify]
    public string EncryptionProvider { get; set; } = ConnectionInfo.DEFAULT_ENCRYPTION;

    [Notify]
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Whether a password for this connection is in the operating system's credential store. A note,
    /// never the password.
    /// </summary>
    [Notify]
    public bool PasswordIsStored { get; set; }

    #endregion
}
