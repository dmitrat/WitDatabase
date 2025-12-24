using Microsoft.JSInterop;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.IndexedDb;

/// <summary>
/// Extension methods for configuring WitDatabaseBuilder with IndexedDB storage.
/// </summary>
public static class WitDatabaseBuilderIndexedDbExtensions
{
    #region Storage Configuration

    /// <summary>
    /// Use IndexedDB for storage (Blazor WebAssembly).
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="databaseName">Name of the IndexedDB database.</param>
    /// <param name="jsRuntime">Blazor JS runtime (inject via @inject IJSRuntime).</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if LSM-Tree engine is selected (not compatible with IndexedDB).
    /// </exception>
    /// <remarks>
    /// IndexedDB storage is only compatible with B+Tree engine.
    /// File locking is automatically disabled as it's not applicable in browser.
    /// 
    /// Example usage in Blazor component:
    /// <code>
    /// @inject IJSRuntime JSRuntime
    /// 
    /// var db = new WitDatabaseBuilder()
    ///     .WithIndexedDbStorage("MyDatabase", JSRuntime)
    ///     .WithBTree()
    ///     .WithTransactions()
    ///     .Build();
    /// </code>
    /// </remarks>
    public static WitDatabaseBuilder WithIndexedDbStorage(
        this WitDatabaseBuilder builder, 
        string databaseName, 
        IJSRuntime jsRuntime)
    {
        return builder.WithIndexedDbStorage(databaseName, jsRuntime, builder.Options.PageSize);
    }

    /// <summary>
    /// Use IndexedDB for storage with custom page size.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="databaseName">Name of the IndexedDB database.</param>
    /// <param name="jsRuntime">Blazor JS runtime.</param>
    /// <param name="pageSize">Page size in bytes.</param>
    /// <returns>The builder for chaining.</returns>
    public static WitDatabaseBuilder WithIndexedDbStorage(
        this WitDatabaseBuilder builder, 
        string databaseName, 
        IJSRuntime jsRuntime,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jsRuntime);
        
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name cannot be empty", nameof(databaseName));

        // Validate compatibility BEFORE setting options
        ValidateIndexedDbCompatibility(builder.Options);

        // Auto-disable incompatible features
        builder.Options.EnableFileLocking = false;  // Not applicable in browser
        
        // Force BTree if no engine specified
        if (!builder.Options.UseBTree && !builder.Options.UseLsmTree)
        {
            builder.Options.UseBTree = true;
        }

        // Set storage
        builder.Options.Storage = new StorageIndexedDb(databaseName, jsRuntime, pageSize);
        builder.Options.UseMemoryStorage = false;
        builder.Options.FilePath = null;
        builder.Options.LsmDirectory = null;

        return builder;
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates that the current builder options are compatible with IndexedDB storage.
    /// </summary>
    /// <param name="options">Builder options to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if configuration is incompatible.</exception>
    private static void ValidateIndexedDbCompatibility(WitDatabaseBuilderOptions options)
    {
        var errors = new List<string>();

        // LSM-Tree is not compatible
        if (options.UseLsmTree)
        {
            errors.Add(
                "IndexedDB storage is not compatible with LSM-Tree engine. " +
                "LSM-Tree requires file system operations (multiple files, directory scanning) " +
                "which are not available in browser environment. " +
                "Use .WithBTree() instead of .WithLsmTree().");
        }

        // Check if LSM directory was specified
        if (!string.IsNullOrEmpty(options.LsmDirectory))
        {
            errors.Add(
                "IndexedDB storage cannot use LSM directory. " +
                "Remove .WithLsmTree(directory) call.");
        }

        // External transaction journal not supported
        if (options.TransactionJournal != null)
        {
            errors.Add(
                "External transaction journal is not supported with IndexedDB storage. " +
                "The built-in transaction handling will be used instead.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "IndexedDB configuration error:\n" + 
                string.Join("\n", errors.Select((e, i) => $"  {i + 1}. {e}")));
        }
    }

    #endregion
}
