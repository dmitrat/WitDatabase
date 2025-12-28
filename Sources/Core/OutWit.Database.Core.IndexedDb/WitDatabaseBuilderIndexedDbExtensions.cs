using Microsoft.JSInterop;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.IndexedDb.Indexes;
using OutWit.Database.Core.Stores;

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
    /// Secondary index factory is automatically configured to use IndexedDB.
    /// 
    /// For Blazor WASM, use <see cref="WitDatabaseBuilder.BuildAsync"/> for proper async initialization:
    /// <code>
    /// @inject IJSRuntime JSRuntime
    /// 
    /// var db = await new WitDatabaseBuilder()
    ///     .WithIndexedDbStorage("MyDatabase", JSRuntime)
    ///     .WithBTree()
    ///     .WithTransactions()
    ///     .BuildAsync();  // Use BuildAsync for WASM!
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

        // Register validation event to catch late configuration changes
        builder.OnValidating += ValidateIndexedDbOnBuild;

        // Auto-disable incompatible features
        builder.Options.TransactionParameters.Set("fileLocking", false);  // Not applicable in browser
        
        // Force BTree if no engine specified
        if (!builder.Options.UseBTree && !builder.Options.UseLsmTree)
        {
            builder.Options.StoreProviderKey = StoreBTree.PROVIDER_KEY;
        }

        // Set storage
        builder.Options.CustomStorage = new StorageIndexedDb(databaseName, jsRuntime, pageSize);
        builder.Options.StoreParameters.Remove("useMemory");
        builder.Options.StoreParameters.Remove("filePath");
        builder.Options.StoreParameters.Remove("directory");

        // Auto-configure secondary index factory for IndexedDB
        // Use database name with "_indexes" suffix to avoid conflicts
        var indexDbName = databaseName + "_indexes";
        builder.Options.SecondaryIndexFactory = new SecondaryIndexFactoryIndexedDb(jsRuntime, indexDbName);

        return builder;
    }

    #endregion

    #region Index Configuration

    /// <summary>
    /// Explicitly configures IndexedDB-based secondary indexes.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="jsRuntime">Blazor JS runtime.</param>
    /// <param name="indexDatabaseName">Name of the IndexedDB database for indexes.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This method is typically not needed when using WithIndexedDbStorage,
    /// as it automatically configures the index factory. Use this method
    /// when you need to customize the index database name or when using
    /// a different primary storage with IndexedDB indexes.
    /// </remarks>
    public static WitDatabaseBuilder WithIndexedDbIndexes(
        this WitDatabaseBuilder builder,
        IJSRuntime jsRuntime,
        string indexDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jsRuntime);
        
        if (string.IsNullOrWhiteSpace(indexDatabaseName))
            throw new ArgumentException("Index database name cannot be empty", nameof(indexDatabaseName));

        builder.Options.SecondaryIndexFactory = new SecondaryIndexFactoryIndexedDb(jsRuntime, indexDatabaseName);

        return builder;
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validation handler for OnValidating event.
    /// Catches configuration changes made after WithIndexedDbStorage was called.
    /// </summary>
    private static void ValidateIndexedDbOnBuild(WitDatabaseBuilderOptions options)
    {
        // Only validate if IndexedDB storage is configured
        if (options.CustomStorage is not StorageIndexedDb)
            return;

        ValidateIndexedDbCompatibility(options);
    }

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
        if (options.CustomJournal != null || !string.IsNullOrEmpty(options.JournalProviderKey))
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
