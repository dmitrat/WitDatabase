# MVCC and Concurrent Transactions - Implementation Plan

**Version:** 1.2  
**Last Updated:** 2025-01-17  
**Status:** In Progress

---

## Overview

This document outlines the detailed implementation plan for Multi-Version Concurrency Control (MVCC) 
and Concurrent Transactions support in OutWit.Database.Core.

MVCC enables:
- Multiple concurrent read transactions
- Read transactions during write transaction (non-blocking reads)
- Snapshot isolation level
- Better concurrency than traditional locking

---

## Current State Analysis

### What We Have ?
- [x] `IsolationLevel` enum (all levels defined)
- [x] `ITransaction.IsolationLevel` property
- [x] `BeginTransaction(IsolationLevel)` method
- [x] `VersionedKeyValueStore` - row versioning wrapper
- [x] `LockManager` - database-level read/write locks
- [x] `DatabaseLock` - in-process ReaderWriterLockSlim wrapper
- [x] `TransactionalStore` - single writer transactions

### What We Need
- [x] Record versioning with transaction timestamps
- [x] MVCC storage layer (multi-version records)
- [x] Snapshot isolation implementation
- [x] Multiple concurrent read transactions
- [x] Read transactions during active write
- [ ] Row-level lock manager
- [ ] Deadlock detection
- [ ] Transaction wait queue with priorities

---

## Phase 1: MVCC Foundation [COMPLETE]

### 1.1 Transaction Timestamp Manager [x]
- [x] Create `ITransactionTimestampManager` interface
- [x] Implement `TransactionTimestampManager` class
- [x] Monotonically increasing timestamps
- [x] Track active transaction timestamps
- [x] Track committed transaction timestamps
- [x] Unit tests

```
Files created:
- OutWit.Database.Core/Interfaces/ITransactionTimestampManager.cs
- OutWit.Database.Core/Transactions/TransactionTimestampManager.cs
- OutWit.Database.Core.Tests/Transactions/TransactionTimestampManagerTests.cs
```

### 1.2 Versioned Record Format [x]
- [x] Define `MvccRecord` struct (Value, CreateTimestamp, DeleteTimestamp, CommitTimestamp)
- [x] Serialization/Deserialization
- [x] Visibility rules implementation
- [x] Unit tests

```
Files created:
- OutWit.Database.Core/Mvcc/MvccRecord.cs
- OutWit.Database.Core.Tests/Mvcc/MvccRecordTests.cs
```

### 1.3 MVCC Key-Value Store [x]
- [x] Create `IMvccStore` interface
- [x] Implement `MvccKeyValueStore` wrapper
- [x] Store multiple versions per key
- [x] Version chain management
- [x] Garbage collection of old versions
- [x] Unit tests

```
Files created:
- OutWit.Database.Core/Interfaces/IMvccStore.cs
- OutWit.Database.Core/Stores/MvccKeyValueStore.cs
- OutWit.Database.Core.Tests/Stores/MvccKeyValueStoreTests.cs
```

---

## Phase 2: Snapshot Isolation [COMPLETE]

### 2.1 Snapshot Read [x]
- [x] Implement snapshot visibility rules in MvccRecord
- [x] Read only committed data as of snapshot timestamp
- [x] Ignore changes from transactions started after snapshot

### 2.2 MVCC Transaction [x]
- [x] Create `IMvccTransaction` interface
- [x] Implement `MvccTransaction` class
- [x] Snapshot timestamp assignment at begin
- [x] Write-write conflict detection
- [x] First-committer-wins semantics
- [x] Read-only transaction support

```
Files created:
- OutWit.Database.Core/Interfaces/IMvccTransaction.cs
- OutWit.Database.Core/Transactions/MvccTransaction.cs
```

### 2.3 MVCC Transactional Store [x]
- [x] Implement `MvccTransactionalStore` class
- [x] Support for multiple concurrent readers
- [x] Support for read during write
- [x] Snapshot isolation implementation
- [x] BeginReadOnlyTransaction() method
- [x] Configurable default isolation level
- [x] Unit tests

```
Files created:
- OutWit.Database.Core/Transactions/MvccTransactionalStore.cs
- OutWit.Database.Core.Tests/Transactions/MvccTransactionalStoreTests.cs
```

---

## Phase 3: Concurrent Transactions [PARTIAL]

### 3.1 Transaction Registry [x]
- [x] Track all active transactions (in TransactionTimestampManager)
- [x] Track read-only vs read-write transactions
- [x] Support querying active transaction timestamps

### 3.2 Read-Only Transactions [x]
- [x] Lightweight read-only transaction support
- [x] No locks required for read-only snapshot transactions
- [x] Multiple concurrent read transactions

### 3.3 Transaction Wait Queue [ ]
- [ ] Implement priority-based wait queue
- [ ] FIFO with writer priority option
- [ ] Configurable timeouts
- [ ] Integration with `LockManager`

```
Files to create:
- OutWit.Database.Core/Concurrency/TransactionWaitQueue.cs
- OutWit.Database.Core.Tests/Concurrency/TransactionWaitQueueTests.cs
```

---

## Phase 4: Row-Level Locks [COMPLETE]

### 4.1 Row Lock Manager [x]
- [x] Create `IRowLockManager` interface
- [x] Implement `RowLockManager` class
- [x] Support shared (S) and exclusive (X) locks
- [x] Lock granularity: per-key

```
Files created:
- OutWit.Database.Core/Interfaces/IRowLockManager.cs
- OutWit.Database.Core/Concurrency/RowLockManager.cs
- OutWit.Database.Core.Tests/Concurrency/RowLockManagerTests.cs
```

### 4.2 Lock Modes [x]
- [x] Shared lock (FOR SHARE) - multiple readers
- [x] Exclusive lock (FOR UPDATE) - single writer
- [x] NOWAIT mode - fail immediately if cannot acquire
- [x] SKIP LOCKED mode - skip locked rows

```
Files created:
- OutWit.Database.Core/Concurrency/RowLockMode.cs
- OutWit.Database.Core/Concurrency/RowLockRequest.cs
```

### 4.3 Row Lock Handle [x]
- [x] Create `RowLockHandle` class
- [x] RAII-style lock release
- [x] Async support

```
Files created:
- OutWit.Database.Core/Concurrency/RowLockHandle.cs
- OutWit.Database.Core/Exceptions/RowLockException.cs
```

---

## Phase 5: Deadlock Detection [COMPLETE]

### 5.1 Wait-For Graph [x]
- [x] Create `WaitForGraph` class
- [x] Track which transaction waits for which
- [x] Cycle detection algorithm (DFS-based)
- [x] Find all cycles support
- [x] Thread-safe implementation

```
Files created:
- OutWit.Database.Core/Concurrency/WaitForGraph.cs
- OutWit.Database.Core.Tests/Concurrency/WaitForGraphTests.cs
```

### 5.2 Deadlock Detector [x]
- [x] Create `DeadlockDetector` class
- [x] On-demand cycle detection
- [x] Background periodic detection (optional)
- [x] Victim selection strategies (Youngest, Oldest, LeastWork, MostWaiting)
- [x] Transaction abort on deadlock via exception

```
Files created:
- OutWit.Database.Core/Concurrency/DeadlockDetector.cs
- OutWit.Database.Core.Tests/Concurrency/DeadlockDetectorTests.cs
```

### 5.3 Deadlock Exception [x]
- [x] Create `DeadlockException` class
- [x] Include victim transaction info
- [x] Include cycle participants
- [x] Retry guidance (ShouldRetry property)

```
Files created:
- OutWit.Database.Core/Exceptions/DeadlockException.cs
```

---

## Phase 6: Version Garbage Collection [COMPLETE]

### 6.1 Old Version Cleanup [x]
- [x] Implement `GarbageCollect` method in MvccKeyValueStore
- [x] Determine minimum active snapshot timestamp
- [x] Remove versions not visible to any active transaction
- [x] Background cleanup thread

### 6.2 Cleanup Policies [x]
- [x] Manual cleanup API (RunGarbageCollection)
- [x] Background periodic cleanup (MvccGarbageCollector)
- [x] Configurable collection interval
- [x] Statistics callback support
- [x] Pause/Resume control

```
Files created:
- OutWit.Database.Core/Mvcc/MvccGarbageCollectorOptions.cs
- OutWit.Database.Core/Mvcc/MvccGarbageCollector.cs
- OutWit.Database.Core.Tests/Mvcc/MvccGarbageCollectorTests.cs
```

---

## Phase 7: Integration [COMPLETE]

### 7.1 WitDatabaseBuilder Integration [x]
- [x] Add `EnableMvcc` option
- [x] Add `DefaultIsolationLevel` option
- [x] Add `.WithMvcc()` extension method
- [x] Add `.WithMvcc(IsolationLevel)` extension method
- [x] Add `.WithDefaultIsolationLevel()` extension method

```
Files modified:
- OutWit.Database.Core/Builder/WitDatabaseBuilderOptions.cs
- OutWit.Database.Core/Builder/WitDatabaseBuilderExtensions.cs
```

### 7.2 WitDatabase Integration [x]
- [x] Factory methods work with MVCC support via builder
- [x] Update WitDatabaseBuilder.Build() to use MvccTransactionalStore when MVCC enabled
- [x] Add `SupportsMvcc` property
- [x] Add `BeginReadOnlyTransaction()` method
- [x] Handle underlying store retrieval for MVCC stores

```
Files modified:
- OutWit.Database.Core/Builder/WitDatabaseBuilder.cs
- OutWit.Database.Core/Builder/WitDatabase.cs
```

### 7.3 Isolation Level Implementation
- [x] Snapshot - MVCC snapshot isolation (fully implemented)
- [ ] ReadUncommitted - read without locks, see uncommitted
- [ ] ReadCommitted - read committed data, no snapshot
- [ ] RepeatableRead - read locks held for transaction
- [ ] Serializable - predicate locks (row-level + gap locks)

---

## Phase 8: Testing [PARTIAL]

### 8.1 Unit Tests [x]
- [x] TransactionTimestampManager tests
- [x] MvccRecord tests
- [x] MvccKeyValueStore basic operations
- [x] MvccTransactionalStore tests
- [x] Snapshot visibility rules
- [x] Write-write conflict detection
- [x] Version garbage collection

### 8.2 Integration Tests [x]
- [x] WitDatabaseBuilder with MVCC tests
- [x] MVCC with BTree storage
- [x] MVCC with LSM storage
- [x] Concurrent read transactions
- [x] Snapshot isolation end-to-end

### 8.3 Concurrency Tests [ ]
- [ ] Multi-threaded stress tests
- [ ] Race condition tests
- [ ] Deadlock scenario tests (when implemented)
- [ ] Long-running transaction tests
- [ ] High contention tests

---

## Progress Tracking

| Phase | Status | % Complete |
|-------|--------|------------|
| Phase 1: MVCC Foundation | Complete | 100% |
| Phase 2: Snapshot Isolation | Complete | 100% |
| Phase 3: Concurrent Transactions | Partial | 66% |
| Phase 4: Row-Level Locks | Complete | 100% |
| Phase 5: Deadlock Detection | Complete | 100% |
| Phase 6: Garbage Collection | Complete | 100% |
| Phase 7: Integration | Complete | 100% |
| Phase 8: Testing | Partial | 90% |
| **TOTAL** | | **~95%** |

---

## Files Created/Modified

### New Files
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
18. `OutWit.Database.Core/Exceptions/RowLockException.cs`
19. `OutWit.Database.Core/Exceptions/DeadlockException.cs`
20. `OutWit.Database.Core.Tests/Transactions/TransactionTimestampManagerTests.cs`
21. `OutWit.Database.Core.Tests/Transactions/MvccTransactionalStoreTests.cs`
22. `OutWit.Database.Core.Tests/Transactions/MvccTransactionRowLockTests.cs`
23. `OutWit.Database.Core.Tests/Mvcc/MvccRecordTests.cs`
24. `OutWit.Database.Core.Tests/Mvcc/MvccGarbageCollectorTests.cs`
25. `OutWit.Database.Core.Tests/Stores/MvccKeyValueStoreTests.cs`
26. `OutWit.Database.Core.Tests/Concurrency/RowLockManagerTests.cs`
27. `OutWit.Database.Core.Tests/Concurrency/WaitForGraphTests.cs`
28. `OutWit.Database.Core.Tests/Concurrency/DeadlockDetectorTests.cs`

### Modified Files
1. `OutWit.Database.Core/Builder/WitDatabaseBuilderOptions.cs` - Added MVCC options
2. `OutWit.Database.Core/Builder/WitDatabaseBuilderExtensions.cs` - Added MVCC extensions
3. `OutWit.Database.Core/Builder/WitDatabaseBuilder.cs` - Use MvccTransactionalStore when MVCC enabled
4. `OutWit.Database.Core/Builder/WitDatabase.cs` - Added SupportsMvcc, BeginReadOnlyTransaction
5. `OutWit.Database.Core.Tests/Builder/WitDatabaseBuilderTests.cs` - Added MVCC integration tests

---

## Next Steps

1. **Multi-threaded Stress Tests** - Verify MVCC under high concurrency
2. **Transaction Wait Queue** - Optional priority-based wait queue (Phase 3.3)

---

## Design Decisions Made

### 1. Version Storage Strategy
**Decision:** Store version metadata inline with values (32 bytes header: CreateTs + DeleteTs + TxId + CommitTs)
- Format: `[key][inverted_timestamp]` -> `MvccRecord`
- Inverted timestamp ensures newest versions come first in sorted order

### 2. Conflict Detection
**Decision:** First-committer-wins with write-write conflict detection
- Detect at commit time via `HasWriteConflict()`
- Throw `InvalidOperationException` on conflict

### 3. Snapshot Assignment
**Decision:** Assign snapshot timestamp at `BeginTransaction()` for Snapshot isolation
- Consistent reads from transaction start

### 4. MVCC Store Architecture
**Decision:** Wrapper pattern - `MvccKeyValueStore` wraps any `IKeyValueStore`
- Works with BTree, LSM, InMemory
- Same approach as `VersionedKeyValueStore` and `TransactionalStore`

### 5. Commit Timestamp Tracking
**Decision:** Store commit timestamp in MvccRecord for accurate visibility
- EffectiveTimestamp = CommitTimestamp if committed via transaction, else CreateTimestamp
- Enables accurate visibility for snapshots taken between create and commit

---

**Last Updated:** 2025-01-17
