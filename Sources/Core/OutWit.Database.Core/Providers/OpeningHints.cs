namespace OutWit.Database.Core.Providers;

/// <summary>
/// Hints about what configuration to use when opening a database.
/// </summary>
public sealed class OpeningHints
{
    public override string ToString()
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrEmpty(StoreProvider))
            parts.Add($"Store: {StoreProvider}");
        
        if (RequiresEncryption)
            parts.Add($"Encrypted: {EncryptionProvider}");
        
        if (HasTransactions)
            parts.Add("Transactions: enabled");
        
        if (HasMvcc)
            parts.Add("MVCC: enabled");
        
        if (HasFileLocking)
            parts.Add("FileLocking: enabled");

        return string.Join(", ", parts);
    }

    #region Propeties

    /// <summary>
    /// Whether the database requires encryption.
    /// </summary>
    public bool RequiresEncryption { get; init; }

    /// <summary>
    /// The encryption provider key used (empty if not encrypted).
    /// </summary>
    public string EncryptionProvider { get; init; } = "";

    /// <summary>
    /// The store provider key (e.g., "btree", "lsm").
    /// </summary>
    public string StoreProvider { get; init; } = "";

    /// <summary>
    /// Whether transactions were enabled.
    /// </summary>
    public bool HasTransactions { get; init; }

    /// <summary>
    /// Whether MVCC (Multi-Version Concurrency Control) was enabled.
    /// </summary>
    public bool HasMvcc { get; init; }

    /// <summary>
    /// Whether file locking was enabled.
    /// </summary>
    public bool HasFileLocking { get; init; }

    #endregion
}