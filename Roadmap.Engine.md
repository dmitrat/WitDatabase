# OutWit.Database (Engine) - Roadmap

**Version:** 2.2  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-01-28

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

**Current Status: ~70% - Core SQL Execution + Transactions Complete**

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
- ? All iterator types (Filter, Project, Sort, Limit, Distinct, Join, GroupBy, Union, Intersect, Except, Alias, Locking)
- ? Subquery support (Scalar, EXISTS, IN, ANY/SOME/ALL, Correlated)
- ? All scalar functions (60+)
- ? All aggregate functions
- ? Constraint validation (NOT NULL, UNIQUE, CHECK, FOREIGN KEY)
- ? Trigger execution (BEFORE/AFTER/INSTEAD OF)
- ? **Transaction support** (BEGIN/COMMIT/ROLLBACK, Isolation Levels, Savepoints)
- ? **FOR UPDATE / FOR SHARE** locking hints with NOWAIT/SKIP LOCKED

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
| Query timeout support | [ ] | P1 | v1 | - |
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
| `ROWVERSION` | [ ] | P1 | v1 | SS15.1 |
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
| `ALTER TABLE ADD CONSTRAINT` | [ ] | P0 | v1 | SS2.3 |
| `ALTER TABLE DROP CONSTRAINT` | [ ] | P0 | v1 | SS2.3 |
| `ALTER TABLE ADD COLUMN` with DEFAULT (populate existing rows) | [ ] | P0 | v1 | SS2.3 |

### 3.2 Index Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE INDEX` execution | [x] | P0 | v1 | SS2.4 |
| `CREATE UNIQUE INDEX` | [x] | P0 | v1 | SS2.4 |
| Index building from existing data | [ ] | P0 | v1 | SS2.4 |
| Index seek (equality lookup) | [ ] | P0 | v1 | SS2.4 |
| Index range scan | [ ] | P0 | v1 | SS2.4 |
| Index auto-update on DML | [ ] | P0 | v1 | SS2.4 |
| `DROP INDEX` execution | [x] | P0 | v1 | SS2.5 |
| Partial indexes | [ ] | P1 | v1 | SS19.1 |
| Expression indexes | [ ] | P1 | v1 | SS19.2 |
| Covering indexes | [ ] | P1 | v1 | SS19.3 |

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
| INSERT ... RETURNING | [ ] | P1 | v1 | SS3.2 |
| DEFAULT value handling | [x] | P0 | v1 | SS3.2 |
| AUTOINCREMENT handling | [x] | P0 | v1 | SS3.2 |
| Constraint validation | [x] | P0 | v1 | SS3.2 |
| INSERT OR REPLACE | [ ] | P1 | v1 | SS16.1 |
| INSERT ... ON CONFLICT | [ ] | P1 | v1 | SS16.2 |

### 4.4 UPDATE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE with WHERE | [x] | P0 | v1 | SS3.3 |
| UPDATE ... RETURNING | [ ] | P1 | v1 | SS3.3 |
| Multi-column UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE with expressions | [x] | P0 | v1 | SS3.3 |
| Index update on modification | [ ] | P1 | v1 | SS3.3 |
| NOT NULL validation on UPDATE | [x] | P0 | v1 | SS3.3 |
| UPDATE ... FROM | [ ] | P1 | v1 | SS17.2 |

### 4.5 DELETE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic DELETE | [x] | P0 | v1 | SS3.4 |
| DELETE with WHERE | [x] | P0 | v1 | SS3.4 |
| DELETE ... RETURNING | [ ] | P1 | v1 | SS3.4 |
| Index cleanup on delete | [ ] | P1 | v1 | SS3.4 |
| Cascading deletes | [ ] | P1 | v1 | SS2.1 |
| DELETE ... USING | [ ] | P1 | v1 | SS17.3 |

### 4.6 TRUNCATE / MERGE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| TRUNCATE TABLE | [ ] | P1 | v1 | SS17.1 |
| MERGE execution | [ ] | P1 | v1 | SS16.3 |

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

### 6.1 Aggregate Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| COUNT(*) | [x] | P0 | v1 | SS5.1 |
| COUNT(expr) | [x] | P0 | v1 | SS5.1 |
| COUNT(DISTINCT expr) | [x] | P0 | v1 | SS5.1 |
| SUM | [x] | P0 | v1 | SS5.1 |
| AVG | [x] | P0 | v1 | SS5.1 |
| MIN | [x] | P0 | v1 | SS5.1 |
| MAX | [x] | P0 | v1 | SS5.1 |
| GROUP_CONCAT | [x] | P1 | v1 | SS5.1 |

### 6.2 String Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| LENGTH / CHAR_LENGTH | [x] | P0 | v1 | SS5.2 |
| OCTET_LENGTH | [x] | P0 | v1 | SS5.2 |
| UPPER / LOWER | [x] | P0 | v1 | SS5.2 |
| SUBSTR / SUBSTRING | [x] | P0 | v1 | SS5.2 |
| LEFT / RIGHT | [x] | P0 | v1 | SS5.2 |
| TRIM / LTRIM / RTRIM | [x] | P0 | v1 | SS5.2 |
| REPLACE | [x] | P0 | v1 | SS5.2 |
| INSTR / POSITION | [x] | P0 | v1 | SS5.2 |
| CONCAT / CONCAT_WS | [x] | P0 | v1 | SS5.2 |
| REVERSE | [x] | P1 | v1 | SS5.2 |
| REPEAT | [x] | P1 | v1 | SS5.2 |
| SPACE | [x] | P1 | v1 | SS5.2 |
| LPAD / RPAD | [x] | P1 | v1 | SS5.2 |

### 6.3 Numeric Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| ABS | [x] | P0 | v1 | SS5.3 |
| ROUND / FLOOR / CEIL / TRUNC | [x] | P0 | v1 | SS5.3 |
| MOD | [x] | P0 | v1 | SS5.3 |
| POWER / SQRT | [x] | P1 | v1 | SS5.3 |
| SIGN | [x] | P1 | v1 | SS5.3 |
| EXP / LOG / LOG10 / LOG2 | [x] | P1 | v1 | SS5.3 |
| PI / DEGREES / RADIANS | [x] | P2 | v1 | SS5.3 |
| SIN / COS / TAN / ASIN / ACOS / ATAN / ATAN2 | [x] | P2 | v1 | SS5.3 |
| RANDOM | [x] | P1 | v1 | SS5.3 |

### 6.4 Date/Time Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| NOW / CURRENT_TIMESTAMP | [x] | P0 | v1 | SS5.4 |
| CURRENT_DATE / CURRENT_TIME | [x] | P0 | v1 | SS5.4 |
| DATE / TIME extraction | [x] | P0 | v1 | SS5.4 |
| YEAR / MONTH / DAY / HOUR / MINUTE / SECOND | [x] | P0 | v1 | SS5.4 |
| DAYOFWEEK / DAYOFYEAR / WEEKOFYEAR / QUARTER | [x] | P1 | v1 | SS5.4 |
| DATEADD / DATEDIFF | [x] | P0 | v1 | SS5.4 |
| STRFTIME | [x] | P1 | v1 | SS5.4 |
| MAKEDATE / MAKETIME | [x] | P1 | v1 | SS5.4 |

### 6.5 ID Generation Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| NEWGUID / NEWUUID | [x] | P0 | v1 | SS5.5 |
| INCREMENT / NEXTVAL | [x] | P0 | v1 | SS5.5 |
| LASTINCREMENT / CURRVAL | [x] | P0 | v1 | SS5.5 |

### 6.6 Conversion Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| CAST / CONVERT | [x] | P0 | v1 | SS5.6 |
| TOSTRING / TOINT / TOREAL / TOBOOL / TODECIMAL / TODATETIME / TOGUID | [x] | P1 | v1 | SS5.6 |
| HEX / UNHEX | [x] | P1 | v1 | SS5.6 |
| BASE64 / UNBASE64 | [x] | P1 | v1 | SS5.6 |
| FORMAT | [x] | P1 | v1 | SS5.6 |

### 6.7 Null Handling Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| COALESCE | [x] | P0 | v1 | SS5.7 |
| NULLIF | [x] | P0 | v1 | SS5.7 |
| IFNULL / NVL | [x] | P0 | v1 | SS5.7 |

### 6.8 System Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| DATABASE | [x] | P1 | v1 | SS5.8 |
| VERSION | [x] | P1 | v1 | SS5.8 |
| TYPEOF | [x] | P1 | v1 | SS5.8 |
| CHANGES | [x] | P0 | v1 | SS5.8 |
| LAST_INSERT_ROWID | [x] | P0 | v1 | SS5.8 |

### 6.9 JSON Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| JSON_VALUE / JSON_QUERY | [ ] | P1 | v1 | SS21.2 |
| JSON_EXTRACT | [x] | P1 | v1 | SS21.2 |
| JSON_SET / JSON_INSERT | [ ] | P1 | v1 | SS21.2 |
| JSON_ARRAY / JSON_OBJECT | [ ] | P1 | v1 | SS21.2 |
| JSON_TYPE | [x] | P1 | v1 | SS21.2 |
| JSON_ARRAY_LENGTH | [x] | P1 | v1 | SS21.2 |

---

## 7. Window Functions

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| OVER clause handling | [ ] | P1 | v1 | SS7 |
| PARTITION BY | [ ] | P1 | v1 | SS7 |
| ORDER BY in window | [ ] | P1 | v1 | SS7 |
| Frame clause (ROWS/RANGE) | [ ] | P1 | v1 | SS7 |
| ROW_NUMBER | [ ] | P1 | v1 | SS7.1 |
| RANK / DENSE_RANK | [ ] | P1 | v1 | SS7.1 |
| NTILE | [ ] | P2 | v1 | SS7.1 |
| LAG / LEAD | [ ] | P1 | v1 | SS7.2 |
| FIRST_VALUE / LAST_VALUE | [ ] | P2 | v1 | SS7.2 |

---

## 8. CTE and Set Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| WITH clause execution | [ ] | P1 | v1 | SS6 |
| Multiple CTEs | [ ] | P1 | v1 | SS6 |
| Recursive CTE | [ ] | P1 | v1 | SS6 |
| UNION | [x] | P0 | v1 | SS8 |
| UNION ALL | [x] | P0 | v1 | SS8 |
| INTERSECT | [x] | P1 | v1 | SS8 |
| EXCEPT | [x] | P1 | v1 | SS8 |

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

### Implementation Details:
- **Transaction SQL**: `BEGIN TRANSACTION`, `COMMIT`, `ROLLBACK` executed via `StatementExecutor.Transactions.cs`
- **Savepoints**: Full support via `SAVEPOINT name`, `RELEASE SAVEPOINT name`, `ROLLBACK TO SAVEPOINT name`
- **Isolation Levels**: READ UNCOMMITTED, READ COMMITTED, REPEATABLE READ, SERIALIZABLE, SNAPSHOT
- **Row-Level Locking**: `IteratorLocking.cs` applies locks during query iteration
- **Wait Modes**: WAIT (default), NOWAIT (immediate failure), SKIP LOCKED (skip locked rows)

---

## 10. Schema Information

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| INFORMATION_SCHEMA.TABLES | [ ] | P1 | v1 | SS13.1 |
| INFORMATION_SCHEMA.COLUMNS | [ ] | P1 | v1 | SS13.1 |
| INFORMATION_SCHEMA.KEY_COLUMN_USAGE | [ ] | P1 | v1 | SS13.1 |
| INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS | [ ] | P1 | v1 | SS13.1 |
| INFORMATION_SCHEMA.INDEXES | [ ] | P1 | v1 | SS13.1 |
| INFORMATION_SCHEMA.VIEWS | [ ] | P1 | v1 | SS13.1 |

---

## 11. ADO.NET Provider

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

## 12. Query Optimization

| Feature | Status | Priority | Version |
|---------|--------|----------|---------|
| Index selection | [ ] | P1 | v1 |
| Join ordering | [ ] | P1 | v1 |
| Predicate pushdown | [ ] | P1 | v1 |
| Query plan caching | [ ] | P1 | v1 |
| Statistics-based optimization | [ ] | P2 | v2 |
| EXPLAIN output | [ ] | P2 | v2 |

---

## 13. v2 Features (Deferred)

### 13.1 User-Defined Functions

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE FUNCTION` execution | [ ] | P2 | v2 | SS22.1 |
| `RETURNS TABLE` support | [ ] | P2 | v2 | SS22.2 |
| `DETERMINISTIC` handling | [ ] | P2 | v2 | SS22.1 |
| `DROP FUNCTION` execution | [ ] | P2 | v2 | SS22 |

### 13.2 Stored Procedures

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE PROCEDURE` execution | [ ] | P2 | v2 | SS23 |
| `DROP PROCEDURE` execution | [ ] | P2 | v2 | SS23 |
| `CALL` / `EXECUTE` execution | [ ] | P2 | v2 | SS23 |

### 13.3 Query Analysis

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `EXPLAIN` execution | [ ] | P2 | v2 | SS25.1 |
| `EXPLAIN ANALYZE` | [ ] | P2 | v2 | SS25.1 |
| `EXPLAIN (FORMAT JSON/TEXT)` | [ ] | P2 | v2 | SS25.1 |

### 13.4 Database Administration

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `DROP DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `ATTACH DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `DETACH DATABASE` | [ ] | P2 | v2 | SS26.1 |
| `VACUUM` execution | [ ] | P2 | v2 | SS26.2 |
| `ANALYZE` execution | [ ] | P2 | v2 | SS26.2 |
| `PRAGMA` support | [ ] | P2 | v2 | SS26.3 |

### 13.5 Cursor Support

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Scrollable cursors | [ ] | P2 | v2 | - |
| DECLARE CURSOR | [ ] | P2 | v2 | - |
| FETCH | [ ] | P2 | v2 | - |
| CLOSE CURSOR | [ ] | P2 | v2 | - |

### 13.6 Advanced Statistics

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Column cardinality estimation | [ ] | P2 | v2 | - |
| Histogram statistics | [ ] | P2 | v2 | - |

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

### Phase 2: JOINs and Advanced Queries - ? MOSTLY COMPLETE

**Goal:** Multi-table queries and subqueries

- [x] INNER JOIN, LEFT JOIN, RIGHT JOIN, FULL JOIN ?
- [x] CROSS JOIN ?
- [x] CREATE/DROP INDEX (metadata) ?
- [ ] Index usage in WHERE clauses
- [x] GROUP BY, HAVING ?
- [x] Subqueries (scalar, IN, EXISTS, ANY/ALL) ?
- [x] Correlated subqueries ?
- [ ] CTE (WITH clause)
- [x] UNION / UNION ALL / INTERSECT / EXCEPT ?

### Phase 3: Transactions and Concurrency - ? COMPLETE

**Goal:** Full transaction support

- [x] Transaction isolation levels ?
- [x] Savepoints ?
- [x] FOR UPDATE / FOR SHARE ?
- [ ] ROWVERSION support
- [ ] INSERT ... ON CONFLICT
- [ ] MERGE statement

### Phase 4: Production Ready (Current)

**Goal:** Production-ready engine

- [ ] **Index Implementation** (BLOCKING - P0)
- [ ] Window functions
- [ ] Recursive CTE
- [x] Views and triggers ?
- [ ] All remaining v1 functions
- [ ] INFORMATION_SCHEMA
- [ ] Basic query optimization

### Phase 5: Advanced Features (v2)

**Goal:** Advanced enterprise features

- [ ] User-defined functions
- [ ] Stored procedures
- [ ] EXPLAIN / EXPLAIN ANALYZE
- [ ] Database administration commands
- [ ] Scrollable cursors
- [ ] Advanced statistics and optimization

---

## Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| ExpressionEvaluator (all) | 194 | ? Passing |
| StatementExecutor | 162 | ? Passing |
| Iterators | 119 | ? Passing |
| QueryPlanner | 50 | ? Passing |
| WitSqlValue | 130 | ? Passing |
| Definitions | 90 | ? Passing |
| Schema | 50 | ? Passing |
| WitSqlEngine (integration) | 132 | ? Passing |
| StatementExecutorLockingTests | 17 | ? Passing |
| **Total** | **976** | ? Passing |

---

## Recent Changes

### 2025-01-28
- ? **Transaction Support Complete**:
  - BEGIN TRANSACTION / COMMIT / ROLLBACK SQL execution
  - Isolation level support (SET TRANSACTION ISOLATION LEVEL)
  - Savepoints (SAVEPOINT, RELEASE SAVEPOINT, ROLLBACK TO SAVEPOINT)
  - Transaction-aware table scans via `ITransaction.Scan()`
- ? **FOR UPDATE / FOR SHARE Locking**:
  - `IteratorLocking.cs` - applies row-level locks during iteration
  - `QueryPlanner.ApplyLockingClause()` - integrates locking into query plan
  - NOWAIT mode - immediate failure if lock unavailable
  - SKIP LOCKED mode - skip rows that are already locked
  - Requires MVCC transaction
- ? 46 new transaction and locking tests

### 2025-01-26
- ? Added full subquery support:
  - Scalar subqueries in SELECT list
  - EXISTS / NOT EXISTS
  - IN (subquery) / NOT IN (subquery)
  - ANY / SOME / ALL quantified comparisons
  - Correlated subqueries
- ? Fixed IteratorAlias to properly expose aliased column names
- ? Added ExpressionEvaluator.Subquery.cs (new partial class)
- ? Updated EvaluateColumnRef to support OuterRow for correlation
- ? Added 22 new subquery tests

---

**Last Updated:** 2025-01-28
