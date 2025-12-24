# OutWit.Database.Core

**WitDatabase Core** - High-performance embedded key-value storage engine for .NET.

This library provides the foundation for WitDatabase, including B+Tree and LSM-Tree storage engines, MVCC transactions, encryption, and a modular architecture for extensibility.

---

## Overview

OutWit.Database.Core is a production-ready embedded database engine designed for:

- **High Performance** - Optimized B+Tree and LSM-Tree implementations
- **ACID Compliance** - Full transaction support with multiple isolation levels
- **Security** - Built-in AES-GCM encryption with pluggable crypto providers
- **Extensibility** - Modular provider architecture for custom components
- **Concurrency** - MVCC, row-level locking, deadlock detection
- **Cross-Platform** - Works on Windows, Linux, macOS, and **Blazor WebAssembly**

### Key Features

- **Storage Engines**: B+Tree (read-optimized) and LSM-Tree (write-optimized)
- **MVCC**: Multi-Version Concurrency Control with snapshot isolation
- **5 Isolation Levels**: ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, Snapshot
- **Row-Level Locking**: FOR UPDATE, FOR SHARE, NOWAIT, SKIP LOCKED
- **Transactions**: Savepoints, rollback, concurrent transactions
- **Encryption**: AES-GCM (built-in), ChaCha20-Poly1305 (via BouncyCastle)
- **Secondary Indexes**: Unique and non-unique, with auto-persistence
- **WAL & Recovery**: Write-ahead logging, crash recovery
- **Blazor WASM**: IndexedDB storage provider for browser-based apps
- **.NET 9/10**: Targets latest .NET versions

---

## Installation

```xml
<PackageReference Include="OutWit.Database.Core" Version="1.0.0" />
```

For ChaCha20-Poly1305 encryption:
```xml
<PackageReference Include="OutWit.Database.Core.BouncyCastle" Version="1.0.0" />
```

For Blazor WebAssembly (IndexedDB storage):
```xml
<PackageReference Include="OutWit.Database.Core.IndexedDb" Version="1.0.0" />
```

---

## Quick Start

### Create Database

```csharp
using OutWit.Database.Core.Builder;

// Simple file-based database
var db = WitDatabase.Create("mydata.db");

// With encryption
var db = WitDatabase.Create("mydata.db", "password123");

// In-memory database
var db = WitDatabase.CreateInMemory();
```

### Basic Operations

```csharp
// Put
db.Put("user:1", Encoding.UTF8.GetBytes("{\"name\":\"John\"}"));

// Get
var value = db.Get("user:1");

// Delete
db.Delete("user:1");

// Scan range
foreach (var (key, value) in db.Scan(startKey, endKey))
{
    Console.WriteLine($"{Encoding.UTF8.GetString(key)}: {Encoding.UTF8.GetString(value)}");
}

// Flush to disk
db.Flush();
```

### Transactions

```csharp
using var tx = db.BeginTransaction();

tx.Put(key1, value1);
tx.Put(key2, value2);

// Commit or rollback
tx.Commit();
// tx.Rollback();
```

### Isolation Levels

```csharp
using var tx = db.BeginTransaction(IsolationLevel.Serializable);
// ...
tx.Commit();
```

### Row-Level Locking

```csharp
using var tx = db.BeginTransaction(IsolationLevel.Snapshot);

// Exclusive lock (FOR UPDATE)
var value = ((IMvccTransaction)tx).GetForUpdate(key);

// Shared lock (FOR SHARE)  
var value = ((IMvccTransaction)tx).GetForShare(key);

// Non-blocking (NOWAIT)
var value = ((IMvccTransaction)tx).GetForUpdate(key, RowLockWaitMode.NoWait);

// Skip if locked (SKIP LOCKED)
var value = ((IMvccTransaction)tx).GetForUpdate(key, RowLockWaitMode.SkipLocked);

tx.Commit();
```

### Savepoints

```csharp
using var tx = db.BeginTransaction(IsolationLevel.Snapshot);

tx.Put(key1, value1);
var savepoint = ((ITransactionWithSavepoints)tx).CreateSavepoint("sp1");

tx.Put(key2, value2);
// Oops, rollback to savepoint
((ITransactionWithSavepoints)tx).RollbackToSavepoint(savepoint);

tx.Commit(); // Only key1 is committed
```

### Secondary Indexes

```csharp
// Create index
var index = db.CreateIndex("email_idx", isUnique: true);

// Add entries
index.Add(Encoding.UTF8.GetBytes("john@example.com"), primaryKey);

// Lookup
var primaryKey = index.Get(Encoding.UTF8.GetBytes("john@example.com"));

// Range scan
foreach (var (indexKey, pk) in index.Scan(startKey, endKey))
{
    // ...
}
```

---

## Builder API

The fluent builder provides full control over database configuration:

```csharp
var db = new WitDatabaseBuilder()
    // Storage
    .WithFilePath("data.db")           // File storage
    // .WithMemoryStorage()             // In-memory
    // .WithStorage(customStorage)      // Custom IStorage
    
    // Engine
    .WithBTree()                        // B+Tree (read-optimized)
    // .WithLsmTree()                   // LSM-Tree (write-optimized)
    // .WithLsmTree(opts => { ... })    // LSM with custom options
    // .WithStore(customStore)          // Custom IKeyValueStore
    
    // Encryption
    .WithEncryption("password")         // AES-GCM with password
    // .WithAesEncryption(key256)       // AES-GCM with raw key
    // .WithEncryption(cryptoProvider)  // Custom ICryptoProvider
    
    // Transactions
    .WithTransactions()                 // Enable transactions
    .WithMvcc()                         // Enable MVCC
    .WithDefaultIsolationLevel(IsolationLevel.Snapshot)
    
    // Concurrency
    .WithFileLocking()                  // Enable file locking
    .WithLockTimeout(TimeSpan.FromSeconds(30))
    
    // Performance
    .WithPageSize(8192)                 // Page size (4KB-64KB)
    .WithCacheSize(2000)                // Cache size in pages
    
    // Indexes
    .WithIndexDirectory("./indexes")    // Custom index directory
    // .WithSecondaryIndexFactory(factory) // Custom index factory
    
    .Build();
```

---

## Architecture

### Project Structure

```
OutWit.Database.Core/
|-- Builder/                    # Fluent builder API
|   |-- WitDatabase.cs         # Main database class
|   |-- WitDatabaseBuilder.cs  # Configuration builder
|   +-- WitDatabaseBuilderExtensions.cs
|-- Interfaces/                 # Extensibility contracts
|   |-- IKeyValueStore.cs      # Key-value store interface
|   |-- IStorage.cs            # Storage backend interface
|   |-- ICryptoProvider.cs     # Encryption provider interface
|   |-- IPageCache.cs          # Cache provider interface
|   |-- ITransactionJournal.cs # Journal provider interface
|   |-- ITransaction.cs        # Transaction interface
|   |-- IMvccTransaction.cs    # MVCC transaction interface
|   |-- ISecondaryIndex.cs     # Secondary index interface
|   +-- IProvider.cs           # Base provider interface
|-- Stores/                     # Storage engine implementations
|   |-- StoreBTree.cs          # B+Tree implementation
|   |-- StoreLsm.cs            # LSM-Tree implementation
|   +-- StoreInMemory.cs       # In-memory store
|-- Tree/                       # B+Tree internals
|   |-- BTree.cs               # B+Tree operations
|   +-- BTreeNode.*.cs         # Node operations (split files)
|-- LSM/                        # LSM-Tree internals
|   |-- MemTable.cs            # In-memory sorted table
|   |-- SSTableBuilder.cs      # SSTable writer
|   |-- SSTableReader.cs       # SSTable reader
|   |-- WriteAheadLog.cs       # LSM WAL
|   |-- Compactor.cs           # Background compaction
|   +-- BloomFilter.cs         # Probabilistic lookup
|-- Storage/                    # Storage backends
|   |-- StorageFile.cs         # File-based storage
|   |-- StorageMemory.cs       # In-memory storage
|   +-- StorageEncrypted.cs    # Encrypted wrapper
|-- Encryption/                 # Encryption components
|   |-- EncryptorProviderAesGcm.cs # AES-GCM provider
|   |-- EncryptorPage.cs       # Page-level encryption
|   +-- EncryptorBlock.cs      # Block-level encryption
|-- Transactions/               # Transaction support
|   |-- Transaction.cs         # Basic transaction
|   |-- MvccTransaction.cs     # MVCC transaction
|   |-- TransactionalStore.cs  # Transaction wrapper
|   |-- MvccTransactionalStore.cs
|   +-- Savepoint.cs           # Savepoint support
|-- Mvcc/                       # MVCC components
|   |-- MvccRecord.cs          # Versioned record
|   |-- MvccKeyValueStore.cs   # MVCC store
|   +-- MvccGarbageCollector.cs
|-- Concurrency/                # Concurrency control
|   |-- LockManager.cs         # Database-level locks
|   |-- RowLockManager.cs      # Row-level locks
|   |-- DeadlockDetector.cs    # Deadlock detection
|   |-- WaitForGraph.cs        # Wait-for graph
|   +-- TransactionWaitQueue.cs
|-- Indexes/                    # Secondary indexes
|   |-- IndexManager.cs        # Index management
|   |-- SecondaryIndexKeyValueStore.cs
|   +-- IndexMetadataStore.cs
|-- Cache/                      # Page caching
|   |-- PageCacheLru.cs        # LRU cache
|   +-- PageCacheShardedClock.cs # Sharded clock cache
|-- Managers/                   # Page management
|   |-- PageManager.cs         # Page allocation
|   +-- PageManagerOverflow.cs # Overflow pages
|-- Wal/                        # Write-ahead logging
|   |-- WriteAheadLog.cs       # WAL implementation
|   +-- WalReplayVisitor*.cs   # Recovery visitors
|-- Providers/                  # Provider system
|   |-- ProviderRegistry.cs    # Global registry
|   |-- ProviderFactory.cs     # Factory pattern
|   +-- StorageDetector.cs     # Auto-detection
|-- Query/                      # Query support
|   |-- QueryContext.cs        # Execution context
|   |-- BatchExecutor.cs       # Batch operations
|   +-- MultiResultReader.cs   # Multiple result sets
|-- Exceptions/                 # Exception types
|   |-- DeadlockException.cs
|   |-- RowLockException.cs
|   +-- ConfigurationMismatchException.cs
+-- Utils/                      # Utilities
    |-- CryptoUtils.cs         # Key derivation
    +-- Crc32.cs               # Checksums
```

### Component Diagram

```
+------------------------------------------------------------------+
|                        WitDatabase                                |
|  +------------------------------------------------------------+  |
|  |                   WitDatabaseBuilder                        |  |
|  +------------------------------------------------------------+  |
|                              |                                    |
|              +---------------+---------------+                    |
|              |               |               |                    |
|              v               v               v                    |
|  +--------------+  +---------------+  +---------------+          |
|  | IKeyValueStore|  | ITransactional|  | IIndexManager |          |
|  |    (Store)    |  |    Store      |  |               |          |
|  +--------------+  +---------------+  +---------------+          |
|          |                  |                                     |
|  +--------------+  +---------------+                              |
|  |   StoreBTree  |  |  Mvcc         |                              |
|  |   StoreLsm    |  |  Transactional|                              |
|  | StoreInMemory |  |  Store        |                              |
|  +--------------+  +---------------+                              |
|          |                                                        |
|  +--------------------------------------------------------------+ |
|  |                    IStorage                                   | |
|  |  +------------+  +-------------+  +---------------------+    | |
|  |  |StorageFile |  |StorageMemory|  |  StorageEncrypted   |    | |
|  |  +------------+  +-------------+  |         |           |    | |
|  |                                   |  +--------------+   |    | |
|  |                                   |  |ICryptoProvider|   |    | |
|  |                                   |  |  AES-GCM      |   |    | |
|  |                                   |  |  ChaCha20     |   |    | |
|  |                                   |  +--------------+   |    | |
|  |                                   +---------------------+    | |
|  +--------------------------------------------------------------+ |
+-------------------------------------------------------------------+
```

---

## Storage Engines

### B+Tree (`StoreBTree`)

Best for:
- Read-heavy workloads
- Random access patterns
- Small to medium databases

Features:
- O(log n) lookups, inserts, deletes
- Efficient range scans
- Page-based storage with caching
- Overflow pages for large values

```csharp
var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithBTree()
    .Build();
```

### LSM-Tree (`StoreLsm`)

Best for:
- Write-heavy workloads
- Sequential write patterns
- Large datasets

Features:
- O(1) amortized writes
- Background compaction
- Bloom filters for fast negative lookups
- Block cache for reads

```csharp
var db = new WitDatabaseBuilder()
    .WithLsmTree("./lsm_data", opts =>
    {
        opts.MemTableSizeLimit = 64 * 1024 * 1024;  // 64MB
        opts.Level0CompactionTrigger = 4;
        opts.EnableBlockCache = true;
        opts.BlockCacheSizeBytes = 128 * 1024 * 1024;  // 128MB
    })
    .Build();
```

---

## Encryption

### Built-in AES-GCM

```csharp
// Password-based
var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithEncryption("password")
    .Build();

// Raw key
byte[] key = new byte[32]; // 256-bit
var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithAesEncryption(key)
    .Build();
```

### ChaCha20-Poly1305 (BouncyCastle)

```csharp
using OutWit.Database.Core.BouncyCastle;

var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithBouncyCastleEncryption("password")
    .Build();
```

### Custom Crypto Provider

```csharp
public class MyCustomCrypto : ICryptoProvider
{
    public string ProviderKey => "my-custom-crypto";
    public int NonceSize => 12;
    public int TagSize => 16;
    
    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, 
        Span<byte> ciphertext, Span<byte> tag) { ... }
    
    public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, 
        ReadOnlySpan<byte> tag, Span<byte> plaintext) { ... }
    
    public ICryptoProvider Clone() => new MyCustomCrypto();
    public void Dispose() { }
}

var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithEncryption(new MyCustomCrypto())
    .Build();
```

---

## Transaction Isolation Levels

| Level | Dirty Read | Non-Repeatable Read | Phantom Read | Description |
|-------|------------|---------------------|--------------|-------------|
| ReadUncommitted | Yes | Yes | Yes | No isolation, highest concurrency |
| ReadCommitted | No | Yes | Yes | Only see committed data |
| RepeatableRead | No | No | Yes | Locks held for duration |
| Serializable | No | No | No | Full isolation |
| Snapshot | No | No | No | MVCC-based, no locks for reads |

---

## Performance Tips

1. **Choose the right engine**: B+Tree for reads, LSM-Tree for writes
2. **Tune cache size**: More cache = fewer disk reads
3. **Use appropriate page size**: 4KB default, 8KB-16KB for large values
4. **Batch operations**: Use transactions for multiple writes
5. **Enable MVCC**: For read-heavy concurrent workloads
6. **Use Snapshot isolation**: Best balance of consistency and concurrency

---

## Blazor WebAssembly Support

WitDatabase can run entirely in the browser using IndexedDB as the storage backend.

### Installation

```xml
<PackageReference Include="OutWit.Database.Core.IndexedDb" Version="1.0.0" />
```

Add JavaScript files to `index.html`:
```html
<script src="_content/OutWit.Database.Core.IndexedDb/witdb-indexeddb.js"></script>
<script src="_content/OutWit.Database.Core.IndexedDb/witdb-indexeddb-index.js"></script>
```

### Usage in Blazor

```razor
@inject IJSRuntime JSRuntime
@using OutWit.Database.Core.Builder
@using OutWit.Database.Core.IndexedDb

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
}
```

### With Encryption in Browser

```csharp
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("SecureDatabase", JSRuntime)
    .WithBTree()
    .WithEncryption("user-password")  // AES-GCM works in browser
    .WithTransactions()
    .Build();
```

### Browser Compatibility

| Feature | Compatible | Notes |
|---------|-----------|-------|
| B+Tree | ? Yes | Full support |
| LSM-Tree | ? No | Requires file system |
| MVCC | ? Yes | All isolation levels |
| Encryption | ? Yes | AES-GCM, BouncyCastle |
| Secondary Indexes | ? Yes | Via IndexedDB |
| Transactions | ? Yes | Full support |

See [OutWit.Database.Core.IndexedDb](../OutWit.Database.Core.IndexedDb/) for full documentation.

---

## Related Projects

| Project | Description |
|---------|-------------|
| OutWit.Database | SQL execution engine |
| OutWit.Database.Parser | SQL parser |
| OutWit.Database.Core.BouncyCastle | ChaCha20-Poly1305 encryption |
| **OutWit.Database.Core.IndexedDb** | **IndexedDB storage for Blazor WASM** |

---

## License

MIT License - see LICENSE file for details.

---

## See Also

- [STATUS.md](STATUS.md) - Implementation status
- [EXTENSIBILITY.md](EXTENSIBILITY.md) - Extension guide
- [../Roadmap.Core.md](../Roadmap.Core.md) - Full roadmap
- [../WitSql.md](../WitSql.md) - SQL specification
