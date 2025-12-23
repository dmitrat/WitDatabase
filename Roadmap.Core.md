# OutWit.Database.Core - Roadmap

**Version:** 1.3  
**Based on:** OutWit.Database.Core.TODO.md  
**Last Updated:** 2025-01-16

---

## Legend

| Symbol | Meaning |
|--------|---------|
| [x] | Implemented |
| [ ] | Not implemented |

**Priority Legend:**
- **P0** = Critical (required for ADO.NET/EF Core)
- **P1** = Important (production ready)
- **P2** = Optional (nice-to-have)

---

## Progress Summary

| Category | Implemented | Missing | Progress |
|----------|-------------|---------|----------|
| Key-Value Store | 3 | 0 | 100% |
| Storage Engines | 3 | 0 | 100% |
| Storage Backends | 3 | 0 | 100% |
| Encryption | 3 | 0 | 100% |
| Crash Recovery | 3 | 0 | 100% |
| Basic Concurrency | 4 | 0 | 100% |
| Isolation Levels + MVCC | 4 | 0 | 100% |
| Row-level Locks | 0 | 4 | 0% |
| Savepoints | 4 | 0 | 100% |
| Multiple Result Sets | 3 | 0 | 100% |
| Cursor Support | 0 | 4 | 0% - v2 |
| Query Context | 5 | 0 | 100% |
| Secondary Indexes | 8 | 0 | 100% |
| Storage Detection | 2 | 0 | 100% |
| Bulk Operations | 3 | 0 | 100% |
| Statistics | 2 | 2 | 50% |
| VACUUM/Compaction | 0 | 3 | 0% - v2 |
| Concurrent Transactions | 2 | 1 | 66% |
| ROWVERSION | 3 | 0 | 100% |
| **TOTAL** | **55** | **14** | **80%** |

---

## Recent Changes (v1.3)

### MVCC (Multi-Version Concurrency Control) [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `ITransactionTimestampManager` | [x] | Manages transaction timestamps |
| `TransactionTimestampManager` | [x] | Thread-safe implementation |
| `MvccRecord` | [x] | Versioned record with visibility rules |
| `IMvccStore` | [x] | MVCC key-value store interface |
| `MvccKeyValueStore` | [x] | Multi-version storage wrapper |
| `IMvccTransaction` | [x] | MVCC transaction interface |
| `MvccTransaction` | [x] | Transaction with snapshot isolation |
| `MvccTransactionalStore` | [x] | Transactional store with MVCC |
| Snapshot isolation | [x] | Consistent point-in-time reads |
| Write-write conflict detection | [x] | First-committer-wins semantics |
| Read-only transactions | [x] | Lightweight concurrent reads |
| Version garbage collection | [x] | Cleanup of old versions |

```csharp
// Create MVCC-enabled store
var inner = new StoreInMemory();
var store = new MvccTransactionalStore(inner);

// Multiple concurrent read transactions
var readTx1 = store.BeginReadOnlyTransaction();
var readTx2 = store.BeginReadOnlyTransaction();

// Both see consistent snapshots
var v1 = readTx1.Get(key);
var v2 = readTx2.Get(key);

// Read during write (snapshot isolation)
var writeTx = store.BeginTransaction();
writeTx.Put(key, newValue);
// readTx1 still sees old value
var oldValue = readTx1.Get(key);

writeTx.Commit();
// readTx1 still sees old value (snapshot)
// New transactions see new value
```

### Builder Integration [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `EnableMvcc` option | [x] | Enable MVCC in builder options |
| `DefaultIsolationLevel` option | [x] | Set default isolation level |
| `.WithMvcc()` extension | [x] | Enable MVCC with Snapshot isolation |
| `.WithMvcc(IsolationLevel)` | [x] | Enable MVCC with custom default |
| `.WithDefaultIsolationLevel()` | [x] | Set isolation level independently |

```csharp
// Enable MVCC via builder
var db = new WitDatabaseBuilder()
    .WithMemoryStorage()
    .WithMvcc()  // Enables snapshot isolation
    .Build();

// With custom isolation level
var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithMvcc(IsolationLevel.Serializable)
    .Build();
```

---

## Recent Changes (v1.2)

### Transaction Isolation Levels [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `IsolationLevel` enum | [x] | ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, Snapshot |
| `ITransaction.IsolationLevel` | [x] | Property to get transaction's isolation level |
| `BeginTransaction(IsolationLevel)` | [x] | Create transaction with specific isolation level |

```csharp
// Create transaction with specific isolation level
using var tx = db.BeginTransaction(IsolationLevel.Serializable);
tx.Put(key, value);
tx.Commit();

// Check isolation level
Console.WriteLine(tx.IsolationLevel); // Serializable
```

### Multiple Result Sets [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `IMultiResultReader` interface | [x] | Read multiple result sets from batch execution |
| `MultiResultReader` class | [x] | Implementation with ResultSet wrapper |
| `NextResult()` method | [x] | Advance to next result set |
| `IBatchExecutor` interface | [x] | Batch operation execution |
| `BatchExecutor` class | [x] | Execute multiple operations with results |

```csharp
// Reading multiple result sets
using var reader = new MultiResultReader(resultSets);
while (reader.NextResult())
{
    foreach (var (key, value) in reader.CurrentResult!)
    {
        // Process results
    }
    Console.WriteLine($"Records affected: {reader.RecordsAffected}");
}

// Batch execution
using var reader = store.ExecuteBatch(
    new BatchPutOperation(key1, value1),
    new BatchGetOperation(key1),
    new BatchScanOperation(startKey, endKey),
    new BatchDeleteOperation(key1)
);

while (reader.NextResult())
{
    // Process each result set
}
```

### Bulk Operations [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `BulkPut` extension | [x] | Insert/update multiple key-value pairs |
| `BulkDelete` extension | [x] | Delete multiple keys |
| `IBulkKeyValueStore` interface | [x] | For stores with native bulk support |
| `StreamingPut` extension | [x] | Batch streaming with progress callback |
| `StreamingPutWithTransaction` | [x] | Transactional streaming with auto-commit |

```csharp
// Bulk insert
var items = Enumerable.Range(0, 10000)
    .Select(i => (ToBytes($"key{i}"), ToBytes($"value{i}")));
var count = store.BulkPut(items);

// Bulk delete
var keys = Enumerable.Range(0, 5000).Select(i => ToBytes($"key{i}"));
var deleted = store.BulkDelete(keys);

// Streaming insert with progress
var total = store.StreamingPut(items, batchSize: 1000,
    progress: count => Console.WriteLine($"Inserted: {count}"));

// Async streaming from IAsyncEnumerable
await store.StreamingPutAsync(asyncDataSource, batchSize: 1000);
```

### Store Statistics [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `IKeyValueStoreStatistics` interface | [x] | Statistics for query optimizer |
| `Count()` extension | [x] | Get total key-value pairs |
| `IsEmpty()` extension | [x] | Check if store is empty |
| `GetStatistics()` extension | [x] | Get statistics wrapper |

```csharp
// Get statistics
var count = store.Count();
var isEmpty = store.IsEmpty();
var stats = store.GetStatistics();
Console.WriteLine($"Approximate size: {stats.ApproximateSizeInBytes}");
```

### ROWVERSION / Optimistic Concurrency [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `IVersionedKeyValueStore` interface | [x] | Versioned key-value operations |
| `VersionedKeyValueStore` class | [x] | Wrapper adding versions to any store |
| `ConditionalPut` / `ConditionalDelete` | [x] | Optimistic concurrency control |

```csharp
// Create versioned store
var store = new VersionedKeyValueStore(innerStore);

// Put with version tracking
var version = store.PutWithVersion(key, value);

// Conditional update (optimistic concurrency)
var (success, newVersion) = store.ConditionalPut(key, newValue, expectedVersion);
if (!success)
{
    // Version mismatch - handle conflict
}

// Conditional delete
var deleted = store.ConditionalDelete(key, expectedVersion);
```

---

## Recent Changes (v1.1)

### Storage Auto-Detection [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `StorageDetector` | [x] | Auto-detects BTree vs LSM databases |
| `WitDatabase.Open()` auto-detect | [x] | Opens both BTree (file) and LSM (directory) |

```csharp
// Now works automatically for both BTree and LSM!
using var db = WitDatabase.Open(path);          // Auto-detects
using var db = WitDatabase.Open(path, password); // Encrypted, auto-detects
using var db = WitDatabase.CreateOrOpen(path);   // Creates or opens, auto-detects
```

### Index Metadata Persistence [x]

| Feature | Status | Description |
|---------|--------|-------------|
| `IndexMetadataStore` | [x] | Persists index definitions to store |
| Auto-restore on open | [x] | Indexes recreated automatically |

```csharp
// Create index
using (var db = WitDatabase.Create("data.db"))
{
    db.CreateIndex("idx_email", isUnique: true);
}

// Reopen - index restored automatically!
using (var db = WitDatabase.Open("data.db"))
{
    Assert.That(db.HasIndex("idx_email"), Is.True);
}
```

---

## 1. Existing Components [x]

### 1.1 Key-Value Store Interfaces

| Feature | Status | Description |
|---------|--------|-------------|
| `IKeyValueStore` | [x] | Get, Put, Delete, Scan, Flush |
| `ITransactionalStore` | [x] | BeginTransaction, ACID support |
| `ITransaction` | [x] | Get, Put, Delete, Commit, Rollback |

### 1.2 Storage Engines

| Feature | Status | Description |
|---------|--------|-------------|
| `StoreBTree` | [x] | B+Tree storage, read-optimized |
| `StoreLsm` | [x] | LSM-Tree storage, write-optimized |
| `StoreInMemory` | [x] | In-memory storage for testing |

### 1.3 Storage Backends

| Feature | Status | Description |
|---------|--------|-------------|
| `StorageFile` | [x] | File-based persistent storage |
| `StorageMemory` | [x] | Memory-based storage for testing |
| `StorageEncrypted` | [x] | Encrypted storage wrapper |

### 1.4 Encryption

| Feature | Status | Description |
|---------|--------|-------------|
| AES-256-GCM | [x] | Hardware accelerated encryption |
| ChaCha20-Poly1305 | [x] | BouncyCastle, Blazor WASM compatible |
| PBKDF2 | [x] | Password-based key derivation |

### 1.5 Crash Recovery

| Feature | Status | Description |
|---------|--------|-------------|
| Write-Ahead Log (WAL) | [x] | Durability guarantee |
| Rollback Journal | [x] | Alternative to WAL |
| Crash recovery | [x] | Automatic recovery on database open |

### 1.6 Concurrency

| Feature | Status | Description |
|---------|--------|-------------|
| Reader-writer locking | [x] | Multiple concurrent readers |
| Writer priority | [x] | Prevents writer starvation |
| File locking | [x] | Multi-process safety |
| Reentrancy detection | [x] | Throws LockRecursionException |

### 1.7 Savepoints

| Feature | Status | Description |
|---------|--------|-------------|
| `CreateSavepoint(name)` | [x] | Create named savepoint |
| `RollbackToSavepoint(name)` | [x] | Rollback to savepoint |
| `ReleaseSavepoint(name)` | [x] | Release savepoint |
| Nested savepoints | [x] | Stack-based savepoint management |

**Usage:**
```csharp
var tx = db.BeginTransaction();
if (tx is ITransactionWithSavepoints txSp)
{
    txSp.CreateSavepoint("sp1");
    tx.Put("key", "value");
    txSp.RollbackToSavepoint("sp1"); // Undo changes
}
```

### 1.8 Query Context

| Feature | Status | Description |
|---------|--------|-------------|
| `IQueryContext` interface | [x] | Query execution metadata |
| `AffectedRows` property | [x] | Rows affected by DML |
| `LastInsertId` property | [x] | Auto-generated ID |
| Query timeout support | [x] | Configurable timeout |
| `CancellationToken` propagation | [x] | Async cancellation |

**Note:** Used by SQL layer (`OutWit.Database`), not key-value layer.

### 1.9 Secondary Indexes

| Feature | Status | Description |
|---------|--------|-------------|
| `ISecondaryIndex` interface | [x] | Secondary index operations |
| `SecondaryIndexKeyValueStore` | [x] | Uses any IKeyValueStore |
| Unique index support | [x] | Enforces uniqueness |
| Non-unique index support | [x] | Multiple PKs per index key |
| Index auto-update | [x] | Via IndexManager |
| Storage-agnostic factory | [x] | Works with any store type |
| WitDatabase integration | [x] | CreateIndex, DropIndex, etc. |
| Index metadata persistence | [x] | Auto-restored on reopen |

---

## 2. Missing Components [ ]

### 2.1 Transaction Isolation Levels [x]

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `IsolationLevel` enum | [x] | P0 | SS1.1 |
| MVCC (Multi-Version Concurrency Control) | [x] | P0 | SS1.2 |
| Extend `ITransaction` for isolation level | [x] | P0 | SS1.3 |
| Record versioning (transaction timestamp) | [x] | P0 | SS1.4 |

### 2.2 Row-level Locks

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `RowLockManager` class | [ ] | P0 | SS2.1 |
| `FOR UPDATE` / `FOR SHARE` support | [ ] | P0 | SS2.2 |
| `NOWAIT` / `SKIP LOCKED` modes | [ ] | P1 | SS2.3 |
| Deadlock detection | [ ] | P0 | SS2.4 |

### 2.3 Multiple Result Sets [x]

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `IMultiResultReader` interface | [x] | P1 | SS4.1 |
| `NextResult()` method | [x] | P1 | SS4.2 |
| Batch execution support | [x] | P1 | SS4.3 |

### 2.4 Cursor Support (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `ICursor` interface | [ ] | P2 - v2 | SS5.1 |
| Forward-only mode | [ ] | P2 - v2 | SS5.2 |
| Scrollable mode | [ ] | P2 - v2 | SS5.2 |
| Fetch size (batching) | [ ] | P2 - v2 | SS5.3 |

### 2.5 Bulk Operations [x]

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `BulkPut(IEnumerable<(key, value)>)` | [x] | P1 | SS8.1 |
| `BulkDelete(IEnumerable<key>)` | [x] | P1 | SS8.2 |
| Streaming insert support | [x] | P1 | SS8.3 |

### 2.6 Statistics and Metadata

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Table row count (approximate/exact) | [x] | P1 | SS9.1 |
| Index statistics for query optimizer | [x] | P1 | SS9.2 |
| `ANALYZE` command support | [ ] | P2 - v2 | SS9.3 |
| Column cardinality estimation | [ ] | P2 - v2 | SS9.4 |

### 2.7 VACUUM / Compaction API (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Explicit `Vacuum()` method for BTree | [ ] | P2 - v2 | SS10.1 |
| Incremental vacuum support | [ ] | P2 - v2 | SS10.2 |
| Compaction progress/status API | [ ] | P2 - v2 | SS10.3 |

### 2.8 Concurrent Transactions [~]

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Multiple concurrent read transactions | [x] | P0 | SS11.1 |
| Read transactions during write (MVCC) | [x] | P0 | SS11.2 |
| Transaction wait queue with priorities | [ ] | P0 | SS11.3 |

### 2.9 ROWVERSION / Concurrency Tokens [x]

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Auto-incrementing row version support | [x] | P1 | SS12.1 |
| Optimistic concurrency check at kernel | [x] | P1 | SS12.2 |
| Conditional Put/Delete (version check) | [x] | P1 | SS12.3 |

---

## 3. Implementation Priorities

### 3.1 Phase 1: MVP for ADO.NET [x]

All Phase 1 features are complete.

### 3.2 Phase 2: EF Core Compatibility

| Feature | Priority |
|---------|----------|
| `IsolationLevel` enum | P0 |
| MVCC implementation | P0 |
| `RowLockManager` | P0 |
| Deadlock detection | P0 |
| Multiple concurrent transactions | P0 |
| ROWVERSION support | P1 |

### 3.3 Phase 3: Production Ready

| Feature | Priority |
|---------|----------|
| `IMultiResultReader` | P1 |
| Bulk operations | P1 |
| Statistics | P1 |
| `NOWAIT` / `SKIP LOCKED` | P1 |

### 3.4 Phase 4: Nice to Have (Deferred to v2)

| Feature | Priority |
|---------|----------|
| `ICursor` interface | P2 - v2 |
| VACUUM API | P2 - v2 |
| `ANALYZE` support | P2 - v2 |
| Cardinality estimation | P2 - v2 |

---

## 4. Architecture Notes

### 4.1 Storage Auto-Detection

```
WitDatabase.Open(path)
    -> StorageDetector.Detect(path)
        -> Directory exists? -> Check for sst_*.sst or wal.log -> LSM
        -> File exists? -> Read header magic bytes -> BTree
            -> Magic valid -> Read ProviderMetadata
            -> Magic invalid -> Encrypted BTree
```

### 4.2 Secondary Index Architecture

```
ISecondaryIndex (interface)
+-- SecondaryIndexKeyValueStore - Universal implementation
    +-- Uses any IKeyValueStore (BTree, LSM, InMemory, custom)

ISecondaryIndexFactory (interface)
+-- SecondaryIndexFactoryKeyValueStore - Universal factory
    +-- Creates indexes using any IKeyValueStore

IndexMetadataStore
+-- Persists index definitions (name, isUnique)
+-- Uses system key prefix "\0\0_idx_meta_"
+-- Auto-restored on WitDatabase open
```

### 4.3 Tested Storage Combinations

| Storage Engine | Backend | Encryption | Indexes | Auto-Detect | Status |
|----------------|---------|------------|---------|-------------|--------|
| BTree | Memory | No | Yes | N/A | Done |
| BTree | Memory | AES-GCM | Yes | N/A | Done |
| BTree | File | No | Yes | Yes | Done |
| BTree | File | AES-GCM | Yes | Yes | Done |
| LSM | Directory | No | Yes | Yes | Done |
| LSM | Directory | AES-GCM | Yes | Yes | Done |

---

## 5. API Quick Reference

### 5.1 Opening Databases

```csharp
// Create new (BTree by default)
var db = WitDatabase.Create("data.db");
var db = WitDatabase.Create("data.db", password);

// Open existing (auto-detects BTree vs LSM)
var db = WitDatabase.Open("data.db");           // BTree file
var db = WitDatabase.Open("/path/to/lsm_dir"); // LSM directory
var db = WitDatabase.Open(path, password);      // Encrypted

// Create or open (auto-detects for existing)
var db = WitDatabase.CreateOrOpen(path);

// In-memory
var db = WitDatabase.CreateInMemory();

// Full control via builder
var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")   // or .WithLsmTree(dir)
    .WithBTree()               // or .WithLsmTree()
    .WithEncryption(password)
    .WithTransactions()
    .WithIndexDirectory("indexes/")
    .Build();
```

### 5.2 Database Info

```csharp
var info = WitDatabase.GetDatabaseInfo(path);
// info.Exists, info.StoreType ("btree"/"lsm"), 
// info.RequiresPassword, info.HasTransactions, etc.
```

### 5.3 Indexes

```csharp
var index = db.CreateIndex("idx_email", isUnique: true);
index.Add(emailBytes, userIdBytes);

var userIds = index.Find(emailBytes);
db.DropIndex("idx_email");
```

### 5.4 Transactions with Savepoints

```csharp
using var tx = db.BeginTransaction();
tx.Put(key, value);

if (tx is ITransactionWithSavepoints txSp)
{
    txSp.CreateSavepoint("sp1");
    tx.Put(key2, value2);
    txSp.RollbackToSavepoint("sp1"); // Undo key2
}

tx.Commit(); // Only key is committed
```

---

**Last Updated:** 2025-01-16
