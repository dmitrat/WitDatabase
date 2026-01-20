# OutWit.Database.Core.IndexedDb

IndexedDB storage provider for WitDatabase - enables **Blazor WebAssembly** support.

This package allows WitDatabase to run entirely in the browser with data persisted to IndexedDB.

---

## Installation

```xml
<PackageReference Include="OutWit.Database.Core.IndexedDb" Version="1.0.0" />
```

Add the JavaScript files to your `index.html`:

```html
<script src="_content/OutWit.Database.Core.IndexedDb/witdb-indexeddb.js"></script>
<script src="_content/OutWit.Database.Core.IndexedDb/witdb-indexeddb-index.js"></script>
```

---

## Quick Start

### Basic Usage

```razor
@page "/database-demo"
@inject IJSRuntime JSRuntime
@using OutWit.Database.Core.Builder
@using OutWit.Database.Core.IndexedDb

<h3>Database Demo</h3>

@code {
    private WitDatabase? _db;

    protected override async Task OnInitializedAsync()
    {
        _db = new WitDatabaseBuilder()
            .WithIndexedDbStorage("MyAppDatabase", JSRuntime)
            .WithBTree()
            .WithTransactions()
            .Build();
        
        // Initialize storage (opens IndexedDB)
        await (_db.Store as StorageIndexedDb)?.InitializeAsync()!;
    }

    private async Task SaveData()
    {
        var key = Encoding.UTF8.GetBytes("user:1");
        var value = Encoding.UTF8.GetBytes("{\"name\":\"John\"}");
        
        await _db!.PutAsync(key, value);
    }

    private async Task LoadData()
    {
        var key = Encoding.UTF8.GetBytes("user:1");
        var value = await _db!.GetAsync(key);
        
        if (value != null)
        {
            var json = Encoding.UTF8.GetString(value);
            Console.WriteLine(json);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_db != null)
        {
            await _db.DisposeAsync();
        }
    }
}
```

### With Encryption

```csharp
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("SecureDatabase", JSRuntime)
    .WithBTree()
    .WithEncryption("user-password")  // AES-GCM encryption
    .WithTransactions()
    .Build();
```

### With MVCC

```csharp
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("MvccDatabase", JSRuntime)
    .WithBTree()
    .WithMvcc()
    .WithDefaultIsolationLevel(IsolationLevel.Snapshot)
    .Build();
```

---

## Compatibility

### Storage Engines

| Engine | Compatible | Notes |
|--------|-----------|-------|
| B+Tree (WithBTree) | Yes | Recommended, full support |
| LSM-Tree (WithLsmTree) | No | Requires file system |
| In-Memory | Yes | No persistence |

### Features

| Feature | Compatible | Notes |
|---------|-----------|-------|
| Basic CRUD | Yes | Full support |
| Transactions | Yes | Via TransactionalStore |
| MVCC | Yes | All isolation levels |
| Savepoints | Yes | Full support |
| Encryption | Yes | AES-GCM, BouncyCastle |
| Secondary Indexes | Yes | Full support via IndexedDB |
| File Locking | No | Not applicable |
| WAL/Journal | No | Single-file model |

### Browsers

| Browser | Supported |
|---------|-----------|
| Chrome 80+ | Yes |
| Firefox 75+ | Yes |
| Edge 80+ | Yes |
| Safari 14+ | Yes |

---

## Secondary Indexes

Secondary indexes are automatically configured when using `WithIndexedDbStorage()`. Each index uses a separate object store within a dedicated IndexedDB database.

### Automatic Configuration

```csharp
// Indexes are automatically configured
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("MyDatabase", JSRuntime)
    .WithBTree()
    .Build();

// Index factory is automatically set up for IndexedDB
// Indexes will use "MyDatabase_indexes" database
```

### Custom Index Database

```csharp
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("MyDatabase", JSRuntime)
    .WithIndexedDbIndexes(JSRuntime, "CustomIndexDb")  // Override default
    .WithBTree()
    .Build();
```

### Using Indexes

```csharp
// Create index manager
var indexFactory = new SecondaryIndexFactoryIndexedDb(JSRuntime, "MyDatabase_indexes");
var indexManager = new IndexManager(indexFactory);

// Create a secondary index
var emailIndex = indexManager.CreateIndex("idx_email", isUnique: true);

// Add entries
emailIndex.Add(emailBytes, primaryKeyBytes);

// Find by index
var primaryKeys = emailIndex.Find(emailBytes);
```

---

## API Reference

### WitDatabaseBuilder Extensions

```csharp
// Basic IndexedDB storage (auto-configures indexes)
builder.WithIndexedDbStorage(string databaseName, IJSRuntime jsRuntime)

// With custom page size
builder.WithIndexedDbStorage(string databaseName, IJSRuntime jsRuntime, int pageSize)

// Custom index database
builder.WithIndexedDbIndexes(IJSRuntime jsRuntime, string indexDatabaseName)
```

### StorageIndexedDb

```csharp
// Create storage directly
var storage = new StorageIndexedDb("DatabaseName", jsRuntime);

// Async initialization (recommended)
await storage.InitializeAsync();

// Check if database exists
bool exists = await storage.DatabaseExistsAsync();

// Properties
storage.DatabaseName    // IndexedDB database name
storage.PageSize        // Page size in bytes
storage.PageCount       // Total number of pages
storage.IsInitialized   // Whether storage is initialized
```

### SecondaryIndexFactoryIndexedDb

```csharp
// Create index factory
var factory = new SecondaryIndexFactoryIndexedDb(jsRuntime, "DatabaseName");

// Create indexes
var uniqueIndex = factory.CreateIndex("idx_unique", isUnique: true);
var nonUniqueIndex = factory.CreateIndex("idx_category", isUnique: false);

// Factory properties
factory.ProviderKey  // "indexeddb"
```

### IndexedDbInterop

Low-level IndexedDB operations (advanced usage):

```csharp
var interop = new IndexedDbInterop(jsRuntime, "DatabaseName");

await interop.OpenAsync();
await interop.WritePageAsync(0, pageData);
var data = await interop.ReadPageAsync(0);
await interop.CloseAsync();
```

---

## Limitations

1. **No LSM-Tree Support**
   - LSM-Tree requires file system operations
   - Use B+Tree instead

2. **Single-Tab Access**
   - IndexedDB can be accessed from multiple tabs
   - But WitDatabase does not coordinate between tabs
   - Use single-tab or implement your own coordination

3. **Storage Quota**
   - Browser limits IndexedDB storage (typically 50-100MB minimum)
   - Check available quota with `navigator.storage.estimate()`

4. **Sync Operations**
   - Sync methods use async bridge internally
   - Prefer async methods for better performance

---

## Error Handling

### Configuration Errors

```csharp
try
{
    var db = new WitDatabaseBuilder()
        .WithIndexedDbStorage("MyDatabase", JSRuntime)
        .WithLsmTree()  // ERROR!
        .Build();
}
catch (InvalidOperationException ex)
{
    // "IndexedDB storage is not compatible with LSM-Tree engine..."
}
```

### Page Size Mismatch

```csharp
try
{
    // First open with 4096
    var db1 = new WitDatabaseBuilder()
        .WithIndexedDbStorage("MyDatabase", JSRuntime, pageSize: 4096)
        .Build();
    db1.Dispose();
    
    // Try to open with different page size
    var db2 = new WitDatabaseBuilder()
        .WithIndexedDbStorage("MyDatabase", JSRuntime, pageSize: 8192)  // ERROR!
        .Build();
}
catch (InvalidOperationException ex)
{
    // "Database was created with page size 4096, but 8192 was requested..."
}
```

---

## Troubleshooting

### JavaScript Not Loaded

If you see errors about `witDb` or `witDbIndex` being undefined:

1. Ensure both script tags are in `index.html`
2. Make sure they are loaded before your Blazor app starts
3. Check browser console for script loading errors

### IndexedDB Not Available

```csharp
// Check if IndexedDB is available
if (window.indexedDB == null)
{
    // IndexedDB not supported or blocked
}
```

### Quota Exceeded

```csharp
try
{
    await db.PutAsync(key, largeValue);
}
catch (JSException ex) when (ex.Message.Contains("quota"))
{
    // Storage quota exceeded
    // Consider cleanup or ask user to allow more storage
}
```

---

## Performance Tips

1. **Use Async Methods**
   ```csharp
   // Preferred
   await db.PutAsync(key, value);
   
   // Avoid when possible (uses sync bridge)
   db.Put(key, value);
   ```

2. **Batch Operations**
   ```csharp
   using var tx = db.BeginTransaction();
   foreach (var item in items)
   {
       tx.Put(item.Key, item.Value);
   }
   tx.Commit();
   ```

3. **Appropriate Page Size**
   - Default 4096 is good for small records
   - Use 8192-16384 for larger records

---

## See Also

- [OutWit.Database.Core](../OutWit.Database.Core/) - Core database library
- [OutWit.Database.Core.BouncyCastle](../OutWit.Database.Core.BouncyCastle/) - ChaCha20 encryption
- [MDN: IndexedDB](https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API)
- [Blazor JS Interop](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/)
- [ROADMAP.md](ROADMAP.md) - Version 2.0 planned features
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
