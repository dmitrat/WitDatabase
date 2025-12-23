# MVCC and Concurrent Transactions - Implementation Plan

**Version:** 1.1  
**Last Updated:** 2025-01-16  
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
- [x] Define `MvccRecord` struct (Value, CreateTimestamp, DeleteTimestamp)
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

## Phase 4: Row-Level Locks [ ]

### 4.1 Row Lock Manager
- [ ] Create `IRowLockManager` interface
- [ ] Implement `RowLockManager` class
- [ ] Support shared (S) and exclusive (X) locks
- [ ] Lock granularity: per-key

```
Files to create:
- OutWit.Database.Core/Interfaces/IRowLockManager.cs
- OutWit.Database.Core/Concurrency/RowLockManager.cs
- OutWit.Database.Core.Tests/Concurrency/RowLockManagerTests.cs
```

### 4.2 Lock Modes
- [ ] Shared lock (FOR SHARE) - multiple readers
- [ ] Exclusive lock (FOR UPDATE) - single writer
- [ ] NOWAIT mode - fail immediately if cannot acquire
- [ ] SKIP LOCKED mode - skip locked rows

```
Files to create:
- OutWit.Database.Core/Concurrency/RowLockMode.cs
- OutWit.Database.Core/Concurrency/RowLockRequest.cs
```

### 4.3 Row Lock Handle
- [ ] Create `RowLockHandle` class
- [ ] RAII-style lock release
- [ ] Async support

```
Files to create:
- OutWit.Database.Core/Concurrency/RowLockHandle.cs
```

---

## Phase 5: Deadlock Detection [ ]

### 5.1 Wait-For Graph
- [ ] Create `WaitForGraph` class
- [ ] Track which transaction waits for which
- [ ] Cycle detection algorithm

```
Files to create:
- OutWit.Database.Core/Concurrency/WaitForGraph.cs
- OutWit.Database.Core.Tests/Concurrency/WaitForGraphTests.cs
```

### 5.2 Deadlock Detector
- [ ] Create `DeadlockDetector` class
- [ ] Periodic or on-demand cycle detection
- [ ] Victim selection strategy (youngest, lowest priority)
- [ ] Transaction abort on deadlock

```
Files to create:
- OutWit.Database.Core/Concurrency/DeadlockDetector.cs
- OutWit.Database.Core.Tests/Concurrency/DeadlockDetectorTests.cs
```

### 5.3 Deadlock Exception
- [ ] Create `DeadlockException` class
- [ ] Include victim transaction info
- [ ] Retry guidance

```
Files to create:
- OutWit.Database.Core/Exceptions/DeadlockException.cs
```

---

## Phase 6: Version Garbage Collection [PARTIAL]

### 6.1 Old Version Cleanup [x]
- [x] Implement `GarbageCollect` method in MvccKeyValueStore
- [x] Determine minimum active snapshot timestamp
- [x] Remove versions not visible to any active transaction
- [ ] Background cleanup thread

### 6.2 Cleanup Policies [ ]
- [x] Manual cleanup API (RunGarbageCollection)
- [ ] Immediate cleanup on transaction commit
- [ ] Lazy cleanup during scan
- [ ] Background periodic cleanup

---

## Phase 7: Integration [PARTIAL]

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

### 7.2 WitDatabase Integration [ ]
- [ ] Factory methods with MVCC support
- [ ] Auto-detection of MVCC-enabled databases
- [ ] Update WitDatabaseBuilder.Build() to use MvccTransactionalStore

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

### 8.2 Integration Tests [ ]
- [ ] All storage backends (BTree, LSM, InMemory)
- [ ] With and without encryption
- [ ] End-to-end WitDatabase tests with MVCC

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
| Phase 4: Row-Level Locks | Not Started | 0% |
| Phase 5: Deadlock Detection | Not Started | 0% |
| Phase 6: Garbage Collection | Partial | 50% |
| Phase 7: Integration | Partial | 40% |
| Phase 8: Testing | Partial | 50% |
| **TOTAL** | | **~55%** |

---

## Files Created/Modified

### New Files
1. `OutWit.Database.Core/Interfaces/ITransactionTimestampManager.cs`
2. `OutWit.Database.Core/Interfaces/IMvccStore.cs`
3. `OutWit.Database.Core/Interfaces/IMvccTransaction.cs`
4. `OutWit.Database.Core/Transactions/TransactionTimestampManager.cs`
5. `OutWit.Database.Core/Transactions/MvccTransaction.cs`
6. `OutWit.Database.Core/Transactions/MvccTransactionalStore.cs`
7. `OutWit.Database.Core/Mvcc/MvccRecord.cs`
8. `OutWit.Database.Core/Stores/MvccKeyValueStore.cs`
9. `OutWit.Database.Core.Tests/Transactions/TransactionTimestampManagerTests.cs`
10. `OutWit.Database.Core.Tests/Transactions/MvccTransactionalStoreTests.cs`
11. `OutWit.Database.Core.Tests/Mvcc/MvccRecordTests.cs`
12. `OutWit.Database.Core.Tests/Stores/MvccKeyValueStoreTests.cs`

### Modified Files
1. `OutWit.Database.Core/Builder/WitDatabaseBuilderOptions.cs` - Added MVCC options
2. `OutWit.Database.Core/Builder/WitDatabaseBuilderExtensions.cs` - Added MVCC extensions

---

## Next Steps

1. **Complete WitDatabase Integration** - Update `WitDatabaseBuilder.Build()` to create `MvccTransactionalStore` when MVCC is enabled
2. **Row-Level Locks (Phase 4)** - Required for `FOR UPDATE` / `FOR SHARE` support
3. **Deadlock Detection (Phase 5)** - Required for production row-level locking
4. **Background GC** - Implement background garbage collection thread
5. **Integration Tests** - Test with all storage backends and encryption

---

## Design Decisions Made

### 1. Version Storage Strategy
**Decision:** Store version metadata inline with values (24 bytes header: CreateTs + DeleteTs + TxId)
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

---

**Last Updated:** 2025-01-16
