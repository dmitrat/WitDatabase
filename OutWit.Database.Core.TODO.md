# OutWit.Database.Core - TODO for SQL Engine

Analysis of the `OutWit.Database.Core` kernel for compliance with WitSql specifications to build a full-featured SQL engine with ADO.NET and EF Core support.

## Core Components Status

| Component | Status | Description |
|-----------|--------|-------------|
| IKeyValueStore | [x] Done | Get, Put, Delete, Scan, Flush |
| ITransactionalStore | [x] Done | BeginTransaction, ACID |
| ITransaction | [x] Done | Get, Put, Delete, Commit, Rollback |
| LockManager | [x] Done | Read/Write locks |
| WAL/RollbackJournal | [x] Done | Crash recovery |
| StoreBTree | [x] Done | B+Tree storage |
| StoreLsm | [x] Done | LSM-Tree storage |
| Encryption | [x] Done | AES-GCM, ChaCha20 |
| MVCC | [x] Done | Multi-Version Concurrency Control |
| Row-level Locks | [x] Done | FOR UPDATE/SHARE, NOWAIT, SKIP LOCKED |
| Deadlock Detection | [x] Done | Wait-for graph, victim selection |
| Transaction Wait Queue | [x] Done | Priority-based queuing |

---

## Missing Components

### Category 1: Transaction Isolation Levels

- [x] **1.1** `IsolationLevel` enum (ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, Snapshot)
- [x] **1.2** MVCC (Multi-Version Concurrency Control) for Snapshot isolation
- [x] **1.3** Extend `ITransaction` to specify isolation level
- [x] **1.4** Record versioning (transaction timestamp / row version)

### Category 2: Row-level Locks

- [x] **2.1** `RowLockManager` - row-level locks (not just database-level)
- [x] **2.2** `FOR UPDATE` / `FOR SHARE` - shared/exclusive row locks
- [x] **2.3** `NOWAIT` / `SKIP LOCKED` - non-blocking lock modes
- [x] **2.4** Deadlock detection

### Category 3: Savepoints

- [x] **3.1** Extend `ITransaction` for `CreateSavepoint(name)`
- [x] **3.2** `RollbackToSavepoint(name)` - partial rollback
- [x] **3.3** `ReleaseSavepoint(name)` - release savepoint
- [x] **3.4** Nested savepoints (savepoint stack)

### Category 4: Multiple Result Sets

- [x] **4.1** `IMultiResultReader` - reading multiple result sets
- [x] **4.2** `NextResult()` - move to next result set
- [x] **4.3** Batch execution support

### Category 5: Cursor Support (v2)

- [ ] **5.1** `ICursor` interface for scrollable cursors - v2
- [ ] **5.2** Forward-only and scrollable modes - v2
- [ ] **5.3** Fetch size (batching) - v2

### Category 6: Query Execution Context

- [x] **6.1** `IQueryContext` - query execution context
- [x] **6.2** `AffectedRows` - number of affected rows
- [x] **6.3** `LastInsertId` - last auto-increment ID
- [x] **6.4** Query timeout support
- [x] **6.5** Query cancellation (`CancellationToken` propagation)

### Category 7: Secondary Indexes

- [x] **7.1** `ISecondaryIndex` interface
- [x] **7.2** B+Tree based secondary indexes
- [x] **7.3** Unique index support
- [x] **7.4** Composite index support
- [x] **7.5** Index maintenance (auto-update on Put/Delete)

### Category 8: Bulk Operations

- [x] **8.1** `BulkPut(IEnumerable<(key, value)>)` - batch insert
- [x] **8.2** `BulkDelete(IEnumerable<key>)` - batch delete
- [x] Streaming insert support

### Category 9: Statistics and Metadata

- [x] **9.1** Table row count (approximate/exact)
- [x] **9.2** Index statistics for query optimizer
- [ ] **9.3** `ANALYZE` command support - v2
- [ ] **9.4** Column cardinality estimation - v2

### Category 10: VACUUM / Compaction API (v2)

- [ ] **10.1** Explicit `Vacuum()` method for BTree - v2
- [ ] **10.2** Incremental vacuum support - v2
- [ ] **10.3** Compaction progress/status API - v2

### Category 11: Concurrent Transactions

- [x] **11.1** Multiple concurrent read transactions
- [x] **11.2** Read transactions during write transaction (MVCC)
- [x] **11.3** Transaction wait queue with priorities

### Category 12: ROWVERSION / Concurrency Tokens

- [x] **12.1** Auto-incrementing row version column support
- [x] **12.2** Optimistic concurrency check at kernel level
- [x] **12.3** Conditional Put/Delete (version check)

---

## Implementation Status Summary

### v1 Complete ?

| # | Component | Priority | Status |
|---|-----------|----------|--------|
| 1.1-1.4 | Isolation levels + MVCC | P0 Critical | ? Done |
| 2.1-2.4 | Row-level locks + Deadlock detection | P0 Critical | ? Done |
| 3.1-3.4 | Savepoints | P1 Important | ? Done |
| 4.1-4.3 | Multiple result sets | P1 Important | ? Done |
| 6.1-6.5 | Query execution context | P0 Critical | ? Done |
| 7.1-7.5 | Secondary indexes | P0 Critical | ? Done |
| 8.1-8.3 | Bulk operations | P1 Important | ? Done |
| 9.1-9.2 | Basic statistics | P1 Important | ? Done |
| 11.1-11.3 | Concurrent transactions | P0 Critical | ? Done |
| 12.1-12.3 | ROWVERSION support | P1 Important | ? Done |

### Deferred to v2

| # | Component | Priority | Status |
|---|-----------|----------|--------|
| 5.1-5.3 | Cursor support | P2 Optional | Deferred |
| 9.3-9.4 | Advanced statistics | P2 Optional | Deferred |
| 10.1-10.3 | VACUUM API | P2 Optional | Deferred |

---

## Notes

1. **MVCC** - **FULLY IMPLEMENTED**. Provides snapshot isolation, all isolation levels, concurrent read transactions. Integrated into WitDatabase via `.WithMvcc()` builder extension.

2. **Row-level locks** - **FULLY IMPLEMENTED**. Includes `RowLockManager`, `FOR UPDATE`/`FOR SHARE`, `NOWAIT`/`SKIP LOCKED`, integrated with `MvccTransaction`.

3. **Deadlock detection** - **FULLY IMPLEMENTED**. Includes `WaitForGraph`, `DeadlockDetector` with multiple victim selection strategies, `DeadlockException`.

4. **Transaction wait queue** - **FULLY IMPLEMENTED**. Priority-based queue with FIFO/LIFO ordering, writer priority, timeout support, integrated with `MvccTransactionalStore`.

5. **Current state** - All P0 and P1 features are complete. Only v2 features remain (Cursors, VACUUM, Advanced Statistics).

---

## Files Created for MVCC Implementation

### Core Files (21)
1. `OutWit.Database.Core/Interfaces/ITransactionTimestampManager.cs`
2. `OutWit.Database.Core/Interfaces/IMvccStore.cs`
3. `OutWit.Database.Core/Interfaces/IMvccTransaction.cs`
4. `OutWit.Database.Core/Interfaces/IRowLockManager.cs`
5. `OutWit.Database.Core/Transactions/TransactionTimestampManager.cs`
6. `OutWit.Database.Core/Transactions/MvccTransaction.cs`
7. `OutWit.Database.Core/Transactions/MvccTransactionalStore.cs`
8. `OutWit.Database.Core/Mvcc/MvccRecord.cs`
9. `OutWit.Database.Core/Mvcc/MvccGarbageCollectorOptions.cs`
10. `OutWit.Database.Core/Mvcc/MvccGarbageCollector.cs`
11. `OutWit.Database.Core/Stores/MvccKeyValueStore.cs`
12. `OutWit.Database.Core/Concurrency/RowLockMode.cs`
13. `OutWit.Database.Core/Concurrency/RowLockRequest.cs`
14. `OutWit.Database.Core/Concurrency/RowLockHandle.cs`
15. `OutWit.Database.Core/Concurrency/RowLockManager.cs`
16. `OutWit.Database.Core/Concurrency/WaitForGraph.cs`
17. `OutWit.Database.Core/Concurrency/DeadlockDetector.cs`
18. `OutWit.Database.Core/Concurrency/TransactionWaitQueueOptions.cs`
19. `OutWit.Database.Core/Concurrency/TransactionWaitQueue.cs`
20. `OutWit.Database.Core/Exceptions/RowLockException.cs`
21. `OutWit.Database.Core/Exceptions/DeadlockException.cs`

### Test Files (12)
1. `OutWit.Database.Core.Tests/Transactions/TransactionTimestampManagerTests.cs`
2. `OutWit.Database.Core.Tests/Transactions/MvccTransactionalStoreTests.cs`
3. `OutWit.Database.Core.Tests/Transactions/MvccTransactionRowLockTests.cs`
4. `OutWit.Database.Core.Tests/Transactions/MvccTransactionIsolationLevelTests.cs`
5. `OutWit.Database.Core.Tests/Transactions/MvccTransactionalStoreStressTests.cs`
6. `OutWit.Database.Core.Tests/Mvcc/MvccRecordTests.cs`
7. `OutWit.Database.Core.Tests/Mvcc/MvccGarbageCollectorTests.cs`
8. `OutWit.Database.Core.Tests/Stores/MvccKeyValueStoreTests.cs`
9. `OutWit.Database.Core.Tests/Concurrency/RowLockManagerTests.cs`
10. `OutWit.Database.Core.Tests/Concurrency/WaitForGraphTests.cs`
11. `OutWit.Database.Core.Tests/Concurrency/DeadlockDetectorTests.cs`
12. `OutWit.Database.Core.Tests/Concurrency/TransactionWaitQueueTests.cs`

---

**Last Updated:** 2025-01-17
