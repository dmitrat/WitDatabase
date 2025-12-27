# OutWit.Database (Engine) - TODO List v1

**Last Updated:** 2025-01-30  
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
| ALTER TABLE | 0 | 0 | 0 | ? DONE ? [Details](OutWit.Database.AlterTable.Todo.md) |
| CTE Execution | 2 | 1 | 0 | Required |
| Window Functions | 0 | 6 | 3 | Required |
| DML Enhancements | 0 | 8 | 0 | Required |
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

### Test Coverage: 67 tests (27 basic + 23 auto-update + 17 advanced)

---

## 3. ALTER TABLE (COMPLETED ?)

**Current State:** All ALTER TABLE features implemented including computed columns

> **?? See detailed implementation: [OutWit.Database.AlterTable.Todo.md](OutWit.Database.AlterTable.Todo.md)**

### Completed Tasks:
- [x] **P0** `ALTER TABLE ADD CONSTRAINT` - CHECK, UNIQUE, FOREIGN KEY constraints
- [x] **P0** `ALTER TABLE DROP CONSTRAINT` - Remove named constraints  
- [x] **P0** `ALTER TABLE ADD COLUMN` - populate existing rows with DEFAULT value
- [x] **P2** Computed columns support in ALTER TABLE (STORED and VIRTUAL)

### Implementation Summary:

#### ADD CONSTRAINT (Completed 2025-01-30)
- Created `DefinitionNamedConstraint.cs` with `ConstraintType` enum
- Added `NamedConstraints` property to `DefinitionTable`
- Implemented validation for CHECK (expression evaluation), UNIQUE (duplicate check), FOREIGN KEY (referential integrity)
- PRIMARY KEY throws `NotSupportedException` (would require table rebuild)
- UNIQUE constraint creates implicit index

#### DROP CONSTRAINT (Completed 2025-01-30)
- Removes constraint from metadata
- Drops associated index for UNIQUE constraints
- PRIMARY KEY cannot be dropped (throws `NotSupportedException`)

#### ADD COLUMN with DEFAULT (Completed 2025-01-30)
- Parses and evaluates DEFAULT expression
- Supports deterministic (evaluated once) and non-deterministic functions (NOW, NEWGUID - evaluated per row)
- Populates all existing rows with default value

#### Computed Columns (Completed 2025-01-30)
- STORED computed columns: expression evaluated for all existing rows, value persisted
- VIRTUAL computed columns: metadata stored, NULL placeholder for existing rows
- Supports functions (UPPER, LOWER, etc.), CASE expressions, NULL handling (COALESCE)

### Files Created:
- `Definitions/DefinitionNamedConstraint.cs`
- `Tests/WitSqlEngineAlterTableConstraintTests.cs`

### Files Modified:
- `Definitions/DefinitionTable.cs` - Added `NamedConstraints`, `GetConstraint()`
- `Interfaces/IDatabase.cs` - Added `AddConstraint()`, `DropConstraint()`, `AddComputedColumn()`
- `WitSqlEngine.Ddl.Tables.cs` - Constraint validation, computed columns
- `Schema/SchemaCatalog.Columns.cs` - Constraint persistence
- `Statements/StatementExecutor.Ddl.cs` - Handle constraint actions, computed columns

### Test Coverage: 44 tests (11 ADD CONSTRAINT + 5 DROP CONSTRAINT + 10 ADD COLUMN with DEFAULT + 16 Computed Columns + 2 integration)

---

## 4. CTE (WITH clause) Execution (P0/P1)

**Current State:** Parser supports CTE, Engine doesn't execute

### Tasks:
- [ ] **P0** Simple CTE execution (non-recursive) - needed for EF Core
- [ ] **P0** Multiple CTEs in single query
- [ ] **P1** `WITH RECURSIVE` - recursive CTE execution

---

## 5. Window Functions (P1 - Required for EF Core)

**Current State:** Parser supports, Engine doesn't execute

### Tasks:
- [ ] **P1** OVER clause handling infrastructure
- [ ] **P1** PARTITION BY grouping
- [ ] **P1** ORDER BY in window
- [ ] **P1** ROW_NUMBER() - critical for EF Core pagination
- [ ] **P1** RANK() / DENSE_RANK()
- [ ] **P1** LAG() / LEAD()
- [ ] **P2** Frame clause (ROWS/RANGE)
- [ ] **P2** NTILE()
- [ ] **P2** FIRST_VALUE / LAST_VALUE

---

## 6. DML Enhancements (P1)

### Missing Features:
- [ ] **P1** `INSERT ... RETURNING` - critical for EF Core identity
- [ ] **P1** `UPDATE ... RETURNING`
- [ ] **P1** `DELETE ... RETURNING`
- [ ] **P1** `INSERT OR REPLACE`
- [ ] **P1** `INSERT ... ON CONFLICT DO UPDATE` (UPSERT)
- [ ] **P1** `INSERT ... ON CONFLICT DO NOTHING`
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

## Implementation Timeline

### Phase 2: Engine Completion (Current)

| Week | Tasks |
|------|-------|
| **Week 1-2** | ~~Transaction fix~~, ~~FOR UPDATE/SHARE~~, ~~Index implementation~~ ? |
| **Week 3** | ~~ALTER TABLE ADD COLUMN with DEFAULT~~ ? |
| **Week 4** | ~~ALTER TABLE DROP/ADD CONSTRAINT~~ ? |
| **Week 5-6** | CTE execution, RETURNING clause |
| **Week 7-8** | Window functions (ROW_NUMBER, RANK) |
| **Week 9-10** | INFORMATION_SCHEMA, JSON functions |

### Phase 3: ADO.NET (After Engine)

| Week | Tasks |
|------|-------|
| **Week 11-12** | WitDbConnection, WitDbCommand |
| **Week 13-14** | WitDbDataReader, Parameters |
| **Week 15-16** | Transaction, Factory, Tests |

### Phase 4: EF Core (After ADO.NET)

| Week | Tasks |
|------|-------|
| **Week 17-18** | Basic provider registration |
| **Week 19-20** | Query translation |
| **Week 21-22** | Migrations, Scaffolding |

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
| WitSqlEngineAlterTableConstraint* | 44 | 0 | Computed columns complete |
| **Total** | **1140+** | **0** | 100% passing |

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
| `IteratorIndexSeek.cs` | Created |
| `IteratorIndexRangeScan.cs` | Created |
| `WitSqlEngine.Query.cs` | Modified |
| `WitSqlEngine.Dml.cs` | Modified |
| `WitSqlEngine.Ddl.Indexes.cs` | Modified |
| `WitSqlValue.Getters.cs` | Modified |

### ALTER TABLE (Complete)
| File | Status |
|------|--------|
| `DefinitionNamedConstraint.cs` | Created |
| `DefinitionTable.cs` | Modified |
| `IDatabase.cs` | Modified |
| `WitSqlEngine.Ddl.Tables.cs` | Modified |
| `Schema/SchemaCatalog.Columns.cs` | Modified |
| `StatementExecutor.Ddl.cs` | Modified |
| `WitSqlEngineAlterTableConstraintTests.cs` | Created |

---

## Dependencies

```
+-------------------------------------------------------------+
|                     SQL ENGINE (Phase 2)                     |
+-------------------------------------------------------------+
|  Transaction Fix ? --> FOR UPDATE/SHARE ? --> Index ?        |
|        |                                                     |
|        +--> ALTER TABLE (ADD/DROP CONSTRAINT) ?              |
|        |                                                     |
|        +--> CTE Execution  ? NEXT                            |
|        |                                                     |
|        +--> Window Functions (ROW_NUMBER for pagination)     |
|        |                                                     |
|        +--> RETURNING clause (for identity values)           |
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
```

---

## Next Steps (Immediate)

1. ~~**Transaction Fix** - fix lock recursion issue~~ ?
2. ~~**FOR UPDATE/SHARE** - implement locking hints~~ ?
3. ~~**Index Implementation** - seek, range scan, auto-update~~ ?
4. ~~**ALTER TABLE ADD COLUMN with DEFAULT**~~ ?
5. ~~**ALTER TABLE DROP CONSTRAINT**~~ ?
6. ~~**ALTER TABLE ADD CONSTRAINT**~~ ?
7. **CTE Execution** ? NEXT - implement simple (non-recursive) CTE
8. **RETURNING clause** - INSERT/UPDATE/DELETE ... RETURNING
9. **Window Functions** - ROW_NUMBER(), RANK(), etc.

---

## Related Documents

- [Roadmap.Engine.md](../../Roadmap.Engine.md) - Overall engine roadmap
- [OutWit.Database.AlterTable.Todo.md](OutWit.Database.AlterTable.Todo.md) - ALTER TABLE implementation (COMPLETED)

---

**Last Updated:** 2025-01-30

