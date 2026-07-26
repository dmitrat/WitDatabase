using System.Data.Common;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Connection string builder for WitDatabase connections.
/// All provider-specific parameters are passed through to the provider factories.
/// </summary>
/// <remarks>
/// <para>Core connection string properties:</para>
/// <list type="table">
/// <listheader>
/// <term>Property</term>
/// <description>Description</description>
/// </listheader>
/// <item><term>Data Source</term><description>Path to database file or ":memory:" for in-memory</description></item>
/// <item><term>Mode</term><description>Connection mode (ReadWriteCreate, ReadWrite, ReadOnly, Memory)</description></item>
/// <item><term>Store</term><description>Storage engine provider key (e.g., btree, lsm, inmemory)</description></item>
/// <item><term>Encryption</term><description>Encryption provider key (e.g., aes-gcm, chacha20-poly1305)</description></item>
/// <item><term>Password</term><description>Encryption password</description></item>
/// <item><term>User</term><description>Username for user-based encryption salt derivation</description></item>
/// <item><term>Cache</term><description>Cache provider key (e.g., clock, lru)</description></item>
/// <item><term>Journal</term><description>Journal provider key (e.g., wal, rollback)</description></item>
/// <item><term>Isolation Level</term><description>Default transaction isolation level</description></item>
/// <item><term>MVCC</term><description>Enable Multi-Version Concurrency Control</description></item>
/// <item><term>Transactions</term><description>Enable transaction support</description></item>
/// <item><term>Parallel Mode</term><description>Parallel access mode (None, Auto, Buffered, Latched, Optimistic)</description></item>
/// <item><term>Max Writers</term><description>Maximum number of parallel writers</description></item>
/// </list>
/// <para>All other parameters are passed through to provider factories via ProviderParameters.</para>
/// </remarks>
/// <example>
/// <code>
/// // Minimal - just file path
/// var cs = "Data Source=mydb.witdb";
/// 
/// // With encryption
/// var cs = "Data Source=secure.witdb;Password=secret123";
/// 
/// // LSM-Tree with custom parameters
/// var cs = "Data Source=./data;Store=lsm;MemTableSize=67108864";
/// 
/// // Full configuration with parallel writes
/// var cs = "Data Source=app.witdb;Store=btree;Encryption=aes-gcm;Password=pass;Parallel Mode=Auto";
/// </code>
/// </example>
public sealed class WitDbConnectionStringBuilder : DbConnectionStringBuilder
{
    #region Constants

    // Core ADO.NET settings (we need to know these)
    private const string KEY_DATA_SOURCE = "Data Source";
    private const string KEY_MODE = "Mode";
    private const string KEY_READ_ONLY = "Read Only";
    
    // Provider keys
    private const string KEY_STORE = "Store";
    private const string KEY_ENCRYPTION = "Encryption";
    private const string KEY_PASSWORD = "Password";
    private const string KEY_USER = "User";
    private const string KEY_CACHE = "Cache";
    private const string KEY_JOURNAL = "Journal";
    
    // Transaction settings (ADO.NET needs these for BeginTransaction)
    private const string KEY_ISOLATION_LEVEL = "Isolation Level";
    private const string KEY_MVCC = "MVCC";
    private const string KEY_TRANSACTIONS = "Transactions";
    private const string KEY_SYNCHRONOUS_COMMIT = "Synchronous Commit";
    
    // Parallel mode settings
    private const string KEY_PARALLEL_MODE = "Parallel Mode";
    private const string KEY_MAX_WRITERS = "Max Writers";
    
    // Pooling settings (ADO.NET level)
    private const string KEY_POOLING = "Pooling";
    private const string KEY_MIN_POOL_SIZE = "Min Pool Size";
    private const string KEY_MAX_POOL_SIZE = "Max Pool Size";
    private const string KEY_DEFAULT_TIMEOUT = "Default Timeout";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbConnectionStringBuilder"/> class.
    /// </summary>
    public WitDbConnectionStringBuilder()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbConnectionStringBuilder"/> class
    /// with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    public WitDbConnectionStringBuilder(string connectionString)
    {
        ConnectionString = connectionString;
    }

    #endregion

    #region Functions

    private T GetValue<T>(string key, T defaultValue)
    {
        if (!TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is T typed)
            return typed;

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue))
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(int))
                return (T)(object)int.Parse(stringValue);
            if (typeof(T) == typeof(long))
                return (T)(object)long.Parse(stringValue);
            if (typeof(T) == typeof(bool))
                return (T)(object)ParseBool(stringValue);
            if (typeof(T) == typeof(string))
                return (T)(object)stringValue;
            if (typeof(T).IsEnum)
                return (T)Enum.Parse(typeof(T), stringValue, ignoreCase: true);
        }
        catch
        {
            return defaultValue;
        }

        return defaultValue;
    }

    private static bool ParseBool(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" => true,
            "false" or "no" or "0" or "off" => false,
            _ => bool.Parse(value)
        };
    }

    private void SetValue<T>(string key, T value)
    {
        this[key] = value;
    }

    /// <summary>
    /// Validates the connection string settings for consistency.
    /// </summary>
    /// <returns>A list of validation error messages, empty if valid.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // Validate encryption settings
        if (!string.IsNullOrEmpty(Encryption) && string.IsNullOrEmpty(Password))
        {
            errors.Add("Password is required when encryption is enabled.");
        }

        // Validate pool sizes
        if (MinPoolSize > MaxPoolSize)
        {
            errors.Add($"Min Pool Size ({MinPoolSize}) cannot be greater than Max Pool Size ({MaxPoolSize}).");
        }

        // Validate data source
        if (string.IsNullOrEmpty(DataSource) && Mode != WitDbConnectionMode.Memory)
        {
            errors.Add("Data Source is required unless Mode is Memory.");
        }

        // Validate max writers
        if (MaxWriters < 1)
        {
            errors.Add("Max Writers must be at least 1.");
        }

        return errors;
    }

    /// <summary>
    /// Throws an exception if the connection string is not valid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public void ThrowIfInvalid()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid connection string: {string.Join("; ", errors)}");
        }
    }

    /// <summary>
    /// Gets all parameters from the connection string that can be passed to provider factories.
    /// Excludes core ADO.NET properties like Data Source, Mode, etc.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> GetProviderParameters()
    {
        var coreKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            KEY_DATA_SOURCE, KEY_MODE, KEY_READ_ONLY,
            KEY_STORE, KEY_ENCRYPTION, KEY_PASSWORD, KEY_USER, KEY_CACHE, KEY_JOURNAL,
            KEY_ISOLATION_LEVEL, KEY_MVCC, KEY_TRANSACTIONS, KEY_SYNCHRONOUS_COMMIT,
            KEY_PARALLEL_MODE, KEY_MAX_WRITERS,
            KEY_POOLING, KEY_MIN_POOL_SIZE, KEY_MAX_POOL_SIZE, KEY_DEFAULT_TIMEOUT
        };

        foreach (string key in Keys)
        {
            if (!coreKeys.Contains(key))
            {
                yield return new KeyValuePair<string, object?>(key, this[key]);
            }
        }
    }

    #endregion

    #region Core Properties

    /// <summary>
    /// Gets or sets the path to the database file.
    /// Use ":memory:" for an in-memory database.
    /// </summary>
    public string? DataSource
    {
        get => GetValue<string?>(KEY_DATA_SOURCE, null);
        set => SetValue(KEY_DATA_SOURCE, value);
    }

    /// <summary>
    /// Gets or sets the connection mode.
    /// </summary>
    public WitDbConnectionMode Mode
    {
        get => GetValue(KEY_MODE, WitDbConnectionMode.ReadWriteCreate);
        set => SetValue(KEY_MODE, value);
    }

    /// <summary>
    /// Gets or sets whether the connection is read-only.
    /// </summary>
    public bool ReadOnly
    {
        get => GetValue(KEY_READ_ONLY, false);
        set => SetValue(KEY_READ_ONLY, value);
    }

    #endregion

    #region Provider Keys

    /// <summary>
    /// Gets or sets the storage engine provider key.
    /// Examples: "btree", "lsm", "inmemory", or any registered provider.
    /// </summary>
    public string? Store
    {
        get => GetValue<string?>(KEY_STORE, null);
        set => SetValue(KEY_STORE, value);
    }

    /// <summary>
    /// Gets or sets the encryption provider key.
    /// Examples: "aes-gcm", "chacha20-poly1305", or any registered provider.
    /// </summary>
    public string? Encryption
    {
        get => GetValue<string?>(KEY_ENCRYPTION, null);
        set => SetValue(KEY_ENCRYPTION, value);
    }

    /// <summary>
    /// Gets or sets the encryption password.
    /// </summary>
    public string? Password
    {
        get => GetValue<string?>(KEY_PASSWORD, null);
        set => SetValue(KEY_PASSWORD, value);
    }

    /// <summary>
    /// Gets or sets the username for user-based encryption salt derivation.
    /// </summary>
    public string? User
    {
        get => GetValue<string?>(KEY_USER, null);
        set => SetValue(KEY_USER, value);
    }

    /// <summary>
    /// Gets or sets the cache provider key.
    /// Examples: "clock", "lru", or any registered provider.
    /// </summary>
    public string? Cache
    {
        get => GetValue<string?>(KEY_CACHE, null);
        set => SetValue(KEY_CACHE, value);
    }

    /// <summary>
    /// Gets or sets the transaction journal provider key.
    /// Examples: "wal", "rollback", or any registered provider.
    /// </summary>
    public string? Journal
    {
        get => GetValue<string?>(KEY_JOURNAL, null);
        set => SetValue(KEY_JOURNAL, value);
    }

    #endregion

    #region Transaction Properties

    /// <summary>
    /// Gets or sets the default isolation level for transactions.
    /// </summary>
    public WitDbIsolationLevel IsolationLevel
    {
        get => GetValue(KEY_ISOLATION_LEVEL, WitDbIsolationLevel.ReadCommitted);
        set => SetValue(KEY_ISOLATION_LEVEL, value);
    }

    /// <summary>
    /// Gets or sets whether MVCC (Multi-Version Concurrency Control) is enabled.
    /// Default is true.
    /// </summary>
    public bool Mvcc
    {
        get => GetValue(KEY_MVCC, true);
        set => SetValue(KEY_MVCC, value);
    }

    /// <summary>
    /// Gets or sets whether a commit is flushed to storage before it returns. Default is true.
    /// </summary>
    /// <remarks>
    /// Turning this off makes a successful COMMIT survive only a clean shutdown, not a process kill.
    /// Reasonable for a disposable test database or a re-runnable bulk import; never a good way to
    /// make a benchmark look faster than the guarantee it provides.
    /// </remarks>
    public bool SynchronousCommit
    {
        get => GetValue(KEY_SYNCHRONOUS_COMMIT, true);
        set => SetValue(KEY_SYNCHRONOUS_COMMIT, value);
    }

    /// <summary>
    /// Gets or sets whether transaction support is enabled.
    /// Default is true.
    /// </summary>
    public bool Transactions
    {
        get => GetValue(KEY_TRANSACTIONS, true);
        set => SetValue(KEY_TRANSACTIONS, value);
    }

    #endregion

    #region Parallel Mode Properties

    /// <summary>
    /// Gets or sets the parallel access mode.
    /// Default is None (single-threaded behavior).
    /// </summary>
    public WitDbParallelMode ParallelMode
    {
        get => GetValue(KEY_PARALLEL_MODE, WitDbParallelMode.None);
        set => SetValue(KEY_PARALLEL_MODE, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of parallel writers.
    /// Only applicable when ParallelMode is not None.
    /// Default is number of processors.
    /// </summary>
    public int MaxWriters
    {
        get => GetValue(KEY_MAX_WRITERS, Environment.ProcessorCount);
        set => SetValue(KEY_MAX_WRITERS, value);
    }

    #endregion

    #region Pooling Properties

    /// <summary>
    /// Gets or sets whether connection pooling is enabled.
    /// Default is false.
    /// </summary>
    public bool Pooling
    {
        get => GetValue(KEY_POOLING, false);
        set => SetValue(KEY_POOLING, value);
    }

    /// <summary>
    /// Gets or sets the minimum pool size.
    /// Default is 1.
    /// </summary>
    public int MinPoolSize
    {
        get => GetValue(KEY_MIN_POOL_SIZE, 1);
        set => SetValue(KEY_MIN_POOL_SIZE, value);
    }

    /// <summary>
    /// Gets or sets the maximum pool size.
    /// Default is 100.
    /// </summary>
    public int MaxPoolSize
    {
        get => GetValue(KEY_MAX_POOL_SIZE, 100);
        set => SetValue(KEY_MAX_POOL_SIZE, value);
    }

    /// <summary>
    /// Gets or sets the default command timeout in seconds.
    /// Default is 30 seconds.
    /// </summary>
    public int DefaultTimeout
    {
        get => GetValue(KEY_DEFAULT_TIMEOUT, 30);
        set => SetValue(KEY_DEFAULT_TIMEOUT, value);
    }

    #endregion
}

/// <summary>
/// Specifies the connection mode for WitDatabase.
/// </summary>
public enum WitDbConnectionMode
{
    /// <summary>
    /// Open database for reading and writing; create if it doesn't exist.
    /// </summary>
    ReadWriteCreate,

    /// <summary>
    /// Open database for reading and writing; fail if it doesn't exist.
    /// </summary>
    ReadWrite,

    /// <summary>
    /// Open database for reading only.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Create an in-memory database.
    /// </summary>
    Memory
}

/// <summary>
/// Specifies the transaction isolation level for WitDatabase.
/// </summary>
public enum WitDbIsolationLevel
{
    /// <summary>
    /// Allows dirty reads. Lowest isolation, highest concurrency.
    /// </summary>
    ReadUncommitted,

    /// <summary>
    /// Only committed data is visible. Prevents dirty reads.
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// Read locks are held for the duration of the transaction.
    /// </summary>
    RepeatableRead,

    /// <summary>
    /// Highest isolation level. Full transaction serialization.
    /// </summary>
    Serializable,

    /// <summary>
    /// Snapshot isolation using MVCC.
    /// </summary>
    Snapshot
}

/// <summary>
/// Specifies the parallel access mode for WitDatabase connections.
/// </summary>
public enum WitDbParallelMode
{
    /// <summary>
    /// No parallelism. Single global lock for all operations.
    /// </summary>
    None,

    /// <summary>
    /// Automatic mode selection based on store type.
    /// </summary>
    Auto,

    /// <summary>
    /// Thread-local write buffers with background merge.
    /// </summary>
    Buffered,

    /// <summary>
    /// Page-level latching for fine-grained concurrency.
    /// </summary>
    Latched,

    /// <summary>
    /// Optimistic concurrency with minimal locking.
    /// </summary>
    Optimistic
}
