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

---

## Missing Components

### Category 1: Transaction Isolation Levels

- [x] **1.1** `IsolationLevel` enum (ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, Snapshot)
- [x] **1.2** MVCC (Multi-Version Concurrency Control) for Snapshot isolation
- [x] **1.3** Extend `ITransaction` to specify isolation level
- [x] **1.4** Record versioning (transaction timestamp / row version)

### Category 2: Row-level Locks

- [ ] **2.1** `RowLockManager` - row-level locks (not just database-level)
- [ ] **2.2** `FOR UPDATE` / `FOR SHARE` - shared/exclusive row locks
- [ ] **2.3** `NOWAIT` / `SKIP LOCKED` - non-blocking lock modes
- [ ] **2.4** Deadlock detection

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
- [ ] **11.3** Transaction wait queue with priorities

### Category 12: ROWVERSION / Concurrency Tokens

- [x] **12.1** Auto-incrementing row version column support
- [x] **12.2** Optimistic concurrency check at kernel level
- [x] **12.3** Conditional Put/Delete (version check)

---

## Implementation Priorities

### MVP (Minimum for ADO.NET) - DONE

| # | Component | Priority | Status |
|---|-----------|----------|--------|
| 6.1-6.5 | Query execution context | P0 Critical | [x] Done |
| 7.1-7.5 | Secondary indexes | P0 Critical | [x] Done |
| 3.1-3.4 | Savepoints | P1 Important | [x] Done |
| 9.1-9.2 | Basic statistics | P1 Important | [x] Done |

### Production Ready (Full EF Core Support)

| # | Component | Priority | Status |
|---|-----------|----------|--------|
| 1.1-1.4 | Isolation levels + MVCC | P0 Critical | [x] Done |
| 2.1-2.4 | Row-level locks | P0 Critical | [ ] TODO |
| 4.1-4.3 | Multiple result sets | P1 Important | [x] Done |
| 8.1-8.3 | Bulk operations | P1 Important | [x] Done |
| 11.1-11.3 | Concurrent transactions | P0 Critical | [~] Partial |
| 12.1-12.3 | ROWVERSION support | P1 Important | [x] Done |

### Nice to Have (v2)

| # | Component | Priority | Status |
|---|-----------|----------|--------|
| 5.1-5.3 | Cursor support | P2 Optional | [ ] v2 |
| 9.3-9.4 | Advanced statistics | P2 Optional | [ ] v2 |
| 10.1-10.3 | VACUUM API | P2 Optional | [ ] v2 |

---

## Notes

1. **Secondary indexes** - critical for any SQL engine. Without them, efficient filtering and JOIN operations are impossible.

2. **MVCC** - **IMPLEMENTED**. Required for Snapshot isolation and concurrent reads. Enables EF Core to work in multi-user scenarios.

3. **Savepoints** - used by EF Core for nested transactions and SaveChanges with retry.

4. **Query execution context** - ADO.NET requires information about affected rows count and last insert id.

5. **Current state** - MVCC is implemented, enabling snapshot isolation and concurrent read transactions. Row-level locks and deadlock detection are still needed for complete EF Core support.
