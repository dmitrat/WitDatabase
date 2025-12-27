# WitDatabase - Roadmap v1

**Version:** 1.3  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-02-03

---

## Legend

| Symbol | Meaning |
|--------|---------|
| [x] | Implemented |
| [ ] | Not implemented |

**Priority Legend:**
- **P0** = Critical (required for ADO.NET/EF Core)
- **P1** = Important (production ready)

---

## Overall Progress Summary

| Component | Total Features | Implemented | Missing | Progress |
|-----------|----------------|-------------|---------|----------|
| **OutWit.Database.Core** | 70 | 70 | 0 | 100% |
| **OutWit.Database.Parser** | 290 | 290 | 0 | 100% |
| **OutWit.Database** (Engine) | 200+ | ~200 | ~5 | ~97% |

---

# Part 1: Data Types & DDL

## 1. Data Types (SS1)

### 1.1 Null Type

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `NULL` type | [x] | [x] | [x] | SS1.1 |

### 1.2 Integer Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `TINYINT` / `INT8` (sbyte) | [x] | [x] | [x] | SS1.2 |
| `UTINYINT` / `UINT8` (byte) | [x] | [x] | [x] | SS1.2 |
| `SMALLINT` / `INT16` (short) | [x] | [x] | [x] | SS1.2 |
| `USMALLINT` / `UINT16` (ushort) | [x] | [x] | [x] | SS1.2 |
| `INT` / `INT32` / `INTEGER` (int) | [x] | [x] | [x] | SS1.2 |
| `UINT` / `UINT32` (uint) | [x] | [x] | [x] | SS1.2 |
| `BIGINT` / `INT64` / `LONG` (long) | [x] | [x] | [x] | SS1.2 |
| `UBIGINT` / `UINT64` / `ULONG` (ulong) | [x] | [x] | [x] | SS1.2 |
| VarInt encoding | [x] | N/A | [x] | SS1.2 |

### 1.3 Floating-Point Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `FLOAT16` / `HALF` (Half) | [x] | [x] | [x] | SS1.3 |
| `FLOAT` / `FLOAT32` / `REAL` (float) | [x] | [x] | [x] | SS1.3 |
| `DOUBLE` / `FLOAT64` (double) | [x] | [x] | [x] | SS1.3 |
| `DECIMAL` / `MONEY` / `NUMERIC` | [x] | [x] | [x] | SS1.3 |
| `DECIMAL(precision, scale)` | [x] | [x] | [x] | SS13.2 |

### 1.4 Boolean Type

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BOOLEAN` / `BOOL` | [x] | [x] | [x] | SS1.4 |
| `TRUE` / `FALSE` literals | [x] | [x] | [x] | SS1.4 |

### 1.5 Date and Time Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DATE` / `DATEONLY` | [x] | [x] | [x] | SS1.5 |
| `TIME` / `TIMEONLY` | [x] | [x] | [x] | SS1.5 |
| `DATETIME` / `TIMESTAMP` | [x] | [x] | [x] | SS1.5 |
| `DATETIMEOFFSET` | [x] | [x] | [x] | SS1.5 |
| `INTERVAL` / `TIMESPAN` | [x] | [x] | [x] | SS1.5 |

### 1.6 Unique Identifier

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `GUID` / `UUID` / `UNIQUEIDENTIFIER` | [x] | [x] | [x] | SS1.6 |

### 1.7 String Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CHAR(n)` | [x] | [x] | [x] | SS1.7 |
| `VARCHAR(n)` | [x] | [x] | [x] | SS1.7 |
| `TEXT` | [x] | [x] | [x] | SS1.7 |
| `NCHAR(n)` / `NVARCHAR(n)` / `NTEXT` | [x] | [x] | [x] | SS1.7 |

### 1.8 Binary Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BINARY(n)` | [x] | [x] | [x] | SS1.8 |
| `VARBINARY(n)` | [x] | [x] | [x] | SS1.8 |
| `BLOB` | [x] | [x] | [x] | SS1.8 |

### 1.9 Special Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `ROWVERSION` | [x] | [x] | [ ] | SS15.1 |
| `JSON` / `JSONB` | N/A | [x] | [x] | SS21.1 |

---

## 2. DDL - CREATE TABLE (SS2.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE TABLE table_name (...)` | N/A | [x] | [x] | SS2.1 |
| `CREATE TABLE IF NOT EXISTS` | N/A | [x] | [x] | SS2.1 |
| `NOT NULL` / `NULL` | N/A | [x] | [x] | SS2.1 |
| `PRIMARY KEY` | N/A | [x] | [x] | SS2.1 |
| `PRIMARY KEY AUTOINCREMENT` | N/A | [x] | [x] | SS2.1 |
| `UNIQUE` | N/A | [x] | [x] | SS2.1 |
| `DEFAULT literal_value` | N/A | [x] | [x] | SS2.1 |
| `DEFAULT (expression)` | N/A | [x] | [x] | SS2.1 |
| `CHECK (expression)` | N/A | [x] | [x] | SS2.1 |
| `REFERENCES table(col)` | N/A | [x] | [x] | SS2.1 |
| `ON DELETE/UPDATE action` | N/A | [x] | [ ] | SS2.1 |
| `PRIMARY KEY (column_list)` | N/A | [x] | [x] | SS2.1 |
| `UNIQUE (column_list)` | N/A | [x] | [x] | SS2.1 |
| `FOREIGN KEY (cols) REFERENCES ...` | N/A | [x] | [x] | SS2.1 |
| `CONSTRAINT name ...` | N/A | [x] | [x] | SS13.3 |
| Computed columns `AS (expr)` | N/A | [x] | [x] | SS20 |
| `STORED` / `VIRTUAL` modifiers | N/A | [x] | [x] | SS20 |

## 3. DDL - DROP/ALTER TABLE (SS2.2-2.3)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DROP TABLE table_name` | N/A | [x] | [x] | SS2.2 |
| `DROP TABLE IF EXISTS` | N/A | [x] | [x] | SS2.2 |
| `ALTER TABLE ... ADD [COLUMN]` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... ADD CONSTRAINT` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... DROP [COLUMN]` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... DROP CONSTRAINT` | N/A | [x] | [x] | SS13.3 |
| `ALTER TABLE ... RENAME TO` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... RENAME COLUMN` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... SET/DROP DEFAULT` | N/A | [x] | [x] | SS2.3 |
| `ALTER TABLE ... SET/DROP NOT NULL` | N/A | [x] | [x] | SS2.3 |

## 4. DDL - INDEX (SS2.4-2.5)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE INDEX name ON table (cols)` | [x] | [x] | [x] | SS2.4 |
| `CREATE UNIQUE INDEX` | [x] | [x] | [x] | SS2.4 |
| `CREATE INDEX IF NOT EXISTS` | [x] | [x] | [x] | SS2.4 |
| `ASC` / `DESC` column order | [x] | [x] | [x] | SS2.4 |
| `DROP INDEX [IF EXISTS]` | N/A | [x] | [x] | SS2.5 |
| `WHERE condition` (partial index) | [x] | [x] | [x] | SS19.1 |
| Expression indexes `(LOWER(col))` | [x] | [x] | [x] | SS19.2 |
| `INCLUDE (cols)` (covering index) | [x] | [x] | [x] | SS19.3 |

## 5. DDL - VIEW (SS2.6-2.7)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE VIEW name AS SELECT` | N/A | [x] | [x] | SS2.6 |
| `CREATE VIEW IF NOT EXISTS` | N/A | [x] | [x] | SS2.6 |
| `CREATE VIEW name (cols) AS` | N/A | [x] | [x] | SS2.6 |
| `DROP VIEW [IF EXISTS]` | N/A | [x] | [x] | SS2.7 |

## 6. DDL - TRIGGER (SS2.8-2.9)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE TRIGGER BEFORE/AFTER/INSTEAD OF` | N/A | [x] | [x] | SS2.8 |
| `CREATE TRIGGER IF NOT EXISTS` | N/A | [x] | [x] | SS2.8 |
| `INSERT` / `UPDATE` / `DELETE` events | N/A | [x] | [x] | SS2.8 |
| `UPDATE OF col_list` | N/A | [x] | [x] | SS2.8 |
| `FOR EACH ROW` | N/A | [x] | [x] | SS2.8 |
| `WHEN (condition)` | N/A | [x] | [x] | SS2.8 |
| `BEGIN ... END` body | N/A | [x] | [x] | SS2.8 |
| `OLD.column` / `NEW.column` | N/A | [x] | [x] | SS2.8 |
| `SIGNAL SQLSTATE` | N/A | [x] | [ ] | SS2.8 |
| `DROP TRIGGER [IF EXISTS]` | N/A | [x] | [x] | SS2.9 |

## 7. DDL - SEQUENCE (SS5.5)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE SEQUENCE name START WITH n` | N/A | [x] | [x] | SS5.5 |
| `ALTER SEQUENCE name RESTART WITH n` | N/A | [x] | [x] | SS5.5 |
| `DROP SEQUENCE name` | N/A | [x] | [x] | SS5.5 |
| `INCREMENT(sequence)` function | N/A | [x] | [x] | SS5.5 |
| `LASTINCREMENT(sequence)` function | N/A | [x] | [x] | SS5.5 |

---

# Part 2: DML Statements

## 8. SELECT (SS3.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `SELECT *` / `SELECT column_list` | N/A | [x] | [x] | SS3.1 |
| `SELECT expression AS alias` | N/A | [x] | [x] | SS3.1 |
| `SELECT DISTINCT` / `SELECT ALL` | N/A | [x] | [x] | SS3.1 |
| `FROM table_name [AS alias]` | N/A | [x] | [x] | SS3.1 |
| `FROM (subquery) AS alias` | N/A | [x] | [x] | SS3.1 |
| `INNER JOIN ... ON` | N/A | [x] | [x] | SS3.1 |
| `LEFT/RIGHT/FULL [OUTER] JOIN` | N/A | [x] | [x] | SS3.1 |
| `CROSS JOIN` | N/A | [x] | [x] | SS3.1 |
| `WHERE condition` | N/A | [x] | [x] | SS3.1 |
| `GROUP BY expression` | N/A | [x] | [x] | SS3.1 |
| `HAVING condition` | N/A | [x] | [x] | SS3.1 |
| `ORDER BY expr [ASC/DESC]` | N/A | [x] | [x] | SS3.1 |
| `ORDER BY ... NULLS FIRST/LAST` | N/A | [x] | [x] | SS3.1 |
| `LIMIT count [OFFSET offset]` | N/A | [x] | [x] | SS3.1 |
| `FOR UPDATE` | [x] | [x] | [x] | SS14.2 |
| `FOR SHARE` | [x] | [x] | [x] | SS14.2 |
| `FOR UPDATE NOWAIT` | [x] | [x] | [x] | SS14.2 |
| `FOR UPDATE SKIP LOCKED` | [x] | [x] | [x] | SS14.2 |

## 9. INSERT (SS3.2)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `INSERT INTO table (cols) VALUES (...)` | N/A | [x] | [x] | SS3.2 |
| Multi-row `VALUES (...), (...)` | N/A | [x] | [x] | SS3.2 |
| `INSERT INTO ... SELECT` | N/A | [x] | [x] | SS3.2 |
| `INSERT ... RETURNING` | N/A | [x] | [x] | SS3.2 |
| `INSERT OR REPLACE` | N/A | [x] | [x] | SS16.1 |
| `INSERT ... ON CONFLICT DO UPDATE` | N/A | [x] | [x] | SS16.2 |
| `INSERT ... ON CONFLICT DO NOTHING` | N/A | [x] | [x] | SS16.2 |
| `EXCLUDED.column` reference | N/A | [x] | [x] | SS16 |

## 10. UPDATE (SS3.3)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `UPDATE table SET col = expr` | N/A | [x] | [x] | SS3.3 |
| `UPDATE ... WHERE condition` | N/A | [x] | [x] | SS3.3 |
| `UPDATE ... RETURNING` | N/A | [x] | [x] | SS3.3 |
| `UPDATE ... FROM other_table` | N/A | [x] | [ ] | SS17.2 |
| `UPDATE table AS alias` | N/A | [x] | [x] | SS3.3 |

## 11. DELETE (SS3.4)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DELETE FROM table` | N/A | [x] | [x] | SS3.4 |
| `DELETE FROM ... WHERE` | N/A | [x] | [x] | SS3.4 |
| `DELETE FROM ... RETURNING` | N/A | [x] | [x] | SS3.4 |
| `DELETE ... USING other_table` | N/A | [x] | [ ] | SS17.3 |
| `DELETE FROM table AS alias` | N/A | [x] | [x] | SS3.4 |

## 12. TRUNCATE / MERGE (SS16-17) ? COMPLETE

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `TRUNCATE TABLE table_name` | N/A | [x] | [x] | SS17.1 |
| `MERGE INTO target USING source ON` | N/A | [x] | [x] | SS16.3 |
| `WHEN MATCHED THEN UPDATE` | N/A | [x] | [x] | SS16.3 |
| `WHEN MATCHED THEN DELETE` | N/A | [x] | [x] | SS16.3 |
| `WHEN NOT MATCHED THEN INSERT` | N/A | [x] | [x] | SS16.3 |
| `WHEN MATCHED AND condition` | N/A | [x] | [x] | SS16.3 |
| MERGE with subquery source | N/A | [x] | [x] | SS16.3 |

## 13. CTE / Set Operations (SS6, SS8) ? COMPLETE

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `WITH cte_name AS (SELECT ...)` | N/A | [x] | [x] | SS6 |
| `WITH cte_name (cols) AS (...)` | N/A | [x] | [x] | SS6 |
| Multiple CTEs | N/A | [x] | [x] | SS6 |
| `WITH RECURSIVE ...` | N/A | [x] | [x] | SS6 |
| CTE Caching | N/A | N/A | [x] | SS6 |
| `UNION` / `UNION ALL` | N/A | [x] | [x] | SS8 |
| `INTERSECT` / `EXCEPT` | N/A | [x] | [x] | SS8 |

## 14. Subqueries (SS18)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| Scalar subquery in SELECT | N/A | [x] | [x] | SS3.1 |
| Table subquery in FROM | N/A | [x] | [x] | SS3.1 |
| Subquery with `IN` | N/A | [x] | [x] | SS3.1 |
| `EXISTS (subquery)` | N/A | [x] | [x] | SS18.1 |
| `NOT EXISTS (subquery)` | N/A | [x] | [x] | SS18.1 |
| `expression > ANY (subquery)` | N/A | [x] | [x] | SS18.2 |
| `expression > SOME (subquery)` | N/A | [x] | [x] | SS18.2 |
| `expression > ALL (subquery)` | N/A | [x] | [x] | SS18.2 |

---

# Part 3: Expressions and Operators

(All expression and operator features marked as [x] for Engine - fully implemented)

---

# Part 4: Built-in Functions

## 25. JSON Functions (SS21.2) ? COMPLETE

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `JSON_EXTRACT(json, path)` | N/A | [x] | [x] | SS21.2 |
| `JSON_VALUE(json, path)` | N/A | [x] | [x] | SS21.2 |
| `JSON_QUERY(json, path)` | N/A | [x] | [x] | SS21.2 |
| `JSON_SET(json, path, value)` | N/A | [x] | [x] | SS21.2 |
| `JSON_INSERT(json, path, value)` | N/A | [x] | [x] | SS21.2 |
| `JSON_REPLACE(json, path, value)` | N/A | [x] | [x] | SS21.2 |
| `JSON_REMOVE(json, path)` | N/A | [x] | [x] | SS21.2 |
| `JSON_TYPE(json)` | N/A | [x] | [x] | SS21.2 |
| `JSON_ARRAY_LENGTH(json)` | N/A | [x] | [x] | SS21.2 |
| `JSON_VALID(str)` | N/A | [x] | [x] | SS21.2 |
| `JSON_ARRAY(values...)` | N/A | [x] | [x] | SS21.2 |
| `JSON_OBJECT(key1, val1, ...)` | N/A | [x] | [x] | SS21.2 |

---

# Part 5: Window Functions, Transactions

## 29. Window Functions (SS7) ? COMPLETE

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `OVER ()` | N/A | [x] | [x] | SS7 |
| `OVER (PARTITION BY ...)` | N/A | [x] | [x] | SS7 |
| `OVER (ORDER BY ...)` | N/A | [x] | [x] | SS7 |
| `ROWS/RANGE frame_clause` | N/A | [x] | [ ] | SS7 |
| `UNBOUNDED PRECEDING/FOLLOWING` | N/A | [x] | [ ] | SS7 |
| `n PRECEDING/FOLLOWING`, `CURRENT ROW` | N/A | [x] | [ ] | SS7 |
| `ROW_NUMBER()` | N/A | [x] | [x] | SS7.1 |
| `RANK()` | N/A | [x] | [x] | SS7.1 |
| `DENSE_RANK()` | N/A | [x] | [x] | SS7.1 |
| `NTILE(n)` | N/A | [x] | [x] | SS7.1 |
| `PERCENT_RANK()` | N/A | [x] | [x] | SS7.1 |
| `CUME_DIST()` | N/A | [x] | [x] | SS7.1 |
| `FIRST_VALUE` | N/A | [x] | [x] | SS7.2 |
| `LAST_VALUE` | N/A | [x] | [x] | SS7.2 |
| `NTH_VALUE` | N/A | [x] | [x] | SS7.2 |
| `LAG` | N/A | [x] | [x] | SS7.2 |
| `LEAD` | N/A | [x] | [x] | SS7.2 |
| Aggregate window functions (SUM, AVG, COUNT, MIN, MAX OVER) | N/A | [x] | [x] | SS7 |

**Note:** Frame clause (ROWS/RANGE BETWEEN) deferred to v2. Current implementation uses entire partition for aggregate window functions.

## 30. Transactions (SS9)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BEGIN [TRANSACTION]` | [x] | [x] | [x] | SS9 |
| `COMMIT` | [x] | [x] | [x] | SS9 |
| `ROLLBACK` | [x] | [x] | [x] | SS9 |
| `SAVEPOINT name` | [x] | [x] | [x] | SS9 |
| `RELEASE SAVEPOINT name` | [x] | [x] | [x] | SS9 |
| `ROLLBACK TO SAVEPOINT name` | [x] | [x] | [x] | SS9 |
| `SET TRANSACTION ISOLATION LEVEL` | [x] | [x] | [x] | SS14.1 |
| Isolation level keywords | [x] | [x] | [x] | SS14.1 |

## 31. INFORMATION_SCHEMA (SS13.1) - COMPLETE ?

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `INFORMATION_SCHEMA.TABLES` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.COLUMNS` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.KEY_COLUMN_USAGE` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.TABLE_CONSTRAINTS` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.INDEXES` | N/A | N/A | [x] | SS13.1 |
| `INFORMATION_SCHEMA.VIEWS` | N/A | N/A | [x] | SS13.1 |

## 32. Query Optimization - COMPLETE ?

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| Index selection (cost-based) | N/A | N/A | [x] | - |
| Query plan caching | N/A | N/A | [x] | - |
| Join order optimization | N/A | N/A | [x] | - |
| Predicate pushdown | N/A | N/A | [x] | - |
