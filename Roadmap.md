# WitDatabase - Complete Roadmap

**Version:** 2.1  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-01-26

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
| **OutWit.Database.Core** | 70 | 70 | 100% ? |
| **OutWit.Database.Parser** | 290 | 290 | 100% ? |
| **OutWit.Database** (Engine) | 200+ | ~130 | ~65% ?? |

### v2 Features (Deferred)

| Component | v2 Features | Implemented | Progress |
|-----------|-------------|-------------|----------|
| **OutWit.Database.Core** | 9 | 0 | 0% |
| **OutWit.Database.Parser** | 18 | 0 | 0% |
| **OutWit.Database** (Engine) | 25+ | 0 | 0% |

---

## Core Component Status

### v1 Complete ?

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

### v1 Complete ?

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

| Category | Status | Details |
|----------|--------|---------|
| Query Execution Infrastructure | 90% | All core components complete |
| Data Type Implementation | 100% | All types supported |
| DDL Execution | 90% | Tables, Views, Triggers, Sequences ? |
| DML Execution (SELECT) | 95% | JOINs, Subqueries, Set Ops ? |
| DML Execution (INSERT/UPDATE/DELETE) | 85% | Core operations, triggers ? |
| Expression Evaluation | 100% | Including subqueries ? |
| Built-in Functions (scalar) | 100% | 60+ functions ? |
| Built-in Functions (aggregate) | 100% | COUNT, SUM, AVG, MIN, MAX, GROUP_CONCAT ? |
| Subquery Support | 100% | Scalar, EXISTS, IN, ANY/ALL, Correlated ? |
| Window Functions | 0% | Not started |
| CTE Execution | 0% | Not started |
| Transaction Support | 0% | Not started |
| Schema Information | 0% | INFORMATION_SCHEMA not started |
| ADO.NET Provider | 0% | Not started |
| Query Optimization | 10% | Basic plan building only |

### Recently Completed (2025-01-26)

- ? Full subquery support (scalar, EXISTS, IN, ANY/SOME/ALL)
- ? Correlated subqueries
- ? IteratorAlias fix for proper column name aliasing
- ? ExpressionEvaluator.Subquery.cs - new partial class
- ? OuterRow support in column reference evaluation

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

#### Phase 1: MVP - ? COMPLETE
- [x] Expression evaluator ?
- [x] All scalar functions ?
- [x] Parameter binding ?
- [x] Query executor infrastructure ?
- [x] Basic SELECT, INSERT, UPDATE, DELETE ?
- [x] CREATE/DROP TABLE ?
- [x] Constraint validation ?
- [ ] ADO.NET provider basics

#### Phase 2: JOINs and Advanced Queries - ? MOSTLY COMPLETE
- [x] JOIN operations (INNER, LEFT, RIGHT, FULL, CROSS) ?
- [x] Index creation (metadata) ?
- [x] GROUP BY, HAVING ?
- [x] Subqueries (scalar, IN, EXISTS, ANY/ALL) ?
- [x] Correlated subqueries ?
- [x] Set operations (UNION, INTERSECT, EXCEPT) ?
- [ ] CTE (WITH clause)
- [ ] Index usage in queries

#### Phase 3: Transactions and Concurrency (Planned)
- [ ] Isolation levels
- [ ] Savepoints
- [ ] FOR UPDATE / FOR SHARE
- [ ] MERGE statement

#### Phase 4: Production Ready (Planned)
- [ ] Window functions
- [ ] Recursive CTE
- [x] Views and triggers ?
- [ ] INFORMATION_SCHEMA
- [ ] Basic query optimization

### v2 Phases (Future)

#### Phase 5: Advanced Features
- [ ] User-defined functions
- [ ] Stored procedures
- [ ] EXPLAIN / EXPLAIN ANALYZE
- [ ] Database administration

---

## Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| OutWit.Database.Core | 1811+ | ? Passing |
| OutWit.Database.Parser | 1000+ | ? Passing |
| OutWit.Database (Engine) | 893 | ? Passing |
| **Total** | **3700+** | ? Passing |

### Engine Test Breakdown

| Category | Tests |
|----------|-------|
| ExpressionEvaluator | 194 |
| StatementExecutor | 145 |
| Iterators | 119 |
| QueryPlanner | 50 |
| WitSqlValue | 130 |
| Definitions | 90 |
| Schema | 50 |
| WitSqlEngine Integration | 115 |

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

## Recent Changes

### 2025-01-26
- Engine: Added full subquery support
  - Scalar subqueries
  - EXISTS / NOT EXISTS
  - IN (subquery) / NOT IN (subquery)
  - ANY / SOME / ALL
  - Correlated subqueries
- Engine: Fixed IteratorAlias for proper column name aliasing
- Engine: 893 tests passing (up from 151)
- Updated roadmaps with current progress

---

**Last Updated:** 2025-01-26
