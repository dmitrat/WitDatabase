# OutWit.Database (Engine) - Roadmap

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
- **P0** = Critical (required for basic functionality)
- **P1** = Important (production-ready features)
- **P2** = Optional (nice-to-have features)

**Version Legend:**
- **v1** = First release
- **v2** = Deferred to second release

---

## Progress Summary

**Current Status: 0% - Not Started**

The Engine component (`OutWit.Database`) is responsible for:
- SQL execution against the Core storage layer
- Query planning and optimization
- Type system implementation
- Function evaluation
- ADO.NET provider implementation

---

## 1. Query Execution Infrastructure

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Query executor interface | [ ] | P0 | v1 | - |
| AST to execution plan converter | [ ] | P0 | v1 | - |
| Expression evaluator | [ ] | P0 | v1 | SS4 |
| Type coercion system | [ ] | P0 | v1 | SS1 |
| Result set builder | [ ] | P0 | v1 | - |
| Query context with AffectedRows, LastInsertId | [ ] | P0 | v1 | SS5.8 |
| Parameter binding | [ ] | P0 | v1 | SS11 |
| Query timeout support | [ ] | P0 | v1 | - |
| CancellationToken support | [ ] | P0 | v1 | - |

---

## 2. Data Type Implementation

### 2.1 Primitive Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `NULL` handling | [ ] | P0 | v1 | SS1.1 |
| `TINYINT` / `UTINYINT` | [ ] | P0 | v1 | SS1.2 |
| `SMALLINT` / `USMALLINT` | [ ] | P0 | v1 | SS1.2 |
| `INT` / `UINT` | [ ] | P0 | v1 | SS1.2 |
| `BIGINT` / `UBIGINT` | [ ] | P0 | v1 | SS1.2 |
| `FLOAT16` / `FLOAT` / `DOUBLE` | [ ] | P0 | v1 | SS1.3 |
| `DECIMAL` with precision/scale | [ ] | P0 | v1 | SS1.3 |
| `BOOLEAN` | [ ] | P0 | v1 | SS1.4 |

### 2.2 Date/Time Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `DATE` (DateOnly) | [ ] | P0 | v1 | SS1.5 |
| `TIME` (TimeOnly) | [ ] | P0 | v1 | SS1.5 |
| `DATETIME` | [ ] | P0 | v1 | SS1.5 |
| `DATETIMEOFFSET` | [ ] | P0 | v1 | SS1.5 |
| `INTERVAL` (TimeSpan) | [ ] | P0 | v1 | SS1.5 |

### 2.3 String/Binary Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `CHAR(n)` / `VARCHAR(n)` / `TEXT` | [ ] | P0 | v1 | SS1.7 |
| `BINARY(n)` / `VARBINARY(n)` / `BLOB` | [ ] | P0 | v1 | SS1.8 |
| UTF-8 encoding | [ ] | P0 | v1 | SS1.7 |

### 2.4 Special Types

| Type | Status | Priority | Version | Spec |
|------|--------|----------|---------|------|
| `GUID` | [ ] | P0 | v1 | SS1.6 |
| `ROWVERSION` | [ ] | P1 | v1 | SS15.1 |
| `JSON` / `JSONB` | [ ] | P1 | v1 | SS21.1 |

---

## 3. DDL Execution

### 3.1 Table Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE TABLE` execution | [ ] | P0 | v1 | SS2.1 |
| `CREATE TABLE IF NOT EXISTS` | [ ] | P0 | v1 | SS2.1 |
| Column constraints validation | [ ] | P0 | v1 | SS2.1 |
| Primary key handling | [ ] | P0 | v1 | SS2.1 |
| AUTOINCREMENT support | [ ] | P0 | v1 | SS2.1 |
| Foreign key constraints | [ ] | P1 | v1 | SS2.1 |
| CHECK constraints | [ ] | P1 | v1 | SS2.1 |
| DEFAULT values | [ ] | P0 | v1 | SS2.1 |
| `DROP TABLE` execution | [ ] | P0 | v1 | SS2.2 |
| `ALTER TABLE` execution | [ ] | P1 | v1 | SS2.3 |

### 3.2 Index Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE INDEX` execution | [ ] | P0 | v1 | SS2.4 |
| `CREATE UNIQUE INDEX` | [ ] | P0 | v1 | SS2.4 |
| Index building from existing data | [ ] | P0 | v1 | SS2.4 |
| Index auto-update on DML | [ ] | P0 | v1 | SS2.4 |
| `DROP INDEX` execution | [ ] | P0 | v1 | SS2.5 |
| Partial indexes | [ ] | P1 | v1 | SS19.1 |
| Expression indexes | [ ] | P1 | v1 | SS19.2 |
| Covering indexes | [ ] | P1 | v1 | SS19.3 |

### 3.3 View Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE VIEW` execution | [ ] | P1 | v1 | SS2.6 |
| View query substitution | [ ] | P1 | v1 | SS2.6 |
| `DROP VIEW` execution | [ ] | P1 | v1 | SS2.7 |

### 3.4 Trigger Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE TRIGGER` execution | [ ] | P1 | v1 | SS2.8 |
| BEFORE/AFTER/INSTEAD OF timing | [ ] | P1 | v1 | SS2.8 |
| OLD/NEW pseudo-tables | [ ] | P1 | v1 | SS2.8 |
| Trigger firing on DML | [ ] | P1 | v1 | SS2.8 |
| `DROP TRIGGER` execution | [ ] | P1 | v1 | SS2.9 |

### 3.5 Sequence Operations

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| `CREATE SEQUENCE` execution | [ ] | P0 | v1 | SS5.5 |
| `ALTER SEQUENCE` execution | [ ] | P0 | v1 | SS5.5 |
| `DROP SEQUENCE` execution | [ ] | P0 | v1 | SS5.5 |
| `INCREMENT()` function | [ ] | P0 | v1 | SS5.5 |
| `LASTINCREMENT()` function | [ ] | P0 | v1 | SS5.5 |

---

## 4. DML Execution

### 4.1 SELECT Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic SELECT from table | [ ] | P0 | v1 | SS3.1 |
| Column projection | [ ] | P0 | v1 | SS3.1 |
| WHERE filtering | [ ] | P0 | v1 | SS3.1 |
| Expression evaluation in SELECT | [ ] | P0 | v1 | SS3.1 |
| DISTINCT handling | [ ] | P0 | v1 | SS3.1 |
| ORDER BY sorting | [ ] | P0 | v1 | SS3.1 |
| LIMIT/OFFSET | [ ] | P0 | v1 | SS3.1 |
| GROUP BY aggregation | [ ] | P0 | v1 | SS3.1 |
| HAVING filtering | [ ] | P0 | v1 | SS3.1 |
| Table aliases | [ ] | P0 | v1 | SS3.1 |
| Subqueries in SELECT | [ ] | P0 | v1 | SS3.1 |
| Subqueries in FROM | [ ] | P0 | v1 | SS3.1 |
| Subqueries in WHERE | [ ] | P0 | v1 | SS3.1 |

### 4.2 JOIN Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| INNER JOIN | [ ] | P0 | v1 | SS3.1 |
| LEFT OUTER JOIN | [ ] | P0 | v1 | SS3.1 |
| RIGHT OUTER JOIN | [ ] | P1 | v1 | SS3.1 |
| FULL OUTER JOIN | [ ] | P1 | v1 | SS3.1 |
| CROSS JOIN | [ ] | P1 | v1 | SS3.1 |
| Multiple table joins | [ ] | P0 | v1 | SS3.1 |
| Join optimization (index usage) | [ ] | P1 | v1 | - |

### 4.3 INSERT Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic INSERT | [ ] | P0 | v1 | SS3.2 |
| INSERT with column list | [ ] | P0 | v1 | SS3.2 |
| Multi-row INSERT | [ ] | P0 | v1 | SS3.2 |
| INSERT ... SELECT | [ ] | P0 | v1 | SS3.2 |
| INSERT ... RETURNING | [ ] | P0 | v1 | SS3.2 |
| DEFAULT value handling | [ ] | P0 | v1 | SS3.2 |
| AUTOINCREMENT handling | [ ] | P0 | v1 | SS3.2 |
| Constraint validation | [ ] | P0 | v1 | SS3.2 |
| INSERT OR REPLACE | [ ] | P0 | v1 | SS16.1 |
| INSERT ... ON CONFLICT | [ ] | P0 | v1 | SS16.2 |

### 4.4 UPDATE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic UPDATE | [ ] | P0 | v1 | SS3.3 |
| UPDATE with WHERE | [ ] | P0 | v1 | SS3.3 |
| UPDATE ... RETURNING | [ ] | P0 | v1 | SS3.3 |
| Multi-column UPDATE | [ ] | P0 | v1 | SS3.3 |
| UPDATE with expressions | [ ] | P0 | v1 | SS3.3 |
| Index update on modification | [ ] | P0 | v1 | SS3.3 |
| UPDATE ... FROM | [ ] | P1 | v1 | SS17.2 |

### 4.5 DELETE Execution

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Basic DELETE | [ ] | P0 | v1 | SS3.4 |
| DELETE with WHERE | [ ] | P0 | v1 | SS3.4 |
| DELETE ... RETURNING | [ ] | P0 | v1 | SS3.4 |
| Index cleanup on delete | [ ] | P0 | v1 | SS3.4 |
| Cascading deletes | [ ] | P1 | v1 | SS2.1 |
| DELETE ... USING | [ ] | P1 | v1 | SS17.3 |

### 4.6 TRUNCATE / MERGE

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| TRUNCATE TABLE | [ ] | P0 | v1 | SS17.1 |
| MERGE execution | [ ] | P1 | v1 | SS16.3 |

---

## 5. Expression Evaluation

### 5.1 Operators

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| Comparison operators | [ ] | P0 | v1 | SS4.1 |
| Logical operators (AND, OR, NOT) | [ ] | P0 | v1 | SS4.2 |
| Arithmetic operators | [ ] | P0 | v1 | SS4.3 |
| String concatenation | [ ] | P0 | v1 | SS4.4 |
| Bitwise operators | [ ] | P1 | v1 | SS4.5 |
| BETWEEN evaluation | [ ] | P0 | v1 | SS4.1 |
| IN list evaluation | [ ] | P0 | v1 | SS4.1 |
| IN subquery evaluation | [ ] | P0 | v1 | SS4.1 |
| LIKE pattern matching | [ ] | P0 | v1 | SS4.1 |
| GLOB pattern matching | [ ] | P1 | v1 | SS4.1 |
| IS NULL / IS NOT NULL | [ ] | P0 | v1 | SS4.1 |

### 5.2 Conditional Expressions

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| CASE expression | [ ] | P0 | v1 | SS4.6 |
| COALESCE | [ ] | P0 | v1 | SS4.6 |
| NULLIF | [ ] | P0 | v1 | SS4.6 |
| IIF | [ ] | P0 | v1 | SS4.6 |
| CAST / type conversion | [ ] | P0 | v1 | SS4.6 |

### 5.3 Subquery Operators

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| EXISTS evaluation | [ ] | P0 | v1 | SS18.1 |
| NOT EXISTS evaluation | [ ] | P0 | v1 | SS18.1 |
| ANY / SOME evaluation | [ ] | P0 | v1 | SS18.2 |
| ALL evaluation | [ ] | P0 | v1 | SS18.2 |

---

## 6. Built-in Functions

### 6.1 Aggregate Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| COUNT(*) | [ ] | P0 | v1 | SS5.1 |
| COUNT(expr) | [ ] | P0 | v1 | SS5.1 |
| COUNT(DISTINCT expr) | [ ] | P0 | v1 | SS5.1 |
| SUM | [ ] | P0 | v1 | SS5.1 |
| AVG | [ ] | P0 | v1 | SS5.1 |
| MIN | [ ] | P0 | v1 | SS5.1 |
| MAX | [ ] | P0 | v1 | SS5.1 |
| GROUP_CONCAT | [ ] | P1 | v1 | SS5.1 |

### 6.2 String Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| LENGTH / CHAR_LENGTH | [ ] | P0 | v1 | SS5.2 |
| UPPER / LOWER | [ ] | P0 | v1 | SS5.2 |
| SUBSTR / SUBSTRING | [ ] | P0 | v1 | SS5.2 |
| LEFT / RIGHT | [ ] | P0 | v1 | SS5.2 |
| TRIM / LTRIM / RTRIM | [ ] | P0 | v1 | SS5.2 |
| REPLACE | [ ] | P0 | v1 | SS5.2 |
| INSTR / POSITION | [ ] | P0 | v1 | SS5.2 |
| CONCAT / CONCAT_WS | [ ] | P0 | v1 | SS5.2 |
| Other string functions | [ ] | P1 | v1 | SS5.2 |

### 6.3 Numeric Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| ABS | [ ] | P0 | v1 | SS5.3 |
| ROUND / FLOOR / CEIL | [ ] | P0 | v1 | SS5.3 |
| MOD | [ ] | P0 | v1 | SS5.3 |
| POWER / SQRT | [ ] | P1 | v1 | SS5.3 |
| Trigonometric functions | [ ] | P2 | v1 | SS5.3 |
| RANDOM | [ ] | P1 | v1 | SS5.3 |

### 6.4 Date/Time Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| NOW / CURRENT_TIMESTAMP | [ ] | P0 | v1 | SS5.4 |
| CURRENT_DATE / CURRENT_TIME | [ ] | P0 | v1 | SS5.4 |
| DATE / TIME extraction | [ ] | P0 | v1 | SS5.4 |
| YEAR / MONTH / DAY / HOUR / etc. | [ ] | P0 | v1 | SS5.4 |
| DATEADD / DATEDIFF | [ ] | P0 | v1 | SS5.4 |
| STRFTIME | [ ] | P1 | v1 | SS5.4 |

### 6.5 ID Generation Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| NEWGUID / NEWUUID | [ ] | P0 | v1 | SS5.5 |
| INCREMENT | [ ] | P0 | v1 | SS5.5 |
| LASTINCREMENT | [ ] | P0 | v1 | SS5.5 |

### 6.6 Conversion Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| CAST / CONVERT | [ ] | P0 | v1 | SS5.6 |
| TOSTRING / TOINT / etc. | [ ] | P1 | v1 | SS5.6 |
| HEX / UNHEX | [ ] | P1 | v1 | SS5.6 |
| BASE64 / UNBASE64 | [ ] | P1 | v1 | SS5.6 |

### 6.7 Null Handling Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| COALESCE | [ ] | P0 | v1 | SS5.7 |
| NULLIF | [ ] | P0 | v1 | SS5.7 |
| IFNULL / NVL | [ ] | P0 | v1 | SS5.7 |

### 6.8 System Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| DATABASE | [ ] | P1 | v1 | SS5.8 |
| VERSION | [ ] | P1 | v1 | SS5.8 |
| TYPEOF | [ ] | P1 | v1 | SS5.8 |
| CHANGES | [ ] | P0 | v1 | SS5.8 |
| LAST_INSERT_ROWID | [ ] | P0 | v1 | SS5.8 |

### 6.9 JSON Functions

| Function | Status | Priority | Version | Spec |
|----------|--------|----------|---------|------|
| JSON_VALUE / JSON_QUERY | [ ] | P1 | v1 | SS21.2 |
| JSON_EXTRACT | [ ] | P1 | v1 | SS21.2 |
| JSON_SET / JSON_INSERT | [ ] | P1 | v1 | SS21.2 |
| JSON_ARRAY / JSON_OBJECT | [ ] | P1 | v1 | SS21.2 |

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
| WITH clause execution | [ ] | P0 | v1 | SS6 |
| Multiple CTEs | [ ] | P0 | v1 | SS6 |
| Recursive CTE | [ ] | P1 | v1 | SS6 |
| UNION | [ ] | P0 | v1 | SS8 |
| UNION ALL | [ ] | P0 | v1 | SS8 |
| INTERSECT | [ ] | P1 | v1 | SS8 |
| EXCEPT | [ ] | P1 | v1 | SS8 |

---

## 9. Transaction Support

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| BEGIN TRANSACTION | [ ] | P0 | v1 | SS9 |
| COMMIT | [ ] | P0 | v1 | SS9 |
| ROLLBACK | [ ] | P0 | v1 | SS9 |
| SAVEPOINT | [ ] | P1 | v1 | SS9 |
| RELEASE SAVEPOINT | [ ] | P1 | v1 | SS9 |
| ROLLBACK TO SAVEPOINT | [ ] | P1 | v1 | SS9 |
| Isolation level support | [ ] | P0 | v1 | SS14.1 |
| FOR UPDATE / FOR SHARE | [ ] | P0 | v1 | SS14.2 |

---

## 10. Schema Information

| Feature | Status | Priority | Version | Spec |
|---------|--------|----------|---------|------|
| INFORMATION_SCHEMA.TABLES | [ ] | P0 | v1 | SS13.1 |
| INFORMATION_SCHEMA.COLUMNS | [ ] | P0 | v1 | SS13.1 |
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
| Index selection | [ ] | P0 | v1 |
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

### Phase 1: MVP (4-6 weeks) - v1

**Goal:** Basic SQL execution for simple queries

- [ ] Query executor infrastructure
- [ ] Type system for all basic types
- [ ] Basic SELECT with WHERE, ORDER BY, LIMIT
- [ ] INSERT, UPDATE, DELETE
- [ ] CREATE/DROP TABLE
- [ ] Primary key and basic constraints
- [ ] Basic expression evaluation
- [ ] Core aggregate functions (COUNT, SUM, AVG, MIN, MAX)
- [ ] Essential string/date functions
- [ ] ADO.NET provider basics

### Phase 2: JOINs and Indexes (3-4 weeks) - v1

**Goal:** Multi-table queries and performance

- [ ] INNER JOIN, LEFT JOIN
- [ ] CREATE/DROP INDEX
- [ ] Index usage in WHERE clauses
- [ ] GROUP BY, HAVING
- [ ] Subqueries (scalar, IN, EXISTS)
- [ ] CTE (WITH clause)
- [ ] UNION / UNION ALL

### Phase 3: Transactions and Concurrency (3-4 weeks) - v1

**Goal:** Full transaction support

- [ ] Transaction isolation levels
- [ ] Savepoints
- [ ] FOR UPDATE / FOR SHARE
- [ ] ROWVERSION support
- [ ] INSERT ... ON CONFLICT
- [ ] MERGE statement

### Phase 4: Production Ready (4-6 weeks) - v1

**Goal:** Production-ready engine

- [ ] Window functions
- [ ] Recursive CTE
- [ ] Views and triggers
- [ ] All remaining v1 functions
- [ ] INFORMATION_SCHEMA
- [ ] Basic query optimization
- [ ] JSON functions
- [ ] Collation support

### Phase 5: Advanced Features (Future) - v2

**Goal:** Advanced enterprise features

- [ ] User-defined functions
- [ ] Stored procedures
- [ ] EXPLAIN / EXPLAIN ANALYZE
- [ ] Database administration commands
- [ ] Scrollable cursors
- [ ] Advanced statistics and optimization

---

**Last Updated:** 2025-01-17
