# OutWit.Database.Core - Roadmap

**Version:** 2.0  
**Based on:** OutWit.Database.Core.TODO.md  
**Last Updated:** 2025-01-17

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
| Isolation Levels + MVCC | 5 | 0 | 100% |
| Row-level Locks | 4 | 0 | 100% |
| Deadlock Detection | 4 | 0 | 100% |
| Transaction Wait Queue | 5 | 0 | 100% |
| Savepoints | 4 | 0 | 100% |
| Multiple Result Sets | 3 | 0 | 100% |
| Cursor Support | 0 | 4 | 0% - v2 |
| Query Context | 5 | 0 | 100% |
| Secondary Indexes | 8 | 0 | 100% |
| Storage Detection | 2 | 0 | 100% |
| Bulk Operations | 3 | 0 | 100% |
| Statistics | 2 | 2 | 50% - v2 items |
| VACUUM/Compaction | 0 | 3 | 0% - v2 |
| Concurrent Transactions | 3 | 0 | 100% |
| ROWVERSION | 3 | 0 | 100% |
| MVCC Garbage Collection | 2 | 0 | 100% |
| **TOTAL v1** | **70** | **0** | **100%** |
| **TOTAL (incl v2)** | **70** | **9** | **89%** |

---

## ?? v1 Implementation Complete!

All P0 (Critical) and P1 (Important) features have been implemented:

### Completed Features

| Feature | Status | Tests |
|---------|--------|-------|
| MVCC (Multi-Version Concurrency Control) | ? | 50+ tests |
| All Isolation Levels | ? | 20+ tests |
| Row-Level Locks (FOR UPDATE/SHARE) | ? | 30+ tests |
| NOWAIT / SKIP LOCKED modes | ? | Included |
| Deadlock Detection | ? | 15+ tests |
| Transaction Wait Queue | ? | 24+ tests |
| Background Garbage Collection | ? | 14+ tests |
| Stress Tests | ? | 24 tests |

### Total Test Count: 1811 tests passing

---

## 3. Implementation Priorities

### 3.1 Phase 1: MVP for ADO.NET [x] ? COMPLETE

All Phase 1 features are complete.

### 3.2 Phase 2: EF Core Compatibility [x] ? COMPLETE

| Feature | Priority | Status |
|---------|----------|--------|
| `IsolationLevel` enum | P0 | ? Done |
| MVCC implementation | P0 | ? Done |
| WitDatabase MVCC integration | P0 | ? Done |
| `RowLockManager` | P0 | ? Done |
| Deadlock detection | P0 | ? Done |
| Multiple concurrent transactions | P0 | ? Done |
| ROWVERSION support | P1 | ? Done |
| Transaction wait queue | P1 | ? Done |

### 3.3 Phase 3: Production Ready [x] ? COMPLETE

| Feature | Priority | Status |
|---------|----------|--------|
| `IMultiResultReader` | P1 | ? Done |
| Bulk operations | P1 | ? Done |
| Statistics | P1 | ? Done |
| `NOWAIT` / `SKIP LOCKED` | P1 | ? Done |
| Background GC | P1 | ? Done |
| Stress tests | P1 | ? Done |

### 3.4 Phase 4: Nice to Have (Deferred to v2)

| Feature | Priority | Status |
|---------|----------|--------|
| `ICursor` interface | P2 - v2 | [ ] Deferred |
| VACUUM API | P2 - v2 | [ ] Deferred |
| `ANALYZE` support | P2 - v2 | [ ] Deferred |
| Cardinality estimation | P2 - v2 | [ ] Deferred |

---

## 2. Missing Components [ ]

### 2.1 Row-level Locks [x] ? COMPLETE

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `RowLockManager` class | ? Done | P0 | SS2.1 |
| `FOR UPDATE` / `FOR SHARE` support | ? Done | P0 | SS2.2 |
| `NOWAIT` / `SKIP LOCKED` modes | ? Done | P1 | SS2.3 |
| Deadlock detection | ? Done | P0 | SS2.4 |
| Transaction wait queue | ? Done | P1 | SS11.3 |

### 2.2 Cursor Support (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| `ICursor` interface | [ ] | P2 - v2 | SS5.1 |
| Forward-only mode | [ ] | P2 - v2 | SS5.2 |
| Scrollable mode | [ ] | P2 - v2 | SS5.2 |
| Fetch size (batching) | [ ] | P2 - v2 | SS5.3 |

### 2.3 Statistics and Metadata

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Table row count (approximate/exact) | ? Done | P1 | SS9.1 |
| Index statistics for query optimizer | ? Done | P1 | SS9.2 |
| `ANALYZE` command support | [ ] | P2 - v2 | SS9.3 |
| Column cardinality estimation | [ ] | P2 - v2 | SS9.4 |

### 2.4 VACUUM / Compaction API (Deferred to v2)

| Feature | Status | Priority | TODO Ref |
|---------|--------|----------|----------|
| Explicit `Vacuum()` method for BTree | [ ] | P2 - v2 | SS10.1 |
| Incremental vacuum support | [ ] | P2 - v2 | SS10.2 |
| Compaction progress/status API | [ ] | P2 - v2 | SS10.3 |

---
