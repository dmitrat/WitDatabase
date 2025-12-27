# OutWit.Database (Engine) - TODO List v1

**Last Updated:** 2025-01-28  
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
| Transaction Support | 0 | 0 | 0 | DONE |
| Index Implementation | 1 | 3 | 0 | IN PROGRESS |
| ALTER TABLE | 3 | 0 | 1 | MISSING |
| CTE Execution | 2 | 1 | 0 | Required |
| Window Functions | 0 | 6 | 3 | Required |
| DML Enhancements | 0 | 8 | 0 | Required |
| JSON Functions | 0 | 3 | 3 | Required |
| Query Optimization | 0 | 2 | 2 | Optional |
| INFORMATION_SCHEMA | 0 | 6 | 0 | Required |
| Misc/Cleanup | 0 | 1 | 3 | Polish |
| **ADO.NET Provider** | 0 | 9 | 0 | After Engine |
| **EF Core Provider** | 0 | 10+ | 0 | After ADO.NET |

---

# PHASE 2: SQL Engine (Current)

## 1. Transaction Support (COMPLETED)

**Current State:** Transaction support fully implemented including FOR UPDATE/FOR SHARE

### Implementation Summary:
- Fixed lock recursion issue by adding `Scan()` method to `ITransaction`
- Transaction-aware `IteratorTableScan` uses transaction's `Scan()` when active
- Schema operations (row ID management) now respect active transactions
- SQL statement execution for `BEGIN`, `COMMIT`, `ROLLBACK`, `SAVEPOINT`
- **NEW:** FOR UPDATE / FOR SHARE locking hints via `IteratorLocking`

### Completed Tasks:
- [x] **P0** Fix lock recursion issue in transactions
- [x] **P0** Transaction isolation for queries (uses transaction's Scan method)
- [x] **P0** `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` SQL execution
- [x] **P0** Isolation level support (READ COMMITTED, SERIALIZABLE, etc.)
- [x] **P1** `SAVEPOINT` / `RELEASE SAVEPOINT` / `ROLLBACK TO SAVEPOINT`
- [x] **P1** `FOR UPDATE` / `FOR SHARE` locking hints

### FOR UPDATE/SHARE Implementation Details:
- `IteratorLocking.cs` - applies row-level locks during iteration
- `QueryPlanner.cs` - integrates locking iterator into query plan
- Supports all wait modes: `WAIT` (default), `NOWAIT`, `SKIP LOCKED`
- Requires MVCC transaction (throws if regular transaction or no transaction)
- Leverages Core's `IMvccTransaction.GetForUpdate()`/`GetForShare()` methods

### Test Coverage:
All 46 transaction and locking tests passing:
- `BeginTransactionReturnsHandleTest`
- `CommitPersistsChangesTest`
- `CommitWithoutTransactionDoesNotThrowTest`
- `RollbackDiscardsChangesTest`
- `RollbackWithoutTransactionDoesNotThrowTest`
- `DisposeWithoutCommitAutoRollbacksTest`
- `ChangesVisibleWithinTransactionTest`
- `MultipleInsertsWithinTransactionVisibleTest`
- `UpdateWithinTransactionVisibleTest`
- `DeleteWithinTransactionVisibleTest`
- `BeginTransactionSqlStartsTransactionTest`
- `RollbackSqlDiscardsChangesTest`
- `SavepointRollbackPartialChangesTest`
- `SavepointSqlWorksTest`
- `BeginTransactionWhileActiveThrowsTest`
- `SavepointWithoutTransactionThrowsTest`
- `RollbackToNonExistentSavepointThrowsTest`
- `SelectForUpdateWithoutTransactionThrowsTest`
- `SelectForShareWithoutTransactionThrowsTest`
- `SelectForUpdateWithNonMvccTransactionThrowsTest`
- `SelectWithForUpdateClauseIsParsedTest`
- `SelectWithForShareClauseIsParsedTest`
- `SelectWithForUpdateNoWaitIsParsedTest`
- `SelectWithForUpdateSkipLockedIsParsedTest`
- `SelectWithForShareNoWaitIsParsedTest`
- `SelectForUpdateWithoutFromThrowsTest`
- `SelectWithoutForClauseDoesNotRequireTransactionTest`
- `SelectForUpdateWithWhereClauseIsParsedTest`
- `SelectForShareWithJoinIsParsedTest`
- `SelectForUpdateWithGroupByIsParsedTest`
- `LockingTypeNoneDoesNotRequireTransactionTest`
- `LockingTypeMappingTest(ForUpdate)`
- `LockingTypeMappingTest(ForShare)`
- `SelectForUpdateWithSubqueryInFromReturnsNullTableNameTest`
- Plus 29 StatementExecutorTransactionTests

---

## 2. Index Implementation (P0 - IN PROGRESS)

**Current State:** Index metadata stored, iterators created, but not yet integrated into query planner

### Found in Code:
```csharp
// WitSqlEngine.Query.cs - NOW IMPLEMENTED
public IResultIterator CreateIndexSeek(string tableName, string indexName, WitSqlValue[] keyValues)
public IResultIterator CreateIndexRangeScan(string tableName, string indexName, ...)
```

### Completed Tasks:
- [x] **P0** Implement `IteratorIndexSeek.cs` - equality lookup using secondary index
- [x] **P0** Implement `IteratorIndexRangeScan.cs` - range queries using index
- [x] **P0** Implement `CreateIndexSeek()` in WitSqlEngine
- [x] **P0** Implement `CreateIndexRangeScan()` in WitSqlEngine
- [x] **P0** Index key serialization (sort-order preserving)
- [x] **P0** Index auto-update on INSERT/UPDATE/DELETE

### Remaining Tasks:
- [ ] **P1** Partial index evaluation (WHERE clause on index)
- [ ] **P1** Expression index evaluation (functional indexes)
- [ ] **P1** Covering index support (INCLUDE columns)

### Implementation Files Created:
- `Iterators/IteratorIndexSeek.cs` - Index equality lookup iterator
- `Iterators/IteratorIndexRangeScan.cs` - Index range scan iterator

### Updated Files:
- `WitSqlEngine.Query.cs` - Added `CreateIndexSeek()`, `CreateIndexRangeScan()`, `SerializeIndexKey()`
- `WitSqlEngine.Dml.cs` - Added index auto-update on INSERT/UPDATE/DELETE
- `WitSqlEngine.Ddl.Indexes.cs` - Added physical secondary index creation/drop
- `Values/WitSqlValue.Getters.cs` - Added `AsLong()`, `AsULong()`, `AsUInt64()` methods
- `Types/WitTypeConverter.cs` - Fixed string serialization for lexicographic ordering

### Test Coverage:
All 23 index auto-update tests passing:
- INSERT: Updates secondary indexes, composite indexes, multiple indexes, unique index violation
- UPDATE: Updates indexed columns, removes null from index, adds non-null to index
- DELETE: Removes entries from all indexes
- Range scan: Reflects inserts, updates, and deletes correctly

---

## 3. ALTER TABLE (P0 - MISSING)

**Current State:** Partial implementation - several ALTER actions not executed

### Missing in `StatementExecutor.Ddl.cs`:
```csharp
// ExecuteAlterTable doesn't handle:
case AlterActionAddConstraint addConstraint:  // NOT IMPLEMENTED
case AlterActionDropConstraint dropConstraint: // NOT IMPLEMENTED
```

### Found in Tests (Ignored):
```csharp
// WitSqlEngineDdlTests.cs:99
[Ignore("ALTER TABLE ADD COLUMN with DEFAULT does not yet populate existing rows")]
public void AlterTableAddColumnWithDefaultPopulatesExistingRowsTest()
```

### Tasks:
- [ ] **P0** `ALTER TABLE ADD CONSTRAINT` - needed for EF Core migrations
- [ ] **P0** `ALTER TABLE DROP CONSTRAINT` - needed for EF Core migrations  
- [ ] **P0** `ALTER TABLE ADD COLUMN` - populate existing rows with DEFAULT value
- [ ] **P2** Computed columns support in ALTER TABLE

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
- [ ] **P1** Enable ALTER TABLE DEFAULT test after fix
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
| **Week 1-2** | ~~Transaction fix~~, ~~FOR UPDATE/SHARE~~, ~~Index seek/range~~, Index auto-update, ALTER TABLE fix |
| **Week 3-4** | CTE execution, RETURNING clause |
| **Week 5-6** | Window functions (ROW_NUMBER, RANK) |
| **Week 7-8** | INFORMATION_SCHEMA, JSON functions |

### Phase 3: ADO.NET (After Engine)

| Week | Tasks |
|------|-------|
| **Week 9-10** | WitDbConnection, WitDbCommand |
| **Week 11-12** | WitDbDataReader, Parameters |
| **Week 13-14** | Transaction, Factory, Tests |

### Phase 4: EF Core (After ADO.NET)

| Week | Tasks |
|------|-------|
| **Week 15-16** | Basic provider registration |
| **Week 17-18** | Query translation |
| **Week 19-20** | Migrations, Scaffolding |

---

## Test Status

| Test File | Passing | Ignored | Notes |
|-----------|---------|---------|-------|
| ExpressionEvaluator* | 194 | 0 | OK |
| StatementExecutor* | 162 | 1 | ALTER TABLE DEFAULT |
| Iterators/* | 119 | 0 | OK |
| QueryPlanner* | 50 | 0 | OK |
| WitSqlValue* | 130 | 0 | OK |
| WitSqlEngine* | 132 | 1 | All TX tests enabled, 1 ALTER |
| **Total** | **950+** | **2** | 99.8% passing |

---

## Files Created (Index Implementation)

| File | Purpose |
|------|---------|
| `Iterators/IteratorIndexSeek.cs` | Index equality lookup iterator |
| `Iterators/IteratorIndexRangeScan.cs` | Index range scan iterator |

## Files Modified (Index Implementation)

| File | Changes Made |
|------|-------------|
| `WitSqlEngine.Query.cs` | Added `CreateIndexSeek()`, `CreateIndexRangeScan()`, `SerializeIndexKey()` |
| `WitSqlEngine.Dml.cs` | Added index auto-update on INSERT/UPDATE/DELETE |
| `WitSqlEngine.Ddl.Indexes.cs` | Added physical secondary index creation/drop |
| `Values/WitSqlValue.Getters.cs` | Added `AsLong()`, `AsULong()`, `AsUInt64()` methods |
| `Types/WitTypeConverter.cs` | Fixed string serialization for lexicographic ordering |

---

## Files to Create (Remaining Engine Work)

| File | Purpose |
|------|---------|
| `Iterators/IteratorWindow.cs` | Window function iterator |
| `Iterators/IteratorCte.cs` | CTE materialization iterator |
| `Schema/InformationSchema.cs` | INFORMATION_SCHEMA views |

---

## Files Modified (Transaction Support) - Previously

| File | Changes Made |
|------|-------------|
| `ITransaction.cs` | Added `Scan()` and `ScanAsync()` methods |
| `Transaction.cs` | Implemented `Scan()` with transaction-aware merging |
| `MvccTransaction.cs` | Implemented `Scan()` with MVCC support |
| `TransactionalStore.cs` | Added `ScanFromStore()` internal methods |
| `IDatabase.cs` | Added `CurrentTransaction`, `CreateSavepoint()`, `ReleaseSavepoint()`, `RollbackToSavepoint()`, `BeginTransaction(IsolationLevel)` |
| `WitSqlEngine.Transactions.cs` | Implemented isolation level support and savepoint methods |
| `WitSqlEngine.Query.cs` | Updated `CreateTableScan()` to pass transaction |
| `IteratorTableScan.cs` | Added transaction-aware scanning |
| `StatementExecutor.cs` | Added transaction SQL statement execution |
| `SchemaCatalog.cs` | Added transaction-aware row ID methods |
| `WitSqlEngine.Ddl.Tables.cs` | Updated `GetNextAutoIncrement()` to use transaction |

## Files Modified (FOR UPDATE/FOR SHARE Support) - Previously

| File | Changes Made |
|------|-------------|
| `IteratorLocking.cs` | **NEW** - Row-level locking iterator |
| `QueryPlanner.cs` | Added `ApplyLockingClause()` method and helper methods |
| `StatementExecutorLockingTests.cs` | **NEW** - 17 tests for locking functionality |

---

## Dependencies

```
+-------------------------------------------------------------+
|                     SQL ENGINE (Phase 2)                     |
+-------------------------------------------------------------+
|  Transaction Fix ? --> FOR UPDATE/SHARE ? --> Index Seek ?   |
|        |                                                     |
|        +--> Index Auto-Update (pending)                      |
|        |                                                     |
|        +--> ALTER TABLE (ADD/DROP CONSTRAINT)                |
|        |                                                     |
|        +--> CTE Execution                                    |
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
3. ~~**Index Seek** - implement `CreateIndexSeek()` for secondary index~~ ?
4. ~~**Index Range Scan** - implement range queries~~ ?
5. ~~**Index Auto-Update** - update indexes on INSERT/UPDATE/DELETE~~
6. **ALTER TABLE** - add `AddConstraint` / `DropConstraint` to `ExecuteAlterTable`

---

**Last Updated:** 2025-01-28

