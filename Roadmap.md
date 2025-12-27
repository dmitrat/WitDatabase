# WitDatabase - Complete Roadmap

**Version:** 2.4  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-02-04

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
| **OutWit.Database** (Engine) | 200+ | ~200 | ~97% ?? |

### v2 Features (Deferred)

| Component | v2 Features | Implemented | Progress |
|-----------|-------------|-------------|----------|
| **OutWit.Database.Core** | 9 | 0 | 0% |
| **OutWit.Database.Parser** | 18 | 0 | 0% |
| **OutWit.Database** (Engine) | 30+ | 0 | 0% |

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
| Query Execution Infrastructure | 100% | All core components complete |
| Data Type Implementation | 100% | All types supported |
| DDL Execution | 100% | Tables, Views, Triggers, Sequences ? |
| DML Execution (SELECT) | 100% | JOINs, Subqueries, Set Ops ? |
| DML Execution (INSERT/UPDATE/DELETE) | 100% | Core operations, triggers, RETURNING ? |
| DML Enhancements | 100% | UPSERT, TRUNCATE, MERGE ? |
| Expression Evaluation | 100% | Including subqueries ? |
| Built-in Functions (scalar) | 100% | 60+ functions ? |
| Built-in Functions (aggregate) | 100% | COUNT, SUM, AVG, MIN, MAX, GROUP_CONCAT ? |
| JSON Functions | 100% | JSON_EXTRACT, JSON_VALUE, JSON_QUERY, JSON_SET, etc. ? |
| Subquery Support | 100% | Scalar, EXISTS, IN, ANY/ALL, Correlated ? |
| Transaction Support | 100% | BEGIN/COMMIT/ROLLBACK, Savepoints, Isolation ? |
| Row-Level Locking | 100% | FOR UPDATE/SHARE, NOWAIT, SKIP LOCKED ? |
| Index Implementation | 100% | Seek, Range Scan, Auto-update, Partial, Expression, Covering ? |
| ALTER TABLE | 100% | ADD/DROP CONSTRAINT, ADD COLUMN with DEFAULT ? |
| Computed Columns | 100% | STORED (auto-recalc), VIRTUAL (on-the-fly) ? |
| CTE Execution | 100% | Simple, Multiple, Recursive, Caching ? |
| Window Functions | 100% | All ranking, value, aggregate window functions ? |
| Schema Information | 100% | INFORMATION_SCHEMA views complete ? |
| Query Optimization | 100% | Index selection, Plan caching, Join ordering ? |
| ADO.NET Provider | 0% | Not started |

### Recently Completed (2025-02-04)

- ? **Query Optimization Complete**:
  - Index selection (cost-based) for WHERE clause predicates
  - Query plan caching with LRU eviction and TTL
  - Join order optimization (greedy algorithm)
  - Implicit cross join optimization (`FROM a, b, c`)
  - Explicit JOIN optimization (swap sides for INNER/CROSS)
  - Semantic preservation for LEFT/RIGHT/FULL joins
  - 64 optimization tests passing

### Previously Completed (2025-02-03)

- ? **JSON Functions Complete**:
  - `JSON_EXTRACT(json, path)` - extract any value at path
  - `JSON_VALUE(json, path)` - extract scalar (NULL for objects/arrays)
  - `JSON_QUERY(json, path)` - extract object/array (NULL for scalars)
  - `JSON_SET(json, path, value)` - set value at path
  - `JSON_INSERT(json, path, value)` - insert only if not exists
  - `JSON_REPLACE(json, path, value)` - replace only if exists
  - `JSON_REMOVE(json, path)` - remove value at path
  - `JSON_TYPE(json)` - get type name
  - `JSON_ARRAY_LENGTH(json)` - get array length
  - `JSON_VALID(str)` - validate JSON string
  - `JSON_ARRAY(values...)` - construct array
  - `JSON_OBJECT(key1, val1, ...)` - construct object
- ? 42 JSON function tests passing

### Previously Completed (2025-02-02)

- ? **DML Enhancements Complete**:
  - `INSERT ... RETURNING` - returns inserted rows with auto-generated IDs
  - `UPDATE ... RETURNING` - returns updated rows
  - `DELETE ... RETURNING` - returns deleted rows
  - `INSERT OR REPLACE` / `INSERT OR IGNORE`
  - `ON CONFLICT DO UPDATE` (UPSERT) with EXCLUDED pseudo-table
  - `ON CONFLICT DO NOTHING`
  - `TRUNCATE TABLE` - fast delete with auto-increment reset
  - `MERGE` - full UPSERT with WHEN MATCHED/NOT MATCHED clauses
  - Complex conditions in MERGE (AND, OR, expressions, subquery sources)
- ? **Code Refactoring**:
  - Split `StatementExecutor.Dml.cs` into separate files for better maintainability
- ? 62 DML enhancement tests passing

### Previously Completed (2025-02-01)

- ? **Window Functions Complete**:
  - ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST
  - LAG, LEAD with offset and default value
  - FIRST_VALUE, LAST_VALUE, NTH_VALUE
  - Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER)
  - PARTITION BY and ORDER BY with NULLS FIRST/LAST
  - 24 tests passing
- ? **CTE Execution Complete**:
  - Simple CTEs with explicit column names
  - Multiple CTEs and CTE referencing another CTE
  - Recursive CTEs with max depth protection
  - CTE caching for multiple references
  - 43 tests passing

### Previously Completed (2025-01-31)

- ? **ALTER TABLE implementation complete**
  - `ADD/DROP CONSTRAINT` (CHECK, UNIQUE, FOREIGN KEY)
  - `ADD COLUMN with DEFAULT` (populates existing rows)
  - Computed columns **STORED** (auto-recalculate on UPDATE, auto-calculate on INSERT)
  - Computed columns **VIRTUAL** (evaluated on-the-fly in all iterators)
  - INDEX on STORED computed columns
  - Prevent direct INSERT/UPDATE into computed columns
  - VIRTUAL columns evaluation in IteratorIndexSeek and IteratorIndexRangeScan

### Previously Completed (2025-01-30)

- Engine: Transaction support complete (BEGIN/COMMIT/ROLLBACK, Savepoints)
- Engine: FOR UPDATE/FOR SHARE locking complete
- Engine: Index implementation complete (seek, range, auto-update, partial, expression, covering)

### Previously Completed (2025-01-26)

- Engine: Added full subquery support
  - Scalar subqueries
  - EXISTS / NOT EXISTS
  - IN (subquery) / NOT IN (subquery)
  - ANY / SOME / ALL
  - Correlated subqueries
- Engine: Fixed IteratorAlias for proper column name aliasing

### v2 Deferred

| Category | Features |
|----------|----------|
| Window Frame Clause | ROWS/RANGE BETWEEN |
| User-Defined Functions Execution | CREATE/DROP/INVOKE |
| Stored Procedures Execution | CREATE/DROP/CALL |
| Query Analysis | EXPLAIN, EXPLAIN ANALYZE |
| Database Administration | CREATE/DROP DATABASE, VACUUM |
| Cursor Support | DECLARE, FETCH, CLOSE |
| Advanced Optimization | Histograms, Adaptive |
| Cascading Computed Columns | Cross-table STORED column recalculation |

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

#### Phase 2: JOINs and Advanced Queries - ? COMPLETE
- [x] JOIN operations (INNER, LEFT, RIGHT, FULL, CROSS) ?
- [x] Index creation (metadata) ?
- [x] GROUP BY, HAVING ?
- [x] Subqueries (scalar, IN, EXISTS, ANY/ALL) ?
- [x] Correlated subqueries ?
- [x] Set operations (UNION, INTERSECT, EXCEPT) ?
- [x] CTE (WITH clause) ?
- [x] Index usage in queries ?

#### Phase 3: Transactions and Concurrency - ? COMPLETE
- [x] Isolation levels ?
- [x] Savepoints ?
- [x] FOR UPDATE / FOR SHARE ?
- [x] MERGE statement ?
- [x] ROWVERSION support ?

#### Phase 4: Production Ready - ? COMPLETE
- [x] Index implementation ?
- [x] ALTER TABLE (constraints, defaults) ?
- [x] Computed columns (STORED, VIRTUAL) ?
- [x] Window functions ?
- [x] Recursive CTE ?
- [x] Views and triggers ?
- [x] JSON functions ?
- [x] INFORMATION_SCHEMA ?
- [x] Basic query optimization ?
- [x] Cascading deletes (FK ON DELETE CASCADE/SET NULL/SET DEFAULT) ?
- [x] Query timeout cancellation ?

### v2 Phases (Future)

#### Phase 5: Advanced Features
- [ ] User-defined functions
- [ ] Stored procedures
- [ ] EXPLAIN / EXPLAIN ANALYZE
- [ ] Database administration
- [ ] Window frame clause (ROWS/RANGE BETWEEN)
- [ ] Cascading computed columns (cross-table)

---

## Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| OutWit.Database.Core | 1811+ | ? Passing |
| OutWit.Database.Parser | 1000+ | ? Passing |
| OutWit.Database (Engine) | 1395 | ? Passing |
| **Total** | **4206+** | ? Passing |

### Engine Test Breakdown

| Category | Tests |
|----------|-------|
| ExpressionEvaluator | 194 |
| StatementExecutor | 162 |
| Iterators | 119 |
| QueryPlanner | 50 |
| QueryOptimizer | 14 |
| QueryPlanCache | 12 |
| JoinOrderOptimizer | 11 |
| WitSqlValue | 148 |
| WitSqlEngine Integration | 132 |
| WitSqlEngine Index | 67 |
| WitSqlEngine ALTER TABLE | 60 |
| WitSqlEngine Transactions | 46 |
| WitSqlEngine CTE | 43 |
| WitSqlEngine JSON Functions | 42 |
| WitSqlEngine INFORMATION_SCHEMA | 42 |
| WitSqlEngine Window Functions | 24 |
| WitSqlEngine RETURNING | 20 |
| WitSqlEngine UPSERT | 19 |
| WitSqlEngine TRUNCATE/MERGE | 23 |
| WitSqlEngine Query Optimization | 16 (1 skipped) |
| WitSqlEngine Join Optimization | 10 |

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
| `Sources/Engine/OutWit.Database.Todo.md` | Engine TODO list |
| `Docs/CODE_STYLE_GUIDE.md` | Code style guide |

---

## Recent Changes

### 2025-02-04
- Engine: Query Optimization complete
  - Cost-based index selection for WHERE clause predicates
  - Query plan caching with LRU eviction and TTL
  - Join order optimization (greedy algorithm)
  - Implicit cross join optimization (`FROM a, b, c`)
  - Explicit JOIN optimization (swap sides for INNER/CROSS)
  - Semantic preservation for LEFT/RIGHT/FULL joins
  - 64 optimization tests passing

### 2025-02-03
- Engine: JSON Functions complete (12 functions)
  - JSON_EXTRACT, JSON_VALUE, JSON_QUERY
  - JSON_SET, JSON_INSERT, JSON_REPLACE, JSON_REMOVE
  - JSON_TYPE, JSON_ARRAY_LENGTH, JSON_VALID
  - JSON_ARRAY, JSON_OBJECT
  - 42 JSON function tests passing

### 2025-02-02
- Engine: DML Enhancements complete
  - INSERT/UPDATE/DELETE RETURNING clause
  - INSERT OR REPLACE / INSERT OR IGNORE
  - ON CONFLICT DO UPDATE (UPSERT) with EXCLUDED pseudo-table
  - TRUNCATE TABLE with auto-increment reset
  - MERGE statement with complex conditions
  - 62 DML enhancement tests passing
- Engine: Code refactoring - split StatementExecutor.Dml.cs into separate files

### 2025-02-01
- Engine: Window Functions implementation complete
  - ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST
  - LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTH_VALUE
  - Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER)
  - PARTITION BY and ORDER BY with NULLS FIRST/LAST
  - 24 tests passing
- Engine: CTE Execution implementation complete
  - Simple CTEs with explicit column names
  - Multiple CTEs and CTE referencing another CTE
  - Recursive CTEs with max depth protection
  - CTE caching for multiple references
  - 43 tests passing

### 2025-01-31
- Engine: ALTER TABLE implementation complete
  - `ADD/DROP CONSTRAINT` (CHECK, UNIQUE, FOREIGN KEY)
  - `ADD COLUMN with DEFAULT` (populates existing rows)
  - Computed columns **STORED** (auto-recalculate on UPDATE, auto-calculate on INSERT)
  - Computed columns **VIRTUAL** (evaluated on-the-fly in all iterators)
  - INDEX on STORED computed columns
  - Prevent direct INSERT/UPDATE into computed columns
  - VIRTUAL columns evaluation in IteratorIndexSeek and IteratorIndexRangeScan

### 2025-01-30

- Engine: Transaction support complete (BEGIN/COMMIT/ROLLBACK, Savepoints)
- Engine: FOR UPDATE/FOR SHARE locking complete
- Engine: Index implementation complete (seek, range, auto-update, partial, expression, covering)

### 2025-01-26

- Engine: Added full subquery support
  - Scalar subqueries
  - EXISTS / NOT EXISTS
  - IN (subquery) / NOT IN (subquery)
  - ANY / SOME / ALL
  - Correlated subqueries
- Engine: Fixed IteratorAlias for proper column name aliasing
