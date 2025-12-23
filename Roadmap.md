# WitDatabase - Complete Roadmap

**Version:** 2.0  
**Based on:** WitSql.md specification v1.2  
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

**Version Legend:**
- **v1** = First release
- **v2** = Deferred to second release

---

## Version-Specific Roadmaps

For detailed version-specific information, see:

| File | Description |
|------|-------------|
| [Roadmap.v1.md](Roadmap.v1.md) | All features for v1 release |
| [Roadmap.v2.md](Roadmap.v2.md) | Features deferred to v2 |
| [Roadmap.Core.md](Roadmap.Core.md) | Core storage layer roadmap |
| [Roadmap.Parser.md](Roadmap.Parser.md) | SQL Parser roadmap |
| [Roadmap.Engine.md](Roadmap.Engine.md) | SQL Engine roadmap |

---

## Overall Progress Summary

| Component | v1 Features | Implemented | Progress |
|-----------|-------------|-------------|----------|
| **OutWit.Database.Core** | 70 | 70 | 100% |
| **OutWit.Database.Parser** | 290 | 290 | 100% |
| **OutWit.Database** (Engine) | 200+ | 0 | 0% |

### v2 Features (Deferred)

| Component | v2 Features | Implemented | Progress |
|-----------|-------------|-------------|----------|
| **OutWit.Database.Core** | 9 | 0 | 0% |
| **OutWit.Database.Parser** | 18 | 0 | 0% |
| **OutWit.Database** (Engine) | 25+ | 0 | 0% |

---

## Core Component Status

### v1 Complete

| Category | Status | Features |
|----------|--------|----------|
| Key-Value Store | 100% | Get, Put, Delete, Scan, Flush |
| Storage Engines | 100% | B+Tree, LSM-Tree, InMemory |
| Storage Backends | 100% | File, Memory, Encrypted |
| Encryption | 100% | AES-GCM, ChaCha20 |
| Crash Recovery | 100% | WAL, Rollback Journal |
| Basic Concurrency | 100% | Locks, Writer Priority |
| Isolation Levels + MVCC | 100% | All 5 levels |
| Row-level Locks | 100% | FOR UPDATE/SHARE, NOWAIT, SKIP LOCKED |
| Deadlock Detection | 100% | Wait-for graph, Victim selection |
| Transaction Wait Queue | 100% | Priority-based queuing |
| Savepoints | 100% | Create, Rollback, Release |
| Multiple Result Sets | 100% | IMultiResultReader |
| Query Context | 100% | AffectedRows, LastInsertId |
| Secondary Indexes | 100% | Unique, Composite, Auto-update |
| Bulk Operations | 100% | BulkPut, BulkDelete |
| Statistics | 100% | Row count, Index stats |
| ROWVERSION | 100% | Optimistic concurrency |
| MVCC Garbage Collection | 100% | Background GC |

### v2 Deferred

| Category | Status | Features |
|----------|--------|----------|
| Cursor Support | 0% | ICursor, Scrollable |
| Advanced Statistics | 0% | ANALYZE, Cardinality |
| VACUUM API | 0% | Explicit vacuum, Incremental |

---

## Parser Component Status

### v1 Complete

| Category | Status |
|----------|--------|
| Data Types | 100% |
| DDL - CREATE TABLE | 100% |
| DDL - DROP/ALTER TABLE | 100% |
| DDL - INDEX | 100% |
| DDL - VIEW | 100% |
| DDL - TRIGGER | 100% |
| DDL - SEQUENCE | 100% |
| DML - SELECT | 100% |
| DML - INSERT | 100% |
| DML - UPDATE | 100% |
| DML - DELETE | 100% |
| DML - TRUNCATE/MERGE | 100% |
| CTE / Set Operations | 100% |
| Subqueries | 100% |
| Operators | 100% |
| Conditional Expressions | 100% |
| Literals & Parameters | 100% |
| Collation | 100% |
| All Functions | 100% |
| Window Functions | 100% |
| Transactions | 100% |
| Comments | 100% |

### v2 Deferred

| Category | Status | Features |
|----------|--------|----------|
| User-Defined Functions | 0% | CREATE/DROP FUNCTION |
| Stored Procedures | 0% | CREATE/DROP/CALL PROCEDURE |
| EXPLAIN | 0% | Query analysis |
| Database Administration | 0% | CREATE/DROP DATABASE, VACUUM, PRAGMA |

---

## Engine Component Status

### v1 Required Features

| Category | Status |
|----------|--------|
| Query Execution Infrastructure | 0% |
| Data Type Implementation | 0% |
| DDL Execution | 0% |
| DML Execution | 0% |
| Expression Evaluation | 0% |
| Built-in Functions | 0% |
| Window Functions | 0% |
| CTE and Set Operations | 0% |
| Transaction Support | 0% |
| Schema Information | 0% |
| ADO.NET Provider | 0% |
| Query Optimization | 0% |

### v2 Deferred

| Category | Features |
|----------|----------|
| User-Defined Functions Execution | CREATE/DROP/INVOKE |
| Stored Procedures Execution | CREATE/DROP/CALL |
| Query Analysis | EXPLAIN, EXPLAIN ANALYZE |
| Database Administration | CREATE/DROP DATABASE, VACUUM |
| Cursor Support | DECLARE, FETCH, CLOSE |
| Advanced Optimization | Histograms, Adaptive |

---

## Implementation Timeline

### v1 Phases

#### Phase 1: MVP (4-6 weeks)
- Query executor infrastructure
- Basic SELECT, INSERT, UPDATE, DELETE
- CREATE/DROP TABLE
- ADO.NET provider basics

#### Phase 2: JOINs and Indexes (3-4 weeks)
- JOIN operations
- Index creation and usage
- GROUP BY, HAVING
- Subqueries, CTE

#### Phase 3: Transactions and Concurrency (3-4 weeks)
- Isolation levels
- Savepoints
- FOR UPDATE / FOR SHARE
- MERGE statement

#### Phase 4: Production Ready (4-6 weeks)
- Window functions
- Views and triggers
- All v1 functions
- INFORMATION_SCHEMA

### v2 Phases (Future)

#### Phase 5: Advanced Features
- User-defined functions
- Stored procedures
- EXPLAIN / EXPLAIN ANALYZE
- Database administration

---

## Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| OutWit.Database.Core | 1811+ | Passing |
| OutWit.Database.Parser | 1000+ | Passing |
| OutWit.Database (Engine) | 0 | Not started |

---

## Files Reference

| File | Content |
|------|---------|
| `Roadmap.md` | This complete roadmap |
| `Roadmap.v1.md` | v1 detailed roadmap |
| `Roadmap.v2.md` | v2 deferred features |
| `Roadmap.Core.md` | Core-only roadmap |
| `Roadmap.Parser.md` | Parser-only roadmap |
| `Roadmap.Engine.md` | Engine-only roadmap |
| `WitSql.md` | Language Specification |
| `OutWit.Database.Core.TODO.md` | Core TODO list |
| `CODE_STYLE_GUIDE.md` | Code style guide |

---

**Last Updated:** 2025-01-17
