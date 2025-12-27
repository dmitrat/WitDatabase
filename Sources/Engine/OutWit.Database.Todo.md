# OutWit.Database (Engine) - TODO List v1

**Last Updated:** 2025-12-27  
**Based on:** Code audit + Roadmap.Engine.md

---

## Goals

**Project Goal:** Create a database with full ADO.NET and EF Core provider support.

**Implementation Order:**
1. **Phase 1:** SQL Parser (COMPLETED - 100%)
2. **Phase 2:** SQL Engine (current phase)
3. **Phase 3:** ADO.NET Provider (after Engine completion)
4. **Phase 4:** EF Core Provider (after ADO.NET)

---

## Legend

| Symbol | Status |
|--------|--------|
| [ ] | Not started |
| [~] | In progress / Partial |
| [x] | Complete |

**Priority:**
- **P0** = Critical - blocks other tasks
- **P1** = Required for v1 - needed for ADO.NET/EF Core
- **P2** = Nice-to-have - can be deferred

---

## Summary

| Category | P0 | P1 | P2 | Status |
|----------|----|----|----|----|
| Transaction Support | 0 | 0 | 0 | ? DONE |
| Index Implementation | 0 | 0 | 0 | ? DONE |
| ALTER TABLE | 0 | 0 | 0 | ? DONE |
| CTE Execution | 0 | 0 | 0 | ? DONE |
| Window Functions | 0 | 0 | 1 | ? DONE (frame clause P2) |
| DML Enhancements | 0 | 0 | 0 | ? DONE (RETURNING + UPSERT) |
| JSON Functions | 0 | 3 | 3 | Required |
| Query Optimization | 0 | 2 | 2 | Optional |
| INFORMATION_SCHEMA | 0 | 6 | 0 | Required |
| Misc/Cleanup | 0 | 0 | 3 | Polish |
| **ADO.NET Provider** | 0 | 9 | 0 | After Engine |
| **EF Core Provider** | 0 | 10+ | 0 | After ADO.NET |

---

# PHASE 2: SQL Engine (Current)

## 1. Transaction Support (COMPLETED ?)

**Current State:** Transaction support fully implemented including FOR UPDATE/FOR SHARE

### Implementation Summary:
- Fixed lock recursion issue by adding `Scan()` method to `ITransaction`
- Transaction-aware `IteratorTableScan` uses transaction's `Scan()` when active
- Schema operations (row ID management) now respect active transactions
- SQL statement execution for `BEGIN`, `COMMIT`, `ROLLBACK`, `SAVEPOINT`
- FOR UPDATE / FOR SHARE locking hints via `IteratorLocking`

### Completed Tasks:
- [x] **P0** Fix lock recursion issue in transactions
- [x] **P0** Transaction isolation for queries (uses transaction's Scan method)
- [x] **P0** `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` SQL execution
- [x] **P0** Isolation level support (READ COMMITTED, SERIALIZABLE, etc.)
- [x] **P1** `SAVEPOINT` / `RELEASE SAVEPOINT` / `ROLLBACK TO SAVEPOINT`
- [x] **P1** `FOR UPDATE` / `FOR SHARE` locking hints

### Test Coverage: 46 tests passing

---

## 2. Index Implementation (COMPLETED ?)

**Current State:** Index implementation complete - metadata, iterators, auto-update, building from existing data, partial indexes, expression indexes, and covering indexes

### Completed Tasks:
- [x] **P0** Implement `IteratorIndexSeek.cs` - equality lookup using secondary index
- [x] **P0** Implement `IteratorIndexRangeScan.cs` - range queries using index
- [x] **P0** Index key serialization (sort-order preserving)
- [x] **P0** Index auto-update on INSERT/UPDATE/DELETE
- [x] **P0** Index building from existing data (CREATE INDEX on non-empty table)
- [x] **P1** Partial index evaluation (WHERE clause on index)
- [x] **P1** Expression index evaluation (functional indexes)
- [x] **P1** Covering index support (INCLUDE columns)
- [x] **P1** VIRTUAL computed columns evaluation in index iterators

### Test Coverage: 67 tests (27 basic + 23 auto-update + 17 advanced)

---

## 3. ALTER TABLE (COMPLETED ?)

**Current State:** All ALTER TABLE features implemented including computed columns

### Completed Tasks:
- [x] **P0** `ALTER TABLE ADD CONSTRAINT` - CHECK, UNIQUE, FOREIGN KEY constraints
- [x] **P0** `ALTER TABLE DROP CONSTRAINT` - Remove named constraints  
- [x] **P0** `ALTER TABLE ADD COLUMN` - populate existing rows with DEFAULT value
- [x] **P2** Computed columns support (STORED and VIRTUAL)

### Implementation Summary:

#### ADD CONSTRAINT (Completed 2025-01-30)
- Created `DefinitionNamedConstraint.cs` with `ConstraintType` enum
- Added `NamedConstraints` property to `DefinitionTable`
- Validation: CHECK (expression), UNIQUE (duplicates), FOREIGN KEY (referential integrity)
- PRIMARY KEY throws `NotSupportedException` (requires table rebuild)
- UNIQUE constraint creates implicit index

#### DROP CONSTRAINT (Completed 2025-01-30)
- Removes constraint from metadata
- Drops associated index for UNIQUE constraints
- PRIMARY KEY cannot be dropped

#### ADD COLUMN with DEFAULT (Completed 2025-01-30)
- Parses and evaluates DEFAULT expression
- Supports deterministic and non-deterministic functions (NOW, NEWGUID)
- Populates all existing rows with default value

#### Computed Columns (Completed 2025-01-30)
- **STORED**: Expression evaluated for all rows, value persisted, auto-recalculated on UPDATE
- **VIRTUAL**: Evaluated on-the-fly during SELECT (in all iterators)
- Supports functions (UPPER, LOWER, etc.), CASE expressions, COALESCE
- Prevents direct INSERT/UPDATE into computed columns
- INDEX on STORED computed columns supported

### Test Coverage: 60 tests (constraints + computed columns + integration + persistence)

---

## 4. CTE (WITH clause) Execution (COMPLETED ?)

**Current State:** Full CTE support including recursive CTEs

### Completed Tasks:
- [x] **P0** Simple CTE execution (non-recursive) - needed for EF Core
- [x] **P0** Multiple CTEs in single query
- [x] **P1** `WITH RECURSIVE` - recursive CTE execution

### Implementation Summary:
- **Non-recursive CTEs**: Plan the CTE query and reference it as a virtual table
- **Recursive CTEs**: Execute anchor member, then iteratively execute recursive member until no new rows
- **Column renaming**: Support explicit column names in CTE definition `WITH cte_name (col1, col2) AS (...)`
- **Max recursion depth**: 1000 iterations to prevent infinite loops
- **CTE Caching**: Results cached for multiple references in same query
- **Cache cleanup**: Cleared between queries in `StatementExecutor.Select.cs`

### Test Coverage: 43 tests passing
- Simple CTE queries (select, filter, aggregation)
- CTE with explicit column names
- Multiple CTEs and CTE referencing another CTE
- ORDER BY, LIMIT, GROUP BY, DISTINCT on CTE
- Recursive CTE for hierarchies
- Recursive CTE for sequences (numbers, Fibonacci)
- CTE caching (multiple references, self-join, union)
- Error handling (max depth, requires UNION ALL, duplicate names)
- Integration tests (nested subquery, correlated, LEFT JOIN, EXISTS)

### Key Files:
- `QueryPlanner.cs` - CTE registration and iterator creation
- `IteratorColumnRename.cs` - Column renaming for explicit CTE column names
- `IteratorInMemory.cs` - In-memory row storage for recursive CTE working tables
- `StatementExecutor.Select.cs` - CTE cleanup after query execution

---

## 5. Window Functions (COMPLETED ?)

**Current State:** Full window function support implemented

### Completed Tasks:
- [x] **P1** OVER clause handling infrastructure
- [x] **P1** PARTITION BY grouping
- [x] **P1** ORDER BY in window (with ASC/DESC, NULLS FIRST/LAST)
- [x] **P1** ROW_NUMBER() - critical for EF Core pagination
- [x] **P1** RANK() / DENSE_RANK()
- [x] **P1** LAG() / LEAD() with offset and default value
- [x] **P1** NTILE(n)
- [x] **P1** FIRST_VALUE / LAST_VALUE / NTH_VALUE
- [x] **P1** PERCENT_RANK() / CUME_DIST()
- [x] **P1** Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER)
- [ ] **P2** Frame clause (ROWS/RANGE BETWEEN) - deferred to v2

### Implementation Summary:
- **IteratorWindow.cs** - blocking operator that reads all rows, partitions, sorts, evaluates
- **Ranking functions**: ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST
- **Value functions**: FIRST_VALUE, LAST_VALUE, NTH_VALUE, LAG, LEAD
- **Aggregate functions**: SUM, AVG, COUNT, MIN, MAX with OVER clause
- **Partitioning**: GroupByPartition with composite key support
- **Sorting**: WindowOrderComparer with NULLS FIRST/LAST support

### Test Coverage: 24 tests passing
- ROW_NUMBER with ORDER BY and PARTITION BY
- RANK and DENSE_RANK with ties
- NTILE bucket distribution
- LAG/LEAD with offset and default values
- FIRST_VALUE/LAST_VALUE/NTH_VALUE
- Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER)
- Multiple window functions in same query
- Edge cases (empty table, single row, NULL values)
- PERCENT_RANK and CUME_DIST
- Window functions in subqueries

### Key Files:
- `IteratorWindow.cs` - Window function evaluation iterator
- `QueryPlanner.cs` - Window function detection and iterator creation

---

## 6. DML Enhancements (P1)

**Current State:** UPSERT and RETURNING clause implemented

### Completed Tasks:
- [x] **P1** `INSERT ... RETURNING` - critical for EF Core identity
- [x] **P1** `UPDATE ... RETURNING`
- [x] **P1** `DELETE ... RETURNING`
- [x] **P1** `INSERT OR REPLACE`
- [x] **P1** `INSERT ... ON CONFLICT DO UPDATE` (UPSERT)
- [x] **P1** `INSERT ... ON CONFLICT DO NOTHING`

### Implementation Summary:
- **WitSqlResult** - New constructor for DML with RETURNING (returns both RowsAffected and rows)
- **BuildReturningRow** - Builds row for RETURNING clause from source row and select list
- **BuildReturningSchema** - Builds column schema for RETURNING clause
- **RETURNING *** - Returns all columns from the table
- **RETURNING with expressions** - Supports computed expressions and aliases
- **EXCLUDED pseudo-table** - Supports EXCLUDED.column references in ON CONFLICT DO UPDATE
- **Conflict detection** - Checks primary key, unique columns, and unique indexes
- **WHERE clause in ON CONFLICT** - Supports conditional updates with EXCLUDED references

### Test Coverage: 39 tests passing
- INSERT RETURNING (Id, *, multiple columns, alias, multiple rows, defaults)
- UPDATE RETURNING (single row, all columns, multiple rows, no match)
- DELETE RETURNING (single row, all columns, multiple rows)
- RETURNING with expressions
- Schema type verification
- INSERT OR REPLACE (new row, existing row, multiple rows, with unique index)
- INSERT OR IGNORE (new row, existing row, multiple rows)
- ON CONFLICT DO NOTHING (new row, existing row, with unique constraint)
- ON CONFLICT DO UPDATE (new row, existing row, with EXCLUDED, increment, with WHERE)
- UPSERT with RETURNING (inserted and updated)

### Key Files:
- `StatementExecutor.Dml.cs` - Modified - RETURNING clause support in INSERT/UPDATE/DELETE, UPSERT support
- `WitSqlResult.cs` - Modified - New constructor for DML with RETURNING
- `ContextExecution.cs` - Modified - Added ExcludedRow property for EXCLUDED pseudo-table
- `ExpressionEvaluator.Core.cs` - Modified - EXCLUDED.column reference evaluation
- `WitSqlEngineReturningTests.cs` - Created - 20 RETURNING tests
- `WitSqlEngineUpsertTests.cs` - Created - 19 UPSERT tests

### Missing Features:
- [ ] **P1** `TRUNCATE TABLE`
- [ ] **P1** `MERGE` statement

---

## 7. JSON Functions (P1)

**Current State:** Partial implementation

### Implemented:
- [x] `JSON_EXTRACT(json, path)`
- [x] `JSON_TYPE(json)`
- [x] `JSON_ARRAY_LENGTH(json)`

### Missing (needed for EF Core JSON mapping):
- [ ] **P1** `JSON_VALUE(json, path)` - extract scalar as SQL type
- [ ] **P1** `JSON_QUERY(json, path)` - extract object/array
- [ ] **P1** `JSON_SET(json, path, value)` - modify JSON
- [ ] **P2** `JSON_INSERT` / `JSON_REPLACE` / `JSON_REMOVE`
- [ ] **P2** `JSON_ARRAY()` / `JSON_OBJECT()` - constructors
- [ ] **P2** `JSON_VALID(str)` - validation

---

## 8. INFORMATION_SCHEMA (P1 - Required for EF Core)

**Current State:** Not implemented

EF Core scaffolding requires these views for reverse engineering:

### Tasks:
- [ ] **P1** `INFORMATION_SCHEMA.TABLES`
- [ ] **P1** `INFORMATION_SCHEMA.COLUMNS`
- [ ] **P1** `INFORMATION_SCHEMA.KEY_COLUMN_USAGE`
- [ ] **P1** `INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS`
- [ ] **P1** `INFORMATION_SCHEMA.INDEXES`
- [ ] **P1** `INFORMATION_SCHEMA.VIEWS`

---

## 9. Query Optimization (P1/P2)

**Current State:** No optimization, always full table scan

### Tasks:
- [ ] **P1** Index selection in WHERE clause (cost-based)
- [ ] **P1** Basic predicate pushdown
- [ ] **P2** Join ordering optimization
- [ ] **P2** Query plan caching

---

## 10. Miscellaneous / Cleanup

### Code Cleanup:
- [x] **P1** Enable ignored transaction tests after fix
- [x] **P1** Enable ALTER TABLE DEFAULT test after fix
- [ ] **P2** ROWVERSION auto-increment support
- [ ] **P2** Cascading deletes (FK ON DELETE CASCADE)
- [ ] **P2** Query timeout cancellation

---

# PHASE 3: ADO.NET Provider (After Engine)

> Start only after completing all P0/P1 Engine tasks

## ADO.NET Classes to Implement

| Class | Purpose | Priority |
|-------|---------|----------|
| `WitDbConnection` | Connection management | P1 |
| `WitDbCommand` | Command execution | P1 |
| `WitDbDataReader` | Forward-only result reader | P1 |
| `WitDbParameter` | Named/positional parameters | P1 |
| `WitDbParameterCollection` | Parameter collection | P1 |
| `WitDbTransaction` | Transaction wrapper | P1 |
| `WitDbConnectionStringBuilder` | Connection string parsing | P1 |
| `WitDbProviderFactory` | Factory for DI | P1 |
| `WitDbException` | Provider-specific exception | P1 |

### ADO.NET Features:
- [ ] Async methods (`ExecuteReaderAsync`, etc.)
- [ ] Connection pooling (optional for embedded)
- [ ] Multiple result sets
- [ ] Batched commands

---

# PHASE 4: EF Core Provider (After ADO.NET)

> Start only after completing ADO.NET Provider

## EF Core Classes to Implement

| Class | Purpose |
|-------|---------|
| `WitDbContextOptionsExtensions` | DbContext configuration |
| `WitDbDatabaseProvider` | Database provider registration |
| `WitDbTypeMappingSource` | CLR to DB type mapping |
| `WitDbSqlGenerationHelper` | SQL generation helpers |
| `WitDbQuerySqlGenerator` | Query translation |
| `WitDbModificationCommandBatch` | Batch INSERT/UPDATE/DELETE |
| `WitDbMigrationsSqlGenerator` | Migration SQL generation |
| `WitDbDatabaseCreator` | Database creation/deletion |
| `WitDbRelationalConnection` | Relational connection |
| `WitDbModelValidator` | Model validation |

### EF Core Features:
- [ ] LINQ to SQL translation
- [ ] Change tracking integration
- [ ] Migrations support
- [ ] Scaffolding (reverse engineering)
- [ ] Compiled queries

---

## Test Status

| Test File | Passing | Ignored | Notes |
|-----------|---------|---------|-------|
| ExpressionEvaluator* | 194 | 0 | OK |
| StatementExecutor* | 162 | 0 | OK |
| Iterators/* | 119 | 0 | OK |
| QueryPlanner* | 50 | 0 | OK |
| WitSqlValue* | 148 | 0 | OK |
| WitSqlEngineIndex* | 67 | 0 | OK |
| WitSqlEngine* | 132 | 0 | OK |
| WitSqlEngineAlterTable* | 60 | 0 | Constraints + Computed + Integration |
| WitSqlEngineCte* | 43 | 0 | CTE + Recursive + Caching |
| WitSqlEngineWindowFunction* | 24 | 0 | All window functions |
| WitSqlEngineReturning* | 20 | 0 | INSERT/UPDATE/DELETE RETURNING |
| **Total** | **1227+** | **0** | 100% passing |

---

## Files Created/Modified

### Transaction Support (Complete)
| File | Status |
|------|--------|
| `ITransaction.cs` | Modified - Added `Scan()` |
| `Transaction.cs` | Modified |
| `MvccTransaction.cs` | Modified |
| `IteratorLocking.cs` | Created |
| `IteratorTableScan.cs` | Modified |

### Index Implementation (Complete)
| File | Status |
|------|--------|
| `IteratorIndexSeek.cs` | Created - with VIRTUAL column evaluation |
| `IteratorIndexRangeScan.cs` | Created - with VIRTUAL column evaluation |
| `WitSqlEngine.Query.cs` | Modified |
| `WitSqlEngine.Dml.cs` | Modified |
| `WitSqlEngine.Ddl.Indexes.cs` | Modified - skip rebuild if index has data |
| `WitSqlValue.Getters.cs` | Modified |

### ALTER TABLE (Complete)
| File | Status |
|------|--------|
| `DefinitionNamedConstraint.cs` | Created |
| `DefinitionTable.cs` | Modified - `NamedConstraints`, `GetConstraint()` |
| `IDatabase.cs` | Modified - `AddConstraint()`, `DropConstraint()`, `AddComputedColumn()` |
| `WitSqlEngine.Ddl.Tables.cs` | Modified - constraint validation, computed columns |
| `Schema/SchemaCatalog.Columns.cs` | Modified - constraint persistence |
| `StatementExecutor.Ddl.cs` | Modified - constraint actions, computed columns |
| `StatementExecutor.Dml.cs` | Modified - computed column handling in INSERT/UPDATE |
| `WitSqlEngineAlterTableConstraintTests.cs` | Created |
| `WitSqlEngineAlterTableIntegrationTests.cs` | Created |

### CTE (WITH clause) Execution (Complete)
| File | Status |
|------|--------|
| `QueryPlanner.cs` | Modified - CTE registration and iterator creation |
| `IteratorColumnRename.cs` | Created - Column renaming for explicit CTE column names |
| `IteratorInMemory.cs` | Created - In-memory row storage for recursive CTE working tables |
| `StatementExecutor.Select.cs` | Modified - CTE cleanup after query execution |
| `ContextExecution.cs` | Modified - CTE definitions and cache dictionaries |
| `WitSqlEngineCteTests.cs` | Created - 43 CTE tests |

### Window Functions (Complete)
| File | Status |
|------|--------|
| `IteratorWindow.cs` | Created - Window function evaluation |
| `QueryPlanner.cs` | Modified - Window function detection |
| `WitSqlEngineWindowFunctionTests.cs` | Created - 24 window function tests |

### RETURNING Clause (Complete)
| File | Status |
|------|--------|
| `StatementExecutor.Dml.cs` | Modified - RETURNING clause support |
| `WitSqlResult.cs` | Modified - New constructor for DML with RETURNING |
| `WitSqlEngineReturningTests.cs` | Created - 20 RETURNING tests |

### UPSERT (Complete)
| File | Status |
|------|--------|
| `StatementExecutor.Dml.cs` | Modified - UPSERT support |
| `WitSqlEngineUpsertTests.cs` | Created - 19 UPSERT tests |


## Dependencies

```
+-------------------------------------------------------------+
|                     SQL ENGINE (Phase 2)                     |
+-------------------------------------------------------------+
|  Transaction Fix ? --> FOR UPDATE/SHARE ? --> Index ?     |
|        |                                                     |
|        +--> ALTER TABLE (ADD/DROP CONSTRAINT) ?             |
|        |                                                     |
|        +--> CTE Execution ?                                 |
|        |                                                     |
|        +--> Window Functions ?                              |
|        |                                                     |
|        +--> RETURNING clause ?                              |
|        |                                                     |
|        +--> INFORMATION_SCHEMA (for scaffolding)             |
+-------------------------------------------------------------+
                            |
                            v
+-------------------------------------------------------------+
|                   ADO.NET PROVIDER (Phase 3)                 |
+-------------------------------------------------------------+
|  WitDbConnection --> WitDbCommand --> WitDbDataReader        |
|        |                                                     |
|        +--> WitDbTransaction --> WitDbProviderFactory        |
+-------------------------------------------------------------+
                            |
                            v
+-------------------------------------------------------------+
|                   EF CORE PROVIDER (Phase 4)                 |
+-------------------------------------------------------------+
|  TypeMapping --> QueryTranslation --> Migrations             |
+-------------------------------------------------------------+


