# OutWit.Database.Core - Roadmap

**Version:** 1.0  
**Based on:** OutWit.Database.Core.TODO.md  
**Last Updated:** 2025-01-15

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
| Isolation Levels | 0 | 4 | 0% |
| Row-level Locks | 0 | 4 | 0% |
| Savepoints | 4 | 0 | 100% |
| Multiple Result Sets | 0 | 3 | 0% |
| Cursor Support | 0 | 4 | 0% - v2 |
| Query Context | 5 | 0 | 100% |
| Secondary Indexes | 7 | 0 | 100% |
| Bulk Operations | 0 | 3 | 0% |
| Statistics | 0 | 2 | 0% |
| VACUUM/Compaction | 0 | 3 | 0% - v2 |
| Concurrent Transactions | 0 | 3 | 0% |
| ROWVERSION | 0 | 3 | 0% |
| **TOTAL** | **35** | **29** | **55%** |

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

---

## 2. Missing Components [ ]

### 2.1 Transaction Isolation Levels

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `IsolationLevel` enum | [ ] | P0 | SS1.1 |
| MVCC (Multi-Version Concurrency Control) | [ ] | P0 | SS1.2 |
| Extend `ITransaction` for isolation level | [ ] | P0 | SS1.3 |
| Record versioning (timestamp/row version) | [ ] | P0 | SS1.4 |

**Notes:** Required for Snapshot isolation and concurrent reads. Without MVCC, EF Core cannot work in multi-user scenarios.

### 2.2 Row-level Locks

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `RowLockManager` class | [ ] | P0 | SS2.1 |
| `FOR UPDATE` / `FOR SHARE` support | [ ] | P0 | SS2.2 |
| `NOWAIT` / `SKIP LOCKED` modes | [ ] | P1 | SS2.3 |
| Deadlock detection | [ ] | P0 | SS2.4 |

**Notes:** Currently only database-level locks exist. Row-level locks are essential for pessimistic concurrency in EF Core.

### 2.3 Savepoints

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `CreateSavepoint(name)` | [x] | P1 | SS3.1 |
| `RollbackToSavepoint(name)` | [x] | P1 | SS3.2 |
| `ReleaseSavepoint(name)` | [x] | P1 | SS3.3 |
| Nested savepoints (stack) | [x] | P1 | SS3.4 |

**Notes:** Used by EF Core for nested transactions and SaveChanges with retry. ? Implemented in `ITransactionWithSavepoints` and `Transaction`.

### 2.4 Multiple Result Sets

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `IMultiResultReader` interface | [ ] | P1 | SS4.1 |
| `NextResult()` method | [ ] | P1 | SS4.2 |
| Batch execution support | [ ] | P1 | SS4.3 |

### 2.5 Cursor Support (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `ICursor` interface | [ ] | P2 - v2 | SS5.1 |
| Forward-only mode | [ ] | P2 - v2 | SS5.2 |
| Scrollable mode | [ ] | P2 - v2 | SS5.2 |
| Fetch size (batching) | [ ] | P2 - v2 | SS5.3 |

### 2.6 Query Execution Context

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `IQueryContext` interface | [x] | P0 | SS6.1 |
| `AffectedRows` property | [x] | P0 | SS6.2 |
| `LastInsertId` property | [x] | P0 | SS6.3 |
| Query timeout support | [x] | P0 | SS6.4 |
| `CancellationToken` propagation | [x] | P0 | SS6.5 |

**Notes:** ADO.NET requires information about affected rows count and last insert id. ? Implemented in `IQueryContext` and `QueryContext`.

### 2.7 Secondary Indexes

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `ISecondaryIndex` interface | [x] | P0 | SS7.1 |
| B+Tree based secondary indexes | [x] | P0 | SS7.2 |
| Unique index support | [x] | P0 | SS7.3 |
| Composite index support | [x] | P0 | SS7.4 |
| Index maintenance (auto-update) | [x] | P0 | SS7.5 |
| Storage-agnostic index factory | [x] | P1 | SS7.6 |
| WitDatabase integration | [x] | P1 | SS7.7 |

**Notes:** Critical for any SQL engine. Without secondary indexes, efficient filtering and JOIN operations are impossible.

? Fully Implemented:
- `ISecondaryIndex` - interface for secondary index operations
- `SecondaryIndexBTree` - B+Tree based implementation with unique/non-unique support
- `SecondaryIndexKeyValueStore` - generic implementation using any `IKeyValueStore`
- `ISecondaryIndexFactory` - factory interface for creating storage-appropriate indexes
- `SecondaryIndexFactoryKeyValueStore` - universal factory using any `IKeyValueStore` (works with BTree, LSM, InMemory, or custom stores)
- `IIndexManager` - interface for managing multiple indexes per table
- `IndexManager` - implementation with auto-update on row insert/update/delete
- `WitDatabase` integration - fluent API (`WithSecondaryIndexFactory`, `WithIndexDirectory`) and methods (`CreateIndex`, `GetIndex`, `DropIndex`, `HasIndex`)

### 2.8 Bulk Operations

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `BulkPut(IEnumerable<(key, value)>)` | [ ] | P1 | SS8.1 |
| `BulkDelete(IEnumerable<key>)` | [ ] | P1 | SS8.2 |
| Streaming insert support | [ ] | P1 | SS8.3 |

### 2.9 Statistics and Metadata

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Table row count (approximate/exact) | [ ] | P1 | SS9.1 |
| Index statistics for query optimizer | [ ] | P1 | SS9.2 |
| `ANALYZE` command support | [ ] | P2 - v2 | SS9.3 |
| Column cardinality estimation | [ ] | P2 - v2 | SS9.4 |

### 2.10 VACUUM / Compaction API (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Explicit `Vacuum()` method for BTree | [ ] | P2 - v2 | SS10.1 |
| Incremental vacuum support | [ ] | P2 - v2 | SS10.2 |
| Compaction progress/status API | [ ] | P2 - v2 | SS10.3 |

### 2.11 Concurrent Transactions

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Multiple concurrent read transactions | [ ] | P0 | SS11.1 |
| Read transactions during write (MVCC) | [ ] | P0 | SS11.2 |
| Transaction wait queue with priorities | [ ] | P0 | SS11.3 |

### 2.12 ROWVERSION / Concurrency Tokens

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Auto-incrementing row version support | [ ] | P1 | SS12.1 |
| Optimistic concurrency check at kernel | [ ] | P1 | SS12.2 |
| Conditional Put/Delete (version check) | [ ] | P1 | SS12.3 |

---

## 3. Implementation Priorities

### 3.1 Phase 1: MVP for ADO.NET ?

| Feature | Priority | Status |
|---------|----------|--------|
| `IQueryContext` interface | P0 | ? |
| `AffectedRows` property | P0 | ? |
| `LastInsertId` property | P0 | ? |
| Query timeout | P0 | ? |
| `CancellationToken` support | P0 | ? |
| `ISecondaryIndex` interface | P0 | ? |
| B+Tree secondary indexes | P0 | ? |
| Unique index support | P0 | ? |
| Composite index support | P0 | ? |
| Savepoints | P1 | ? |
| Storage-agnostic index factory | P1 | ? |
| WitDatabase index integration | P1 | ? |

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

## 4. Priority Summary

| Priority | Count | Description |
|----------|-------|-------------|
| P0 | 21 | Required for ADO.NET/EF Core |
| P1 | 16 | Production ready features |
| P2 | 7 | Nice-to-have features |

---

## 5. Architecture Notes

### 5.1 Modular Storage Design

WitDatabase uses a modular storage architecture where users can choose or implement their own storage engines:

```
IKeyValueStore (interface)
??? StoreBTree     - B+Tree, optimized for reads
??? StoreLsm       - LSM-Tree, optimized for writes  
??? StoreInMemory  - In-memory, for testing
```

### 5.2 Secondary Index Architecture

The secondary index system is fully modular and storage-agnostic:

```
ISecondaryIndex (interface)
??? SecondaryIndexBTree         - Uses BTree directly (PageManager)
??? SecondaryIndexKeyValueStore - Uses any IKeyValueStore

ISecondaryIndexFactory (interface)
??? SecondaryIndexFactoryKeyValueStore - Universal factory
    ??? Creates BTree-based indexes (via StoreBTree)
    ??? Creates LSM-based indexes (via StoreLsm)
    ??? Creates in-memory indexes (via StoreInMemory)
    ??? Works with custom IKeyValueStore implementations

IndexManager
??? Accepts ISecondaryIndexFactory in constructor
??? Manages index lifecycle and auto-updates
??? Thread-safe operations

WitDatabase Integration
??? CreateIndex(name, isUnique) - creates new index
??? GetIndex(name) - retrieves existing index
??? DropIndex(name) - removes index
??? HasIndex(name) - checks if index exists
??? IndexNames - lists all index names
??? Fluent API:
?   ??? WithSecondaryIndexFactory(factory) - custom factory
?   ??? WithIndexDirectory(path) - custom index storage location
??? Automatic factory selection based on storage engine
```

### 5.3 Tested Storage Combinations

All combinations are tested and working:

| Storage Engine | Backend | Encryption | Indexes | Status |
|----------------|---------|------------|---------|--------|
| BTree | Memory | No | ? | Working |
| BTree | Memory | AES-GCM | ? | Working |
| BTree | File | No | ? | Working |
| BTree | File | AES-GCM | ? | Working |
| LSM | Directory | No | ? | Working |
| LSM | Directory | AES-GCM | ? | Working |
| Custom Store | - | - | ? | Working (with in-memory indexes) |
| Custom Store | - | - | ? | Working (with custom index factory) |

### 5.4 Adding Custom IKeyValueStore

To add a custom `IKeyValueStore` implementation:

1. **Implement `IKeyValueStore`** - no additional classes needed for indexes
2. **Use with builder**:
   ```csharp
   var db = new WitDatabaseBuilder()
       .WithStore(myCustomStore)
       .WithTransactions()
       .Build();
   ```
3. **Indexes**: By default, in-memory indexes are used. To use your storage for indexes:
   ```csharp
   var indexFactory = new SecondaryIndexFactoryKeyValueStore(
       indexName => new MyCustomStore($"index_{indexName}"));
   
   var db = new WitDatabaseBuilder()
       .WithStore(myCustomStore)
       .WithSecondaryIndexFactory(indexFactory)
       .WithTransactions()
       .Build();
   ```

### 5.5 Known Limitations

1. **Index Persistence**: Index metadata (names, isUnique flags) is NOT persisted. When reopening a database, indexes must be recreated. This will be addressed in a future update with index metadata storage.

2. **LSM Database Open**: `WitDatabase.Open()` does not auto-detect LSM databases. Use `WitDatabaseBuilder` directly for LSM.

---

**Last Updated:** 2025-01-15
