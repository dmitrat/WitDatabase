using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.IndexedDb;

namespace OutWit.Database.Samples.BlazorWasm.Data;

/// <summary>
/// Database service that wraps WitDatabase Core with IndexedDB storage.
/// Provides async operations for Blazor WASM compatibility.
/// </summary>
public sealed class DatabaseService : IAsyncDisposable
{
    #region Constants

    private const string DATABASE_NAME = "WitDbBlazorSample";
    private const string CONTACTS_PREFIX = "contact:";
    private const string NOTES_PREFIX = "note:";
    private const string SEQUENCE_PREFIX = "seq:";

    #endregion

    #region Fields

    private readonly IJSRuntime _jsRuntime;
    private WitDatabase? _database;
    private bool _isInitialized;

    #endregion

    #region Constructors

    public DatabaseService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the database. Must be called before any other operations.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        _database = await new WitDatabaseBuilder()
            .WithIndexedDbStorage(DATABASE_NAME, _jsRuntime)
            .WithBTree()
            .WithTransactions()
            .BuildAsync(cancellationToken);

        _isInitialized = true;
    }

    /// <summary>
    /// Ensures the database is initialized.
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized || _database == null)
            throw new InvalidOperationException("Database is not initialized. Call InitializeAsync first.");
    }

    #endregion

    #region Generic CRUD Operations

    /// <summary>
    /// Saves an entity to the database.
    /// </summary>
    public async Task<T> SaveAsync<T>(string prefix, int id, T entity, CancellationToken cancellationToken = default) where T : class
    {
        EnsureInitialized();
        
        var key = Encoding.UTF8.GetBytes($"{prefix}{id:D10}");
        var value = JsonSerializer.SerializeToUtf8Bytes(entity, JsonOptions);
        
        await _database!.PutAsync(key, value, cancellationToken);
        await _database.FlushAsync(cancellationToken);
        
        return entity;
    }

    /// <summary>
    /// Gets an entity by ID.
    /// </summary>
    public async Task<T?> GetByIdAsync<T>(string prefix, int id, CancellationToken cancellationToken = default) where T : class
    {
        EnsureInitialized();
        
        var key = Encoding.UTF8.GetBytes($"{prefix}{id:D10}");
        var value = await _database!.GetAsync(key, cancellationToken);
        
        if (value == null)
            return null;
        
        return JsonSerializer.Deserialize<T>(value, JsonOptions);
    }

    /// <summary>
    /// Gets all entities with the specified prefix.
    /// </summary>
    public async Task<List<T>> GetAllAsync<T>(string prefix, CancellationToken cancellationToken = default) where T : class
    {
        EnsureInitialized();
        
        var results = new List<T>();
        var startKey = Encoding.UTF8.GetBytes(prefix);
        var endKey = Encoding.UTF8.GetBytes(prefix + "\x7F"); // Max ASCII printable
        
        await foreach (var (_, value) in _database!.ScanAsync(startKey, endKey, cancellationToken))
        {
            var entity = JsonSerializer.Deserialize<T>(value, JsonOptions);
            if (entity != null)
                results.Add(entity);
        }
        
        return results;
    }

    /// <summary>
    /// Deletes an entity by ID.
    /// </summary>
    public async Task<bool> DeleteAsync(string prefix, int id, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        var key = Encoding.UTF8.GetBytes($"{prefix}{id:D10}");
        var result = await _database!.DeleteAsync(key, cancellationToken);
        await _database.FlushAsync(cancellationToken);
        
        return result;
    }

    /// <summary>
    /// Gets the next ID for a sequence.
    /// </summary>
    public async Task<int> GetNextIdAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        var key = Encoding.UTF8.GetBytes($"{SEQUENCE_PREFIX}{sequenceName}");
        var value = await _database!.GetAsync(key, cancellationToken);
        
        var currentId = value != null ? BitConverter.ToInt32(value, 0) : 0;
        var nextId = currentId + 1;
        
        await _database.PutAsync(key, BitConverter.GetBytes(nextId), cancellationToken);
        await _database.FlushAsync(cancellationToken);
        
        return nextId;
    }

    /// <summary>
    /// Gets the count of entities with the specified prefix.
    /// </summary>
    public async Task<int> CountAsync(string prefix, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        var count = 0;
        var startKey = Encoding.UTF8.GetBytes(prefix);
        var endKey = Encoding.UTF8.GetBytes(prefix + "\x7F");
        
        await foreach (var _ in _database!.ScanAsync(startKey, endKey, cancellationToken))
        {
            count++;
        }
        
        return count;
    }

    /// <summary>
    /// Clears all data with the specified prefix.
    /// </summary>
    public async Task ClearAsync(string prefix, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        var keysToDelete = new List<byte[]>();
        var startKey = Encoding.UTF8.GetBytes(prefix);
        var endKey = Encoding.UTF8.GetBytes(prefix + "\x7F");
        
        await foreach (var (key, _) in _database!.ScanAsync(startKey, endKey, cancellationToken))
        {
            keysToDelete.Add(key);
        }

        foreach (var key in keysToDelete)
        {
            await _database.DeleteAsync(key, cancellationToken);
        }
        
        // Reset sequence
        var seqKey = Encoding.UTF8.GetBytes($"{SEQUENCE_PREFIX}{prefix.TrimEnd(':')}");
        await _database.DeleteAsync(seqKey, cancellationToken);
        
        await _database.FlushAsync(cancellationToken);
    }

    #endregion

    #region Prefixes

    public static string ContactsPrefix => CONTACTS_PREFIX;
    public static string NotesPrefix => NOTES_PREFIX;

    #endregion

    #region JSON Options

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
            _database = null;
        }
        _isInitialized = false;
    }

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;

    #endregion
}
