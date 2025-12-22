# OutWit.Database.Parser - TODO

Analysis of the WitSQL parser for compliance with updated specifications (v1.2).

## Implementation Status

### Implemented Components

| Component | Status | File/Class |
|-----------|--------|------------|
| **DML** | | |
| SELECT (basic) | ? Done | `WitSqlStatementSelect` |
| INSERT + RETURNING | ? Done | `WitSqlStatementInsert` |
| UPDATE + RETURNING | ? Done | `WitSqlStatementUpdate` |
| DELETE + RETURNING | ? Done | `WitSqlStatementDelete` |
| CTE (WITH) | ? Done | `ClauseCteDefinition` |
| Set Operations | ? Done | `ClauseSetOperation` |
| **DDL** | | |
| CREATE TABLE | ? Done | `WitSqlStatementCreateTable` |
| DROP TABLE | ? Done | `WitSqlStatementDropTable` |
| ALTER TABLE | ? Done | `WitSqlStatementAlterTable` |
| CREATE INDEX | ? Done | `WitSqlStatementCreateIndex` |
| DROP INDEX | ? Done | `WitSqlStatementDropIndex` |
| CREATE VIEW | ? Done | `WitSqlStatementCreateView` |
| DROP VIEW | ? Done | `WitSqlStatementDropView` |
| CREATE TRIGGER | ? Done | `WitSqlStatementCreateTrigger` |
| DROP TRIGGER | ? Done | `WitSqlStatementDropTrigger` |
| CREATE SEQUENCE | ? Done | `WitSqlStatementCreateSequence` |
| DROP SEQUENCE | ? Done | `WitSqlStatementDropSequence` |
| ALTER SEQUENCE | ? Done | `WitSqlStatementAlterSequence` |
| TRUNCATE TABLE | ? Done | `WitSqlStatementTruncate` |
| **Transactions** | | |
| BEGIN | ? Done | `WitSqlStatementBeginTransaction` |
| COMMIT | ? Done | `WitSqlStatementCommit` |
| ROLLBACK | ? Done | `WitSqlStatementRollback` |
| SAVEPOINT | ? Done | `WitSqlStatementSavepoint` |
| RELEASE SAVEPOINT | ? Done | `WitSqlStatementReleaseSavepoint` |
| **Expressions** | | |
| Literals | ? Done | `WitSqlExpressionLiteral` |
| Column References | ? Done | `WitSqlExpressionColumnRef` |
| Binary Operators | ? Done | `WitSqlExpressionBinary` |
| Unary Operators | ? Done | `WitSqlExpressionUnary` |
| Function Calls | ? Done | `WitSqlExpressionFunctionCall` |
| CASE | ? Done | `WitSqlExpressionCase` |
| CAST/CONVERT | ? Done | `WitSqlExpressionCast` |
| BETWEEN | ? Done | `WitSqlExpressionBetween` |
| IN | ? Done | `WitSqlExpressionIn` |
| LIKE | ? Done | `WitSqlExpressionLike` |
| IS NULL | ? Done | `WitSqlExpressionIsNull` |
| Subquery | ? Done | `WitSqlExpressionSubquery` |
| GLOB | ? Done | `WitSqlExpressionGlob` |
| IIF | ? Done | `WitSqlExpressionIif` |
| EXISTS | ? Done | `WitSqlExpressionExists` |
| Parameters | ? Done | `WitSqlExpressionParameter` |
| Window Functions | ? Done | `SpecWindow` |
| **Types** | | |
| All basic types | ? Done | Grammar `typeName` |
| ROWVERSION | ? Done | Grammar `typeName` |
| JSON/JSONB | ? Done | Grammar `typeName` |

---

## Missing Components (from specification v1.2)

### Category 1: Data Types

- [x] **1.1** `ROWVERSION` / `TIMESTAMP` (alias) - type for concurrency control
- [x] **1.2** `JSON` / `JSONB` - types for JSON documents

### Category 2: SET TRANSACTION ISOLATION LEVEL

- [ ] **2.1** `SET TRANSACTION ISOLATION LEVEL` statement
- [ ] **2.2** Isolation level enum: `READ UNCOMMITTED`, `READ COMMITTED`, `REPEATABLE READ`, `SERIALIZABLE`, `SNAPSHOT`
- [ ] **2.3** AST node: `WitSqlStatementSetTransaction`

### Category 3: Locking Hints (FOR UPDATE/SHARE)

- [ ] **3.1** `FOR UPDATE` clause in SELECT
- [ ] **3.2** `FOR SHARE` clause in SELECT
- [ ] **3.3** `NOWAIT` modifier
- [ ] **3.4** `SKIP LOCKED` modifier
- [ ] **3.5** Extend `WitSqlStatementSelect` for locking hints

### Category 4: UPSERT / MERGE

- [x] **4.1** `INSERT OR REPLACE` statement
- [x] **4.2** `INSERT ... ON CONFLICT (columns) DO UPDATE` statement
- [x] **4.3** `INSERT ... ON CONFLICT (columns) DO NOTHING` statement
- [ ] **4.4** `MERGE INTO ... USING ... WHEN MATCHED/NOT MATCHED` statement
- [x] **4.5** AST nodes: `WitSqlStatementMerge`, extend `WitSqlStatementInsert`
 
### Category 5: TRUNCATE TABLE

- [x] **5.1** `TRUNCATE TABLE table_name` statement
- [x] **5.2** AST node: `WitSqlStatementTruncate`

### Category 6: Advanced Index Features

- [ ] **6.1** `WHERE` clause in CREATE INDEX (partial/filtered indexes)
- [ ] **6.2** Expression indexes: `CREATE INDEX ... ON (LOWER(column))`
- [ ] **6.3** `INCLUDE` clause (covering indexes)
- [ ] **6.4** Extend `WitSqlStatementCreateIndex`

### Category 7: Computed Columns

- [ ] **7.1** `column AS (expression)` in CREATE TABLE
- [ ] **7.2** `STORED` / `VIRTUAL` modifiers
- [ ] **7.3** Extend column definition in grammar

### Category 8: JSON Functions

- [ ] **8.1** `JSON_VALUE(json, path)` function
- [ ] **8.2** `JSON_QUERY(json, path)` function
- [ ] **8.3** `JSON_EXTRACT(json, path)` function
- [ ] **8.4** `JSON_SET(json, path, value)` function
- [ ] **8.5** `JSON_INSERT`, `JSON_REPLACE`, `JSON_REMOVE` functions
- [ ] **8.6** `JSON_TYPE`, `JSON_VALID` functions
- [ ] **8.7** `JSON_ARRAY`, `JSON_OBJECT` functions
- [ ] **8.8** Add to `functionName` rule in grammar

### Category 9: User-Defined Functions

- [ ] **9.1** `CREATE FUNCTION ... RETURNS ... AS BEGIN ... END` statement
- [ ] **9.2** Table-valued functions: `RETURNS TABLE (...)`
- [ ] **9.3** `DETERMINISTIC` modifier
- [ ] **9.4** `DROP FUNCTION` statement
- [ ] **9.5** AST nodes: `WitSqlStatementCreateFunction`, `WitSqlStatementDropFunction`

### Category 10: Stored Procedures

- [ ] **10.1** `CREATE PROCEDURE ... AS BEGIN ... END` statement
- [ ] **10.2** `DROP PROCEDURE` statement
- [ ] **10.3** `CALL procedure_name(args)` statement
- [ ] **10.4** `EXECUTE procedure_name(args)` statement
- [ ] **10.5** AST nodes: `WitSqlStatementCreateProcedure`, `WitSqlStatementDropProcedure`, `WitSqlStatementCall`

### Category 11: Collation

- [ ] **11.1** `COLLATE collation_name` in column definition
- [ ] **11.2** `COLLATE collation_name` in expression
- [ ] **11.3** `COLLATE collation_name` in ORDER BY
- [ ] **11.4** Collation names: `BINARY`, `NOCASE`, `UNICODE`, `UNICODE_CI`
- [ ] **11.5** Extend grammar for collation

### Category 12: EXPLAIN

- [ ] **12.1** `EXPLAIN select_statement` statement
- [ ] **12.2** `EXPLAIN ANALYZE select_statement` statement
- [ ] **12.3** `EXPLAIN (FORMAT JSON/TEXT)` options
- [ ] **12.4** AST node: `WitSqlStatementExplain`

### Category 13: Database Administration

- [ ] **13.1** `CREATE DATABASE database_name` statement
- [ ] **13.2** `DROP DATABASE [IF EXISTS] database_name` statement
- [ ] **13.3** `ATTACH DATABASE 'path' AS alias` statement
- [ ] **13.4** `DETACH DATABASE alias` statement
- [ ] **13.5** `VACUUM [table_name]` statement
- [ ] **13.6** `ANALYZE [table_name]` statement
- [ ] **13.7** `PRAGMA name [= value]` statement
- [ ] **13.8** AST nodes for all of the above

### Category 14: Named Constraints

- [ ] **14.1** `CONSTRAINT constraint_name` prefix for table constraints
- [ ] **14.2** `ALTER TABLE ... DROP CONSTRAINT constraint_name`
- [ ] **14.3** Extend `TableConstraint` for named constraints

### Category 15: UPDATE/DELETE with FROM

- [ ] **15.1** `UPDATE ... FROM other_table WHERE` syntax
- [ ] **15.2** `DELETE FROM ... USING other_table WHERE` syntax
- [ ] **15.3** Extend `WitSqlStatementUpdate`, `WitSqlStatementDelete`

### Category 16: ANY / SOME / ALL Operators

- [x] **16.1** `expression > ANY (subquery)` expression
- [x] **16.2** `expression > SOME (subquery)` expression (alias for ANY)
- [x] **16.3** `expression > ALL (subquery)` expression
- [x] **16.4** AST node: `WitSqlExpressionQuantified`

### Category 17: LEFT/RIGHT Functions

- [x] **17.1** `LEFT(str, n)` function
- [x] **17.2** `RIGHT(str, n)` function
- [x] **17.3** Add to `functionName` rule

---

## Implementation Priorities

### MVP (Critical for ADO.NET)

| # | Component | Priority |
|---|-----------|----------|
| 4.1-4.4 | UPSERT / ON CONFLICT | ?? Critical |
| ~~5.1-5.2~~ | ~~TRUNCATE TABLE~~ | ~~? Done~~ |
| 16.1-16.4 | ANY/SOME/ALL | ?? Critical |
| ~~1.1-1.2~~ | ~~ROWVERSION, JSON types~~ | ~~? Done~~ |

### Production Ready (EF Core)

| # | Component | Priority |
|---|-----------|----------|
| 2.1-2.3 | SET TRANSACTION ISOLATION | ?? Critical |
| 3.1-3.5 | FOR UPDATE/SHARE | ?? Critical |
| 14.1-14.3 | Named Constraints | ?? Critical |
| 6.1-6.4 | Advanced Indexes | ?? Important |
| 7.1-7.3 | Computed Columns | ?? Important |
| 11.1-11.5 | Collation | ?? Important |
| 15.1-15.3 | UPDATE/DELETE FROM | ?? Important |

### Advanced Features

| # | Component | Priority |
|---|-----------|----------|
| 8.1-8.8 | JSON Functions | ?? Important |
| 9.1-9.5 | User-Defined Functions | ?? Optional |
| 10.1-10.5 | Stored Procedures | ?? Optional |
| 12.1-12.4 | EXPLAIN | ?? Optional |
| 13.1-13.8 | Database Administration | ?? Optional |

---

## Notes

1. **UPSERT/ON CONFLICT** - critical for EF Core bulk operations and merge scenarios.

2. **SET TRANSACTION ISOLATION** - required for ADO.NET IsolationLevel support.

3. **FOR UPDATE/SHARE** - used for pessimistic concurrency in EF Core.

4. **Named Constraints** - EF Core migrations generate named constraints.

5. **Current parser** covers ~75% of specification v1.0, but for v1.2 significant grammar extension is required.

---

## Files to Modify

### Grammar (WitSql.g4)

```
- ? Add: ROWVERSION, JSON, JSONB to typeName
- Add: FOR UPDATE/SHARE, NOWAIT, SKIP, LOCKED to selectStatement
- Add: ON CONFLICT, DO UPDATE, DO NOTHING to insertStatement
- Add: MERGE statement
- ? Add: TRUNCATE statement
- Add: SET TRANSACTION statement
- Add: COLLATE to expressions and column definitions
- Add: CREATE/DROP FUNCTION, PROCEDURE
- Add: EXPLAIN statement
- Add: PRAGMA, VACUUM, ANALYZE, ATTACH, DETACH
- Add: ANY, SOME, ALL to expressions
- Add: CONSTRAINT name prefix to table constraints
- Add: JSON_* functions to functionName
- Add: LEFT, RIGHT functions
```

### Visitor (WitSqlVisitor.*.cs)

```
- Add visitor methods for new statements
- Extend VisitInsertStatement for ON CONFLICT
- Extend VisitSelectStatement for FOR UPDATE/SHARE
- Extend VisitCreateTableStatement for computed columns
- Extend VisitCreateIndexStatement for WHERE, INCLUDE
```

### New AST Nodes

```
- WitSqlStatementMerge
- ? WitSqlStatementTruncate
- WitSqlStatementSetTransaction
- WitSqlStatementExplain
- WitSqlStatementCreateFunction
- WitSqlStatementDropFunction
- WitSqlStatementCreateProcedure
- WitSqlStatementDropProcedure
- WitSqlStatementCall
- WitSqlStatementPragma
- WitSqlStatementVacuum
- WitSqlStatementAnalyze
- WitSqlStatementAttachDatabase
- WitSqlStatementDetachDatabase
- WitSqlStatementCreateDatabase
- WitSqlStatementDropDatabase
- WitSqlExpressionQuantified (ANY/SOME/ALL)
- LockingHint enum
- IsolationLevel enum
- CollationType enum
