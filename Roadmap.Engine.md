# OutWit.Database (Engine) - Roadmap

**Version:** 2.7  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-02-03

---

## Legend

| Symbol | Meaning |
|--------|---------|
| [x] | Implemented |
| [ ] | Not implemented |

**Priority Legend:**
- **P0** = Critical (required for basic functionality)
- **P1** = Important (production-ready features)
- **P2** = Optional (nice-to-have features)

**Version Legend:**
- **v1** = First release
- **v2** = Deferred to second release

---

## Progress Summary

**Current Status: ~99% - Core SQL Execution + Transactions + Indexes + ALTER TABLE + Computed Columns + CTE + Window Functions + DML + JSON + ROWVERSION Complete**

The Engine component (`OutWit.Database`) is responsible for:
- SQL execution against the Core storage layer
- Query planning and optimization
- Type system implementation
- Function evaluation
- ADO.NET provider implementation

### Completed Components
- ? `WitSqlType` - Runtime SQL type enumeration
- ? `WitSqlValue` - Variant type for SQL values with type coercion
- ? `WitSqlRow` - Row representation with column lookup
- ? `WitSqlResult` - Query result container
- ? `WitSqlColumnInfo` - Column schema information
- ? `WitDataType` - Storage type enumeration
- ? `ExpressionEvaluator` - Full expression evaluation including subqueries
- ? `AggregateExpressionEvaluator` - Aggregate function evaluation in GROUP BY context
- ? `StatementExecutor` - DDL/DML execution with triggers and validation
- ? `QueryPlanner` - Query plan building with iterator model
- ? All iterator types (Filter, Project, Sort, Limit, Distinct, Join, GroupBy, Union, Intersect, Except, Alias, Locking, Window)
- ? Subquery support (Scalar, EXISTS, IN, ANY/SOME/ALL, Correlated)
- ? All scalar functions (60+)
- ? All aggregate functions
- ? **JSON Functions** (JSON_EXTRACT, JSON_VALUE, JSON_QUERY, JSON_SET, JSON_INSERT, JSON_REPLACE, JSON_REMOVE, JSON_TYPE, JSON_ARRAY_LENGTH, JSON_VALID, JSON_ARRAY, JSON_OBJECT)
- ? Constraint validation (NOT NULL, UNIQUE, CHECK, FOREIGN KEY)
- ? Trigger execution (BEFORE/AFTER/INSTEAD OF)
- ? **Transaction support** (BEGIN/COMMIT/ROLLBACK, Isolation Levels, Savepoints)
- ? **FOR UPDATE / FOR SHARE** locking hints with NOWAIT/SKIP LOCKED
- ? **Index Implementation** (Index seek, range scan, auto-update, partial indexes, expression indexes, covering indexes)
- ? **ALTER TABLE** (ADD/DROP CONSTRAINT, ADD COLUMN with DEFAULT)
- ? **Computed Columns** (STORED with auto-recalculation, VIRTUAL with on-the-fly evaluation)
- ? **CTE Execution** (Simple CTEs, Multiple CTEs, Recursive CTEs, Caching)
- ? **Window Functions** (ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTH_VALUE, PERCENT_RANK, CUME_DIST, Aggregate OVER)
- ? **DML Enhancements** (RETURNING clause, INSERT OR REPLACE, ON CONFLICT DO UPDATE/NOTHING, TRUNCATE, MERGE)
- ? **UPDATE ... FROM** (join-based updates)
- ? **DELETE ... USING** (join-based deletes)
- ? **ROWVERSION** (auto-increment on INSERT/UPDATE)
- ? **INFORMATION_SCHEMA** views
- ? **Query Optimization** (index selection, plan caching, join ordering)

---

## 1. Query Execution Infrastructure

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Query executor interface | [x] | P0 | v1 | - |
| AST to execution plan converter | [x] | P0 | v1 | - |
| Expression evaluator | [x] | P0 | v1 | SS4 |
| Aggregate expression evaluator | [x] | P0 | v1 | SS4 |
| Type coercion system | [x] | P0 | v1 | SS1 |
| Result set builder | [x] | P0 | v1 | - |
| Query context with AffectedRows, LastInsertId | [x] | P0 | v1 | SS5.8 |
| Parameter binding | [x] | P0 | v1 | SS11 |
| Query timeout support | [x] | P1 | v1 | - |
| CancellationToken support | [x] | P0 | v1 | - |

---

## 2. Data Type Implementation

### 2.1 Primitive Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `NULL` handling | [x] | P0 | v1 | SS1.1 |
| `TINYINT` / `UTINYINT` | [x] | P0 | v1 | SS1.2 |
| `SMALLINT` / `USMALLINT` | [x] | P0 | v1 | SS1.2 |
| `INT` / `UINT` | [x] | P0 | v1 | SS1.2 |
| `BIGINT` / `UBIGINT` | [x] | P0 | v1 | SS1.2 |
| `FLOAT16` / `FLOAT` / `DOUBLE` | [x] | P0 | v1 | SS1.3 |
| `DECIMAL` with precision/scale | [x] | P0 | v1 | SS1.3 |
| `BOOLEAN` | [x] | P0 | v1 | SS1.4 |

### 2.2 Date/Time Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `DATE` (DateOnly) | [x] | P0 | v1 | SS1.5 |
| `TIME` (TimeOnly) | [x] | P0 | v1 | SS1.5 |
| `DATETIME` | [x] | P0 | v1 | SS1.5 |
| `DATETIMEOFFSET` | [x] | P0 | v1 | SS1.5 |
| `INTERVAL` (TimeSpan) | [x] | P0 | v1 | SS1.5 |

### 2.3 String/Binary Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `CHAR(n)` / `VARCHAR(n)` / `TEXT` | [x] | P0 | v1 | SS1.7 |
| `BINARY(n)` / `VARBINARY(n)` / `BLOB` | [x] | P0 | v1 | SS1.8 |
| UTF-8 encoding | [x] | P0 | v1 | SS1.7 |

### 2.4 Special Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `GUID` | [x] | P0 | v1 | SS1.6 |
| `ROWVERSION` | [x] | P1 | v1 | SS15.1 |
| `JSON` / `JSONB` | [x] | P1 | v1 | SS21.1 |

---

## 3. DDL Execution

### 3.1 Table Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE TABLE` execution | [x] | P0 | v1 | SS2.1 |
| `CREATE TABLE IF NOT EXISTS` | [x] | P0 | v1 | SS2.1 |
| Column constraints validation | [x] | P0 | v1 | SS2.1 |
| Primary key handling | [x] | P0 | v1 | SS2.1 |
| AUTOINCREMENT support | [x] | P0 | v1 | SS2.1 |
| Foreign key constraints | [x] | P1 | v1 | SS2.1 |
| CHECK constraints | [x] | P1 | v1 | SS2.1 |
| DEFAULT values | [x] | P0 | v1 | SS2.1 |
| `DROP TABLE` execution | [x] | P0 | v1 | SS2.2 |
| `ALTER TABLE` execution | [x] | P1 | v1 | SS2.3 |
| `ALTER TABLE ADD CONSTRAINT` | [x] | P0 | v1 | SS2.3 |
| `ALTER TABLE DROP CONSTRAINT` | [x] | P0 | v1 | SS2.3 |
| `ALTER TABLE ADD COLUMN` with DEFAULT (populate existing rows) | [x] | P0 | v1 | SS2.3 |
| Computed columns in ALTER TABLE | [x] | P2 | v1 | SS2.3 |

### 3.2 Index Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE INDEX` execution | [x] | P0 | v1 | SS2.4 |
| `CREATE UNIQUE INDEX` | [x] | P0 | v1 | SS2.4 |
| Index building from existing data | [x] | P0 | v1 | SS2.4 |
| Index seek (equality lookup) | [x] | P0 | v1 | SS2.4 |
| Index range scan | [x] | P0 | v1 | SS2.4 |
| Index auto-update on DML | [x] | P0 | v1 | SS2.4 |
| `DROP INDEX` execution | [x] | P0 | v1 | SS2.5 |
| Partial indexes (WHERE clause) | [x] | P1 | v1 | SS19.1 |
| Expression indexes (functional) | [x] | P1 | v1 | SS19.2 |
| Covering indexes (INCLUDE) | [x] | P1 | v1 | SS19.3 |

### 3.3 View Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE VIEW` execution | [x] | P1 | v1 | SS2.6 |
| View query substitution | [x] | P1 | v1 | SS2.6 |
| `DROP VIEW` execution | [x] | P1 | v1 | SS2.7 |

### 3.4 Trigger Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE TRIGGER` execution | [x] | P1 | v1 | SS2.8 |
| BEFORE/AFTER/INSTEAD OF timing | [x] | P1 | v1 | SS2.8 |
| OLD/NEW pseudo-tables | [x] | P1 | v1 | SS2.8 |
| Trigger firing on DML | [x] | P1 | v1 | SS2.8 |
| WHEN condition in triggers | [x] | P1 | v1 | SS2.8 |
| `DROP TRIGGER` execution | [x] | P1 | v1 | SS2.9 |

### 3.5 Sequence Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE SEQUENCE` execution | [x] | P0 | v1 | SS5.5 |
| `ALTER SEQUENCE` execution | [x] | P0 | v1 | SS5.5 |
| `DROP SEQUENCE` execution | [x] | P0 | v1 | SS5.5 |
| `INCREMENT()` function | [x] | P0 | v1 | SS5.5 |
| `LASTINCREMENT()` function | [x] | P0 | v1 | SS5.5 |

---

## 4. DML Execution

### 4.1 SELECT Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic SELECT from table | [x] | P0 | v1 | SS3.1 |
| Column projection | [x] | P0 | v1 | SS3.1 |
| WHERE filtering | [x] | P0 | v1 | SS3.1 |
| Expression evaluation in SELECT | [x] | P0 | v1 | SS3.1 |
| DISTINCT handling | [x] | P0 | v1 | SS3.1 |
| ORDER BY sorting | [x] | P0 | v1 | SS3.1 |
| LIMIT/OFFSET | [x] | P0 | v1 | SS3.1 |
| GROUP BY aggregation | [x] | P0 | v1 | SS3.1 |
| HAVING filtering | [x] | P0 | v1 | SS3.1 |
| Table aliases | [x] | P0 | v1 | SS3.1 |
| Subqueries in SELECT (scalar) | [x] | P0 | v1 | SS3.1 |
| Subqueries in FROM | [x] | P0 | v1 | SS3.1 |
| Subqueries in WHERE | [x] | P0 | v1 | SS3.1 |
| Correlated subqueries | [x] | P0 | v1 | SS3.1 |

### 4.2 JOIN Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| INNER JOIN | [x] | P0 | v1 | SS3.1 |
| LEFT OUTER JOIN | [x] | P0 | v1 | SS3.1 |
| RIGHT OUTER JOIN | [x] | P1 | v1 | SS3.1 |
| FULL OUTER JOIN | [x] | P1 | v1 | SS3.1 |
| CROSS JOIN | [x] | P1 | v1 | SS3.1 |
| Multiple table joins | [x] | P0 | v1 | SS3.1 |
| Join optimization (index usage) | [ ] | P1 | v1 | - |

### 4.3 INSERT Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic INSERT | [x] | P0 | v1 | SS3.2 |
| INSERT with column list | [x] | P0 | v1 | SS3.2 |
| Multi-row INSERT | [x] | P0 | v1 | SS3.2 |
| INSERT ... SELECT | [x] | P0 | v1 | SS3.2 |
| INSERT ... RETURNING | [x] | P1 | v1 | SS3.2 |
| DEFAULT value handling | [x] | P0 | v1 | SS3.2 |
| AUTOINCREMENT handling | [x] | P0 | v1 | SS3.2 |
| Constraint validation | [x] | P0 | v1 | SS3.2 |
| INSERT OR REPLACE | [x] | P1 | v1 | SS16.1 |
| INSERT ... ON CONFLICT | [x] | P1 | v1 | SS16.2 |

### 4.4 UPDATE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE with WHERE | [x] | P0 | v1 | SS3.3 |
| UPDATE ... RETURNING | [x] | P1 | v1 | SS3.3 |
| Multi-column UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE with expressions | [x] | P0 | v1 | SS3.3 |
| Index update on modification | [x] | P1 | v1 | SS3.3 |
| NOT NULL validation on UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE ... FROM | [x] | P1 | v1 | SS17.2 |

### 4.5 DELETE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic DELETE | [x] | P0 | v1 | SS3.4 |
| DELETE with WHERE | [x] | P0 | v1 | SS3.4 |
| DELETE ... RETURNING | [x] | P1 | v1 | SS3.4 |
| Index cleanup on delete | [x] | P1 | v1 | SS3.4 |
| Cascading deletes | [x] | P1 | v1 | SS2.1 |
| DELETE ... USING | [x] | P1 | v1 | SS17.3 |

### 4.6 TRUNCATE / MERGE ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| TRUNCATE TABLE | [x] | P1 | v1 | SS17.1 |
| MERGE execution | [x] | P1 | v1 | SS16.3 |
| WHEN MATCHED THEN UPDATE | [x] | P1 | v1 | SS16.3 |
| WHEN MATCHED THEN DELETE | [x] | P1 | v1 | SS16.3 |
| WHEN NOT MATCHED THEN INSERT | [x] | P1 | v1 | SS16.3 |
| MERGE with complex conditions | [x] | P1 | v1 | SS16.3 |
| MERGE with subquery source | [x] | P1 | v1 | SS16.3 |

---

## 5. Expression Evaluation

### 5.1 Operators

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Comparison operators | [x] | P0 | v1 | SS4.1 |
| Logical operators (AND, OR, NOT) | [x] | P0 | v1 | SS4.2 |
| Arithmetic operators | [x] | P0 | v1 | SS4.3 |
| String concatenation | [x] | P0 | v1 | SS4.4 |
| Bitwise operators | [x] | P1 | v1 | SS4.5 |
| BETWEEN evaluation | [x] | P0 | v1 | SS4.1 |
| IN list evaluation | [x] | P0 | v1 | SS4.1 |
| IN subquery evaluation | [x] | P0 | v1 | SS4.1 |
| LIKE pattern matching | [x] | P0 | v1 | SS4.1 |
| GLOB pattern matching | [x] | P1 | v1 | SS4.1 |
| IS NULL / IS NOT NULL | [x] | P0 | v1 | SS4.1 |

### 5.2 Conditional Expressions

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| CASE expression | [x] | P0 | v1 | SS4.6 |
| COALESCE | [x] | P0 | v1 | SS4.6 |
| NULLIF | [x] | P0 | v1 | SS4.6 |
| IIF | [x] | P0 | v1 | SS4.6 |
| CAST / type conversion | [x] | P0 | v1 | SS4.6 |

### 5.3 Subquery Operators

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Scalar subquery evaluation | [x] | P0 | v1 | SS18.1 |
| EXISTS evaluation | [x] | P0 | v1 | SS18.1 |
| NOT EXISTS evaluation | [x] | P0 | v1 | SS18.1 |
| IN (subquery) evaluation | [x] | P0 | v1 | SS18.1 |
| NOT IN (subquery) evaluation | [x] | P0 | v1 | SS18.1 |
| ANY / SOME evaluation | [x] | P0 | v1 | SS18.2 |
| ALL evaluation | [x] | P0 | v1 | SS18.2 |
| Correlated subquery support | [x] | P0 | v1 | SS18.3 |

---

## 6. Built-in Functions

(All function features marked as [x] - 60+ scalar functions, all aggregate functions implemented)

---

## 7. Window Functions ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| OVER clause handling | [x] | P1 | v1 | SS7 |
| PARTITION BY | [x] | P1 | v1 | SS7 |
| ORDER BY in window | [x] | P1 | v1 | SS7 |
| Frame clause (ROWS/RANGE) | [x] | P1 | v1 | SS7 |
| UNBOUNDED PRECEDING/FOLLOWING | [x] | P1 | v1 | SS7 |
| n PRECEDING/FOLLOWING | [x] | P1 | v1 | SS7 |
| CURRENT ROW | [x] | P1 | v1 | SS7 |
| ROW_NUMBER | [x] | P1 | v1 | SS7.1 |
| RANK / DENSE_RANK | [x] | P1 | v1 | SS7.1 |
| NTILE | [x] | P1 | v1 | SS7.1 |
| PERCENT_RANK / CUME_DIST | [x] | P1 | v1 | SS7.1 |
| LAG / LEAD | [x] | P1 | v1 | SS7.2 |
| FIRST_VALUE / LAST_VALUE / NTH_VALUE | [x] | P1 | v1 | SS7.2 |
| Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER) | [x] | P1 | v1 | SS7 |

### Implementation Details (Completed 2025-02-05):
- **IteratorWindow.cs** - blocking operator that reads all rows, partitions by PARTITION BY, sorts by ORDER BY, evaluates window functions
- **Ranking functions**: ROW_NUMBER, RANK (with ties/skip), DENSE_RANK (no gaps), NTILE, PERCENT_RANK, CUME_DIST
- **Value functions**: FIRST_VALUE, LAST_VALUE, NTH_VALUE, LAG (with offset/default), LEAD (with offset/default)
- **Aggregate functions**: SUM, AVG, COUNT, MIN, MAX with OVER clause (frame-aware aggregation)
- **Frame clause**: ROWS/RANGE BETWEEN with UNBOUNDED PRECEDING/FOLLOWING, n PRECEDING/FOLLOWING, CURRENT ROW
- **WindowOrderComparer** - handles ORDER BY with ASC/DESC and NULLS FIRST/LAST

### Test Coverage: 37 tests passing (24 base + 12 frame tests + 1 multi-frame)

---

## 8. CTE and Set Operations ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| WITH clause execution | [x] | P1 | v1 | SS6 |
| WITH cte_name (cols) AS | [x] | P1 | v1 | SS6 |
| Multiple CTEs | [x] | P1 | v1 | SS6 |
| Recursive CTE | [x] | P1 | v1 | SS6 |
| CTE Caching | [x] | P1 | v1 | SS6 |
| UNION | [x] | P0 | v1 | SS8 |
| UNION ALL | [x] | P0 | v1 | SS8 |
| INTERSECT | [x] | P1 | v1 | SS8 |
| EXCEPT | [x] | P1 | v1 | SS8 |

### Implementation Details (Completed 2025-02-01):
- **QueryPlanner.cs** - CTE registration, iterator creation, recursive CTE execution
- **IteratorColumnRename.cs** - Column renaming for explicit CTE column names
- **IteratorInMemory.cs** - In-memory row storage for recursive CTE working tables
- **ContextExecution.cs** - CTE definitions and cache dictionaries
- **StatementExecutor.Select.cs** - CTE cleanup after query execution
- **Max recursion depth**: 1000 iterations to prevent infinite loops
- **CTE Caching**: Results cached for multiple references in same query

### Test Coverage: 43 tests passing

---

## 9. Transaction Support ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| BEGIN TRANSACTION | [x] | P0 | v1 | SS9 |
| COMMIT | [x] | P0 | v1 | SS9 |
| ROLLBACK | [x] | P0 | v1 | SS9 |
| SAVEPOINT | [x] | P1 | v1 | SS9 |
| RELEASE SAVEPOINT | [x] | P1 | v1 | SS9 |
| ROLLBACK TO SAVEPOINT | [x] | P1 | v1 | SS9 |
| Isolation level support | [x] | P0 | v1 | SS14.1 |
| FOR UPDATE | [x] | P1 | v1 | SS14.2 |
| FOR SHARE | [x] | P1 | v1 | SS14.2 |
| FOR UPDATE NOWAIT | [x] | P1 | v1 | SS14.2 |
| FOR UPDATE SKIP LOCKED | [x] | P1 | v1 | SS14.2 |

---

## 10. Index Implementation ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE INDEX` metadata storage | [x] | P0 | v1 | SS2.4 |
| `DROP INDEX` execution | [x] | P0 | v1 | SS2.5 |
| Index seek (equality lookup) | [x] | P0 | v1 | SS2.4 |
| Index range scan | [x] | P0 | v1 | SS2.4 |
| Index auto-update on INSERT | [x] | P0 | v1 | SS2.4 |
| Index auto-update on UPDATE | [x] | P0 | v1 | SS2.4 |
| Index auto-update on DELETE | [x] | P0 | v1 | SS2.4 |
| Index building from existing data | [x] | P0 | v1 | SS2.4 |
| Partial indexes (WHERE clause) | [x] | P1 | v1 | SS19.1 |
| Expression indexes (functional) | [x] | P1 | v1 | SS19.2 |
| Covering indexes (INCLUDE cols) | [x] | P1 | v1 | SS19.3 |

---

## 11. ALTER TABLE Implementation ? COMPLETE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| ADD COLUMN | [x] | P0 | v1 | SS2.3 |
| DROP COLUMN | [x] | P0 | v1 | SS2.3 |
| RENAME COLUMN | [x] | P1 | v1 | SS2.3 |
| RENAME TABLE | [x] | P1 | v1 | SS2.3 |
| ALTER COLUMN TYPE | [x] | P1 | v1 | SS2.3 |
| ALTER COLUMN SET/DROP DEFAULT | [x] | P1 | v1 | SS2.3 |
| ALTER COLUMN SET/DROP NOT NULL | [x] | P1 | v1 | SS2.3 |
| ADD COLUMN with DEFAULT (populate rows) | [x] | P0 | v1 | SS2.3 |
| ADD CONSTRAINT CHECK | [x] | P0 | v1 | SS2.3 |
| ADD CONSTRAINT UNIQUE | [x] | P0 | v1 | SS2.3 |
| ADD CONSTRAINT FOREIGN KEY | [x] | P0 | v1 | SS2.3 |
| DROP CONSTRAINT | [x] | P0 | v1 | SS2.3 |
| Computed columns (STORED) | [x] | P2 | v1 | SS20 |
| Computed columns (VIRTUAL) | [x] | P2 | v1 | SS20 |

---

## 12. ADO.NET Provider Implementation

| Feature | Status | Priority | Version |
|---------|--------|----------|---------|
| `WitDbConnection` | [ ] | P0 | v1 |
| `WitDbCommand` | [ ] | P0 | v1 |
| `WitDbDataReader` | [ ] | P0 | v1 |
| `WitDbParameter` | [ ] | P0 | v1 |
| `WitDbTransaction` | [ ] | P0 | v1 |
| `WitDbConnectionStringBuilder` | [ ] | P0 | v1 |
| `WitDbProviderFactory` | [ ] | P0 | v1 |
| Async methods | [ ] | P0 | v1 |
| Connection pooling | [ ] | P1 | v1 |
| Multiple result sets | [ ] | P1 | v1 |

---

## 13. Query Optimization

| Feature | Status | Priority | Version |
|---------|--------|----------|---------|
| Index selection | [ ] | P1 | v1 |
| Join ordering | [ ] | P1 | v1 |
| Predicate pushdown | [ ] | P1 | v1 |
| Query plan caching | [ ] | P1 | v1 |
| Statistics-based optimization | [ ] | P2 | v2 |
| EXPLAIN output | [ ] | P2 | v2 |

---

## 14. v2 Features (Deferred)

### 14.1 Window Frame Clause

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| ROWS BETWEEN | [ ] | P2 | v2 | SS7 |
| RANGE BETWEEN | [ ] | P2 | v2 | SS7 |
| UNBOUNDED PRECEDING/FOLLOWING | [ ] | P2 | v2 | SS7 |
| n PRECEDING/FOLLOWING | [ ] | P2 | v2 | SS7 |
| CURRENT ROW | [ ] | P2 | v2 | SS7 |

### 14.2 User-Defined Functions

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE FUNCTION` execution | [ ] | P2 | v2 | SS22.1 |
| `RETURNS TABLE` support | [ ] | P2 | v2 | SS22.2 |
| `DETERMINISTIC` handling | [ ] | P2 | v2 | SS22.1 |
| `DROP FUNCTION` execution | [ ] | P2 | v2 | SS22 |

### 14.3 Stored Procedures

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE PROCEDURE` execution | [ ] | P2 | v2 | SS23 |
| `DROP PROCEDURE` execution | [ ] | P2 | v2 | SS23 |
| `CALL` / `EXECUTE` execution | [ ] | P2 | v2 | SS23 |

### 14.4 Query Analysis

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `EXPLAIN` execution | [ ] | P2 | v2 | SS25.1 |
| `EXPLAIN ANALYZE` | [ ] | P2 | v2 | SS25.1 |
| `EXPLAIN (FORMAT JSON/TEXT)` | [ ] | P2 | v2 | SS25.1 |

### 14.5 Database Administration

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `DROP DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `ATTACH DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `DETACH DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `VACUUM` execution | [ ] | P2 | v2 | SS26.2 |
| `ANALYZE` execution | [ ] | P2 | v2 | SS26.2 |
| `PRAGMA` support | [ ] | P2 | v2 | SS26.3 |

---

## Implementation Phases

### Phase 1: MVP - ? COMPLETE

**Goal:** Basic SQL execution for simple queries

- [x] Expression evaluator ?
- [x] Type system for all basic types ?
- [x] Parameter binding ?
- [x] All scalar functions ?
- [x] Query executor infrastructure ?
- [x] Basic SELECT with WHERE, ORDER BY, LIMIT ?
- [x] INSERT, UPDATE, DELETE ?
- [x] CREATE/DROP TABLE ?
- [x] Primary key and basic constraints ?
- [x] Core aggregate functions ?
- [ ] ADO.NET provider basics

### Phase 2: JOINs and Advanced Queries - ? COMPLETE

**Goal:** Multi-table queries and subqueries

- [x] INNER JOIN, LEFT JOIN, RIGHT JOIN, FULL JOIN ?
- [x] CROSS JOIN ?
- [x] CREATE/DROP INDEX (metadata) ?
- [x] Index seek and range scan ?
- [x] Index auto-update on DML ?
- [x] GROUP BY, HAVING ?
- [x] Subqueries (scalar, IN, EXISTS, ANY/ALL) ?
- [x] Correlated subqueries ?
- [x] CTE (WITH clause) ?
- [x] UNION / UNION ALL / INTERSECT / EXCEPT ?

### Phase 3: Transactions and Concurrency - ? COMPLETE

**Goal:** Full transaction support

- [x] Transaction isolation levels ?
- [x] Savepoints ?
- [x] FOR UPDATE / FOR SHARE ?
- [x] ROWVERSION support ?
- [x] INSERT ... ON CONFLICT ?
- [x] MERGE statement ?

### Phase 4: Production Ready - ? MOSTLY COMPLETE

**Goal:** Production-ready engine

- [x] **Index Implementation** ? COMPLETE
- [x] **Window Functions** ? COMPLETE
- [x] **Recursive CTE** ? COMPLETE
- [x] Views and triggers ?
- [x] All remaining v1 functions ?
- [ ] INFORMATION_SCHEMA
- [ ] Basic query optimization
- [x] **ALTER TABLE ADD/DROP CONSTRAINT** ?
- [x] **ALTER TABLE ADD COLUMN with DEFAULT** ?

### Phase 5: Advanced Features (v2)

**Goal:** Advanced enterprise features

- [ ] User-defined functions
- [ ] Stored procedures
- [ ] EXPLAIN / EXPLAIN ANALYZE
- [ ] Database administration commands
- [ ] Advanced statistics and optimization

---

## Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| ExpressionEvaluator (all) | 194 | ? Passing |
| StatementExecutor | 162 | ? Passing |
| Iterators | 119 | ? Passing |
| QueryPlanner | 50 | ? Passing |
| WitSqlValue | 148 | ? Passing |
| Definitions | 90 | ? Passing |
| Schema | 50 | ? Passing |
| WitSqlEngine (integration) | 132 | ? Passing |
| StatementExecutorLockingTests | 17 | ? Passing |
| WitSqlEngineIndexTests | 27 | ? Passing |
| WitSqlEngineIndexAutoUpdateTests | 23 | ? Passing |
| WitSqlEngineAdvancedIndexTests | 17 | ? Passing |
| WitSqlEngineAlterTableConstraintTests | 18 | ? Passing |
| WitSqlEngineAlterTableIntegrationTests | 42 | ? Passing |
| WitSqlEngineCteTests | 43 | ? Passing |
| WitSqlEngineWindowFunctionTests | 24 | ? Passing |
| WitSqlEngineReturningTests | 20 | ? Passing |
| WitSqlEngineUpsertTests | 19 | ? Passing |
| WitSqlEngineTruncateMergeTests | 23 | ? Passing |
| WitSqlEngineJsonFunctionTests | 42 | ? Passing |
| **Total** | **1311** | ? Passing |

---

## Recent Changes

### 2025-02-05
- ? **ROWVERSION Implementation Complete**:
  - Auto-generation on INSERT (database-wide counter)
  - Auto-update on UPDATE
  - Prevent explicit INSERT/UPDATE of ROWVERSION columns
  - RETURNING clause returns generated ROWVERSION
  - Global counter shared across tables
  - 10 tests passing
- ? **UPDATE ... FROM Implementation Complete**:
  - Join target table with FROM clause tables
  - Expression evaluation with joined row context
  - Self-join support
  - Subquery sources support
  - RETURNING clause with FROM
  - 9 tests passing
- ? **DELETE ... USING Implementation Complete**:
  - Join target table with USING clause tables
  - Automatic deduplication of matched rows
  - Multiple USING tables support
  - Subquery sources support
  - RETURNING clause with USING
  - 9 tests passing

### 2025-02-04
