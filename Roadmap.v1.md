# WitDatabase - Roadmap v1

**Version:** 1.0  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-01-17

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
| **OutWit.Database** (Engine) | 200+ | 0 | 200+ | 0% |

---

# Part 1: Data Types & DDL

## 1. Data Types (SS1)

### 1.1 Null Type

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `NULL` type | [x] | [x] | [ ] | SS1.1 |

### 1.2 Integer Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `TINYINT` / `INT8` (sbyte) | [x] | [x] | [ ] | SS1.2 |
| `UTINYINT` / `UINT8` (byte) | [x] | [x] | [ ] | SS1.2 |
| `SMALLINT` / `INT16` (short) | [x] | [x] | [ ] | SS1.2 |
| `USMALLINT` / `UINT16` (ushort) | [x] | [x] | [ ] | SS1.2 |
| `INT` / `INT32` / `INTEGER` (int) | [x] | [x] | [ ] | SS1.2 |
| `UINT` / `UINT32` (uint) | [x] | [x] | [ ] | SS1.2 |
| `BIGINT` / `INT64` / `LONG` (long) | [x] | [x] | [ ] | SS1.2 |
| `UBIGINT` / `UINT64` / `ULONG` (ulong) | [x] | [x] | [ ] | SS1.2 |
| VarInt encoding | [x] | N/A | [ ] | SS1.2 |

### 1.3 Floating-Point Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `FLOAT16` / `HALF` (Half) | [x] | [x] | [ ] | SS1.3 |
| `FLOAT` / `FLOAT32` / `REAL` (float) | [x] | [x] | [ ] | SS1.3 |
| `DOUBLE` / `FLOAT64` (double) | [x] | [x] | [ ] | SS1.3 |
| `DECIMAL` / `MONEY` / `NUMERIC` | [x] | [x] | [ ] | SS1.3 |
| `DECIMAL(precision, scale)` | [x] | [x] | [ ] | SS13.2 |

### 1.4 Boolean Type

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BOOLEAN` / `BOOL` | [x] | [x] | [ ] | SS1.4 |
| `TRUE` / `FALSE` literals | [x] | [x] | [ ] | SS1.4 |

### 1.5 Date and Time Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DATE` / `DATEONLY` | [x] | [x] | [ ] | SS1.5 |
| `TIME` / `TIMEONLY` | [x] | [x] | [ ] | SS1.5 |
| `DATETIME` / `TIMESTAMP` | [x] | [x] | [ ] | SS1.5 |
| `DATETIMEOFFSET` | [x] | [x] | [ ] | SS1.5 |
| `INTERVAL` / `TIMESPAN` | [x] | [x] | [ ] | SS1.5 |

### 1.6 Unique Identifier

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `GUID` / `UUID` / `UNIQUEIDENTIFIER` | [x] | [x] | [ ] | SS1.6 |

### 1.7 String Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CHAR(n)` | [x] | [x] | [ ] | SS1.7 |
| `VARCHAR(n)` | [x] | [x] | [ ] | SS1.7 |
| `TEXT` | [x] | [x] | [ ] | SS1.7 |
| `NCHAR(n)` / `NVARCHAR(n)` / `NTEXT` | [x] | [x] | [ ] | SS1.7 |

### 1.8 Binary Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BINARY(n)` | [x] | [x] | [ ] | SS1.8 |
| `VARBINARY(n)` | [x] | [x] | [ ] | SS1.8 |
| `BLOB` | [x] | [x] | [ ] | SS1.8 |

### 1.9 Special Types

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `ROWVERSION` | [x] | [x] | [ ] | SS15.1 |
| `JSON` / `JSONB` | N/A | [x] | [ ] | SS21.1 |

---

## 2. DDL - CREATE TABLE (SS2.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE TABLE table_name (...)` | N/A | [x] | [ ] | SS2.1 |
| `CREATE TABLE IF NOT EXISTS` | N/A | [x] | [ ] | SS2.1 |
| `NOT NULL` / `NULL` | N/A | [x] | [ ] | SS2.1 |
| `PRIMARY KEY` | N/A | [x] | [ ] | SS2.1 |
| `PRIMARY KEY AUTOINCREMENT` | N/A | [x] | [ ] | SS2.1 |
| `UNIQUE` | N/A | [x] | [ ] | SS2.1 |
| `DEFAULT literal_value` | N/A | [x] | [ ] | SS2.1 |
| `DEFAULT (expression)` | N/A | [x] | [ ] | SS2.1 |
| `CHECK (expression)` | N/A | [x] | [ ] | SS2.1 |
| `REFERENCES table(col)` | N/A | [x] | [ ] | SS2.1 |
| `ON DELETE/UPDATE action` | N/A | [x] | [ ] | SS2.1 |
| `PRIMARY KEY (column_list)` | N/A | [x] | [ ] | SS2.1 |
| `UNIQUE (column_list)` | N/A | [x] | [ ] | SS2.1 |
| `FOREIGN KEY (cols) REFERENCES ...` | N/A | [x] | [ ] | SS2.1 |
| `CONSTRAINT name ...` | N/A | [x] | [ ] | SS13.3 |
| Computed columns `AS (expr)` | N/A | [x] | [ ] | SS20 |
| `STORED` / `VIRTUAL` modifiers | N/A | [x] | [ ] | SS20 |

## 3. DDL - DROP/ALTER TABLE (SS2.2-2.3)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DROP TABLE table_name` | N/A | [x] | [ ] | SS2.2 |
| `DROP TABLE IF EXISTS` | N/A | [x] | [ ] | SS2.2 |
| `ALTER TABLE ... ADD [COLUMN]` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... ADD CONSTRAINT` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... DROP [COLUMN]` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... DROP CONSTRAINT` | N/A | [x] | [ ] | SS13.3 |
| `ALTER TABLE ... RENAME TO` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... RENAME COLUMN` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... SET/DROP DEFAULT` | N/A | [x] | [ ] | SS2.3 |
| `ALTER TABLE ... SET/DROP NOT NULL` | N/A | [x] | [ ] | SS2.3 |

## 4. DDL - INDEX (SS2.4-2.5)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE INDEX name ON table (cols)` | [x] | [x] | [ ] | SS2.4 |
| `CREATE UNIQUE INDEX` | [x] | [x] | [ ] | SS2.4 |
| `CREATE INDEX IF NOT EXISTS` | [x] | [x] | [ ] | SS2.4 |
| `ASC` / `DESC` column order | [x] | [x] | [ ] | SS2.4 |
| `DROP INDEX [IF EXISTS]` | N/A | [x] | [ ] | SS2.5 |
| `WHERE condition` (partial index) | [x] | [x] | [ ] | SS19.1 |
| Expression indexes `(LOWER(col))` | [x] | [x] | [ ] | SS19.2 |
| `INCLUDE (cols)` (covering index) | [x] | [x] | [ ] | SS19.3 |

## 5. DDL - VIEW (SS2.6-2.7)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE VIEW name AS SELECT` | N/A | [x] | [ ] | SS2.6 |
| `CREATE VIEW IF NOT EXISTS` | N/A | [x] | [ ] | SS2.6 |
| `CREATE VIEW name (cols) AS` | N/A | [x] | [ ] | SS2.6 |
| `DROP VIEW [IF EXISTS]` | N/A | [x] | [ ] | SS2.7 |

## 6. DDL - TRIGGER (SS2.8-2.9)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE TRIGGER BEFORE/AFTER/INSTEAD OF` | N/A | [x] | [ ] | SS2.8 |
| `CREATE TRIGGER IF NOT EXISTS` | N/A | [x] | [ ] | SS2.8 |
| `INSERT` / `UPDATE` / `DELETE` events | N/A | [x] | [ ] | SS2.8 |
| `UPDATE OF col_list` | N/A | [x] | [ ] | SS2.8 |
| `FOR EACH ROW` | N/A | [x] | [ ] | SS2.8 |
| `WHEN (condition)` | N/A | [x] | [ ] | SS2.8 |
| `BEGIN ... END` body | N/A | [x] | [ ] | SS2.8 |
| `OLD.column` / `NEW.column` | N/A | [x] | [ ] | SS2.8 |
| `SIGNAL SQLSTATE` | N/A | [x] | [ ] | SS2.8 |
| `DROP TRIGGER [IF EXISTS]` | N/A | [x] | [ ] | SS2.9 |

## 7. DDL - SEQUENCE (SS5.5)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CREATE SEQUENCE name START WITH n` | N/A | [x] | [ ] | SS5.5 |
| `ALTER SEQUENCE name RESTART WITH n` | N/A | [x] | [ ] | SS5.5 |
| `DROP SEQUENCE name` | N/A | [x] | [ ] | SS5.5 |
| `INCREMENT(sequence)` function | N/A | [x] | [ ] | SS5.5 |
| `LASTINCREMENT(sequence)` function | N/A | [x] | [ ] | SS5.5 |

---

# Part 2: DML Statements

## 8. SELECT (SS3.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `SELECT *` / `SELECT column_list` | N/A | [x] | [ ] | SS3.1 |
| `SELECT expression AS alias` | N/A | [x] | [ ] | SS3.1 |
| `SELECT DISTINCT` / `SELECT ALL` | N/A | [x] | [ ] | SS3.1 |
| `FROM table_name [AS alias]` | N/A | [x] | [ ] | SS3.1 |
| `FROM (subquery) AS alias` | N/A | [x] | [ ] | SS3.1 |
| `INNER JOIN ... ON` | N/A | [x] | [ ] | SS3.1 |
| `LEFT/RIGHT/FULL [OUTER] JOIN` | N/A | [x] | [ ] | SS3.1 |
| `CROSS JOIN` | N/A | [x] | [ ] | SS3.1 |
| `WHERE condition` | N/A | [x] | [ ] | SS3.1 |
| `GROUP BY expression` | N/A | [x] | [ ] | SS3.1 |
| `HAVING condition` | N/A | [x] | [ ] | SS3.1 |
| `ORDER BY expr [ASC/DESC]` | N/A | [x] | [ ] | SS3.1 |
| `ORDER BY ... NULLS FIRST/LAST` | N/A | [x] | [ ] | SS3.1 |
| `LIMIT count [OFFSET offset]` | N/A | [x] | [ ] | SS3.1 |
| `FOR UPDATE` | [x] | [x] | [ ] | SS14.2 |
| `FOR SHARE` | [x] | [x] | [ ] | SS14.2 |
| `FOR UPDATE NOWAIT` | [x] | [x] | [ ] | SS14.2 |
| `FOR UPDATE SKIP LOCKED` | [x] | [x] | [ ] | SS14.2 |

## 9. INSERT (SS3.2)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `INSERT INTO table (cols) VALUES (...)` | N/A | [x] | [ ] | SS3.2 |
| Multi-row `VALUES (...), (...)` | N/A | [x] | [ ] | SS3.2 |
| `INSERT INTO ... SELECT` | N/A | [x] | [ ] | SS3.2 |
| `INSERT ... RETURNING` | N/A | [x] | [ ] | SS3.2 |
| `INSERT OR REPLACE` | N/A | [x] | [ ] | SS16.1 |
| `INSERT ... ON CONFLICT DO UPDATE` | N/A | [x] | [ ] | SS16.2 |
| `INSERT ... ON CONFLICT DO NOTHING` | N/A | [x] | [ ] | SS16.2 |
| `EXCLUDED.column` reference | N/A | [x] | [ ] | SS16 |

## 10. UPDATE (SS3.3)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `UPDATE table SET col = expr` | N/A | [x] | [ ] | SS3.3 |
| `UPDATE ... WHERE condition` | N/A | [x] | [ ] | SS3.3 |
| `UPDATE ... RETURNING` | N/A | [x] | [ ] | SS3.3 |
| `UPDATE ... FROM other_table` | N/A | [x] | [ ] | SS17.2 |
| `UPDATE table AS alias` | N/A | [x] | [ ] | SS3.3 |

## 11. DELETE (SS3.4)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `DELETE FROM table` | N/A | [x] | [ ] | SS3.4 |
| `DELETE FROM ... WHERE` | N/A | [x] | [ ] | SS3.4 |
| `DELETE FROM ... RETURNING` | N/A | [x] | [ ] | SS3.4 |
| `DELETE ... USING other_table` | N/A | [x] | [ ] | SS17.3 |
| `DELETE FROM table AS alias` | N/A | [x] | [ ] | SS3.4 |

## 12. TRUNCATE / MERGE (SS16-17)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `TRUNCATE TABLE table_name` | N/A | [x] | [ ] | SS17.1 |
| `MERGE INTO target USING source ON` | N/A | [x] | [ ] | SS16.3 |
| `WHEN MATCHED THEN UPDATE` | N/A | [x] | [ ] | SS16.3 |
| `WHEN NOT MATCHED THEN INSERT` | N/A | [x] | [ ] | SS16.3 |

## 13. CTE / Set Operations (SS6, SS8)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `WITH cte_name AS (SELECT ...)` | N/A | [x] | [ ] | SS6 |
| `WITH cte_name (cols) AS (...)` | N/A | [x] | [ ] | SS6 |
| Multiple CTEs | N/A | [x] | [ ] | SS6 |
| `WITH RECURSIVE ...` | N/A | [x] | [ ] | SS6 |
| `UNION` / `UNION ALL` | N/A | [x] | [ ] | SS8 |
| `INTERSECT` / `EXCEPT` | N/A | [x] | [ ] | SS8 |

## 14. Subqueries (SS18)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| Scalar subquery in SELECT | N/A | [x] | [ ] | SS3.1 |
| Table subquery in FROM | N/A | [x] | [ ] | SS3.1 |
| Subquery with `IN` | N/A | [x] | [ ] | SS3.1 |
| `EXISTS (subquery)` | N/A | [x] | [ ] | SS18.1 |
| `NOT EXISTS (subquery)` | N/A | [x] | [ ] | SS18.1 |
| `expression > ANY (subquery)` | N/A | [x] | [ ] | SS18.2 |
| `expression > SOME (subquery)` | N/A | [x] | [ ] | SS18.2 |
| `expression > ALL (subquery)` | N/A | [x] | [ ] | SS18.2 |

---

# Part 3: Expressions and Operators

## 15. Comparison Operators (SS4.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `=`, `<>`, `!=` | N/A | [x] | [ ] | SS4.1 |
| `<`, `<=`, `>`, `>=` | N/A | [x] | [ ] | SS4.1 |
| `IS NULL` / `IS NOT NULL` | N/A | [x] | [ ] | SS4.1 |
| `BETWEEN x AND y` | N/A | [x] | [ ] | SS4.1 |
| `IN (...)` / `NOT IN (...)` | N/A | [x] | [ ] | SS4.1 |
| `LIKE pattern` / `NOT LIKE` | N/A | [x] | [ ] | SS4.1 |
| `LIKE ... ESCAPE char` | N/A | [x] | [ ] | SS4.1 |
| `GLOB pattern` | N/A | [x] | [ ] | SS4.1 |

## 16. Logical Operators (SS4.2)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `AND` / `OR` / `NOT` | N/A | [x] | [ ] | SS4.2 |
| Precedence: NOT > AND > OR | N/A | [x] | [ ] | SS4.2 |

## 17. Arithmetic Operators (SS4.3)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `+`, `-`, `*`, `/`, `%` | N/A | [x] | [ ] | SS4.3 |
| Unary `-expr`, `+expr` | N/A | [x] | [ ] | SS4.3 |

## 18. String / Bitwise Operators (SS4.4-4.5)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `||` concatenation | N/A | [x] | [ ] | SS4.4 |
| `&`, `|`, `~`, `<<`, `>>` | N/A | [x] | [ ] | SS4.5 |

## 19. Conditional Expressions (SS4.6)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `CASE expr WHEN val THEN result END` | N/A | [x] | [ ] | SS4.6 |
| `CASE WHEN cond THEN result END` | N/A | [x] | [ ] | SS4.6 |
| `COALESCE(...)` | N/A | [x] | [ ] | SS4.6 |
| `NULLIF(a, b)` | N/A | [x] | [ ] | SS4.6 |
| `IIF(cond, true_val, false_val)` | N/A | [x] | [ ] | SS4.6 |
| `CAST(expr AS type)` | N/A | [x] | [ ] | SS4.6 |

## 20. Literals & Parameters (SS1, SS11)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| Integer / Float literals | N/A | [x] | [ ] | SS1.2-1.3 |
| String literals `'text'` | N/A | [x] | [ ] | SS1.7 |
| `TRUE` / `FALSE` / `NULL` | N/A | [x] | [ ] | SS1.1, SS1.4 |
| Hex blob `X'...'` | N/A | [x] | [ ] | SS1.8 |
| Named parameters `@param`, `:param` | N/A | [x] | [ ] | SS11 |
| Positional `?`, `$1`, `$2` | N/A | [x] | [ ] | SS11 |

## 21. Collation (SS24)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `COLLATE collation_name` in column | N/A | [x] | [ ] | SS24 |
| `COLLATE` in expression | N/A | [x] | [ ] | SS24 |
| `COLLATE` in ORDER BY | N/A | [x] | [ ] | SS24 |
| `BINARY`, `NOCASE`, `UNICODE`, `UNICODE_CI` | N/A | [x] | [ ] | SS24 |

---

# Part 4: Built-in Functions

## 22. Aggregate Functions (SS5.1)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `COUNT(*)`, `COUNT(expr)`, `COUNT(DISTINCT)` | N/A | [x] | [ ] | SS5.1 |
| `SUM`, `AVG`, `MIN`, `MAX` | N/A | [x] | [ ] | SS5.1 |
| `GROUP_CONCAT(expr, sep)` | N/A | [x] | [ ] | SS5.1 |

## 23. String Functions (SS5.2)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `LENGTH`, `CHAR_LENGTH`, `OCTET_LENGTH` | N/A | [x] | [ ] | SS5.2 |
| `UPPER`, `LOWER` | N/A | [x] | [ ] | SS5.2 |
| `SUBSTR`, `SUBSTRING` | N/A | [x] | [ ] | SS5.2 |
| `LEFT(str, n)` | N/A | [x] | [ ] | SS5.2 |
| `RIGHT(str, n)` | N/A | [x] | [ ] | SS5.2 |
| `TRIM`, `LTRIM`, `RTRIM` | N/A | [x] | [ ] | SS5.2 |
| `REPLACE`, `INSTR`, `POSITION` | N/A | [x] | [ ] | SS5.2 |
| `CONCAT`, `CONCAT_WS` | N/A | [x] | [ ] | SS5.2 |
| `REVERSE`, `REPEAT`, `SPACE` | N/A | [x] | [ ] | SS5.2 |
| `LPAD`, `RPAD`, `FORMAT` | N/A | [x] | [ ] | SS5.2 |

## 24. Numeric Functions (SS5.3)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `ABS`, `SIGN`, `ROUND`, `FLOOR`, `CEIL` | N/A | [x] | [ ] | SS5.3 |
| `TRUNC`, `MOD`, `POWER`, `SQRT` | N/A | [x] | [ ] | SS5.3 |
| `EXP`, `LOG`, `LOG10`, `LOG2` | N/A | [x] | [ ] | SS5.3 |
| `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN` | N/A | [x] | [ ] | SS5.3 |
| `ATAN2`, `PI`, `DEGREES`, `RADIANS` | N/A | [x] | [ ] | SS5.3 |
| `RANDOM()`, `RANDOM(min, max)` | N/A | [x] | [ ] | SS5.3 |

## 25. Date/Time Functions (SS5.4)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `NOW()`, `CURRENT_TIMESTAMP/DATE/TIME` | N/A | [x] | [ ] | SS5.4 |
| `DATE(expr)`, `TIME(expr)` | N/A | [x] | [ ] | SS5.4 |
| `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND` | N/A | [x] | [ ] | SS5.4 |
| `DAYOFWEEK`, `DAYOFYEAR`, `WEEKOFYEAR`, `QUARTER` | N/A | [x] | [ ] | SS5.4 |
| `DATEADD`, `DATEDIFF`, `STRFTIME` | N/A | [x] | [ ] | SS5.4 |
| `MAKEDATE`, `MAKETIME` | N/A | [x] | [ ] | SS5.4 |

## 26. ID / Conversion / Null Functions (SS5.5-5.7)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `NEWGUID()`, `NEWUUID()` | N/A | [x] | [ ] | SS5.5 |
| `INCREMENT(seq)`, `LASTINCREMENT(seq)` | N/A | [x] | [ ] | SS5.5 |
| `CAST`, `CONVERT` | N/A | [x] | [ ] | SS5.6 |
| `TOSTRING`, `TOINT`, `TODOUBLE`, etc. | N/A | [x] | [ ] | SS5.6 |
| `HEX`, `UNHEX`, `BASE64`, `UNBASE64` | N/A | [x] | [ ] | SS5.6 |
| `COALESCE`, `NULLIF`, `IFNULL`, `NVL` | N/A | [x] | [ ] | SS5.7 |

## 27. System Functions (SS5.8)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `DATABASE()`, `VERSION()`, `TYPEOF()` | N/A | [x] | [ ] | SS5.8 |
| `ROWID`, `CHANGES()`, `LAST_INSERT_ROWID()` | N/A | [x] | [ ] | SS5.8 |

## 28. JSON Functions (SS21.2)

| Function | Core | Parser | Engine | Spec |
|----------|------|--------|--------|------|
| `JSON_VALUE`, `JSON_QUERY`, `JSON_EXTRACT` | N/A | [x] | [ ] | SS21.2 |
| `JSON_SET`, `JSON_INSERT`, `JSON_REPLACE` | N/A | [x] | [ ] | SS21.2 |
| `JSON_REMOVE`, `JSON_TYPE`, `JSON_VALID` | N/A | [x] | [ ] | SS21.2 |
| `JSON_ARRAY`, `JSON_OBJECT` | N/A | [x] | [ ] | SS21.2 |

---

# Part 5: Window Functions, Transactions

## 29. Window Functions (SS7)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `OVER ()` | N/A | [x] | [ ] | SS7 |
| `OVER (PARTITION BY ...)` | N/A | [x] | [ ] | SS7 |
| `OVER (ORDER BY ...)` | N/A | [x] | [ ] | SS7 |
| `ROWS/RANGE frame_clause` | N/A | [x] | [ ] | SS7 |
| `UNBOUNDED PRECEDING/FOLLOWING` | N/A | [x] | [ ] | SS7 |
| `n PRECEDING/FOLLOWING`, `CURRENT ROW` | N/A | [x] | [ ] | SS7 |
| `ROW_NUMBER()`, `RANK()`, `DENSE_RANK()` | N/A | [x] | [ ] | SS7.1 |
| `NTILE(n)`, `PERCENT_RANK()`, `CUME_DIST()` | N/A | [x] | [ ] | SS7.1 |
| `FIRST_VALUE`, `LAST_VALUE`, `NTH_VALUE` | N/A | [x] | [ ] | SS7.2 |
| `LAG`, `LEAD` | N/A | [x] | [ ] | SS7.2 |

## 30. Transactions (SS9)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `BEGIN [TRANSACTION]` | [x] | [x] | [ ] | SS9 |
| `COMMIT` | [x] | [x] | [ ] | SS9 |
| `ROLLBACK` | [x] | [x] | [ ] | SS9 |
| `SAVEPOINT name` | [x] | [x] | [ ] | SS9 |
| `RELEASE SAVEPOINT name` | [x] | [x] | [ ] | SS9 |
| `ROLLBACK TO SAVEPOINT name` | [x] | [x] | [ ] | SS9 |
| `SET TRANSACTION ISOLATION LEVEL` | [x] | [x] | [ ] | SS14.1 |
| Isolation level keywords | [x] | [x] | [ ] | SS14.1 |

## 31. INFORMATION_SCHEMA (SS13.1)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `INFORMATION_SCHEMA.TABLES` | N/A | N/A | [ ] | SS13.1 |
| `INFORMATION_SCHEMA.COLUMNS` | N/A | N/A | [ ] | SS13.1 |
| `INFORMATION_SCHEMA.KEY_COLUMN_USAGE` | N/A | N/A | [ ] | SS13.1 |
| `INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS` | N/A | N/A | [ ] | SS13.1 |
| `INFORMATION_SCHEMA.INDEXES` | N/A | N/A | [ ] | SS13.1 |
| `INFORMATION_SCHEMA.VIEWS` | N/A | N/A | [ ] | SS13.1 |

## 32. Comments (SS10)

| Feature | Core | Parser | Engine | Spec |
|---------|------|--------|--------|------|
| `-- single line comment` | N/A | [x] | N/A | SS10 |
| `/* multi-line comment */` | N/A | [x] | N/A | SS10 |

---

# Part 6: Core Engine Components

## 33. Existing Core Components

| Feature | Core | Notes |
|---------|------|-------|
| `IKeyValueStore` interface | [x] | Get, Put, Delete, Scan, Flush |
| `ITransactionalStore` interface | [x] | BeginTransaction, ACID |
| `ITransaction` interface | [x] | Commit, Rollback |
| `StoreBTree` | [x] | B+Tree, read-optimized |
| `StoreLsm` | [x] | LSM-Tree, write-optimized |
| `StoreInMemory` | [x] | For testing |
| `StorageFile` | [x] | File-based |
| `StorageMemory` | [x] | Memory-based |
| `StorageEncrypted` | [x] | AES-GCM, ChaCha20 |
| AES-256-GCM encryption | [x] | Hardware accelerated |
| ChaCha20-Poly1305 | [x] | BouncyCastle |
| PBKDF2 key derivation | [x] | Password support |
| Write-Ahead Log (WAL) | [x] | Durability |
| Rollback Journal | [x] | Alternative to WAL |
| Crash recovery | [x] | On open |
| Reader-writer locking | [x] | Multiple readers |
| Writer priority | [x] | Prevent starvation |
| File locking | [x] | Multi-process |
| Reentrancy detection | [x] | LockRecursionException |

## 34. v1 Completed Core Components

| Feature | Core | Priority | Status |
|---------|------|----------|--------|
| `IsolationLevel` enum | [x] | P0 | Done |
| MVCC implementation | [x] | P0 | Done |
| Transaction isolation level | [x] | P0 | Done |
| Record versioning | [x] | P0 | Done |
| `RowLockManager` | [x] | P0 | Done |
| `FOR UPDATE` / `FOR SHARE` | [x] | P0 | Done |
| `NOWAIT` / `SKIP LOCKED` | [x] | P1 | Done |
| Deadlock detection | [x] | P0 | Done |
| `CreateSavepoint(name)` | [x] | P1 | Done |
| `RollbackToSavepoint(name)` | [x] | P1 | Done |
| `ReleaseSavepoint(name)` | [x] | P1 | Done |
| Nested savepoints | [x] | P1 | Done |
| `IMultiResultReader` | [x] | P1 | Done |
| `NextResult()` | [x] | P1 | Done |
| Batch execution | [x] | P1 | Done |
| `IQueryContext` | [x] | P0 | Done |
| `AffectedRows` | [x] | P0 | Done |
| `LastInsertId` | [x] | P0 | Done |
| Query timeout | [x] | P0 | Done |
| `CancellationToken` | [x] | P0 | Done |
| `ISecondaryIndex` | [x] | P0 | Done |
| B+Tree secondary indexes | [x] | P0 | Done |
| Unique index support | [x] | P0 | Done |
| Composite index support | [x] | P0 | Done |
| Index auto-update | [x] | P0 | Done |
| `BulkPut()` | [x] | P1 | Done |
| `BulkDelete()` | [x] | P1 | Done |
| Streaming insert | [x] | P1 | Done |
| Table row count | [x] | P1 | Done |
| Index statistics | [x] | P1 | Done |
| Concurrent read transactions | [x] | P0 | Done |
| Read during write (MVCC) | [x] | P0 | Done |
| Transaction wait queue | [x] | P0 | Done |
| ROWVERSION auto-increment | [x] | P1 | Done |
| Optimistic concurrency | [x] | P1 | Done |
| Conditional Put/Delete | [x] | P1 | Done |
| MVCC Garbage Collection | [x] | P1 | Done |

---

# Implementation Phases for Engine

## Phase 1: MVP (4-6 weeks)

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

## Phase 2: JOINs and Indexes (3-4 weeks)

**Goal:** Multi-table queries and performance

- [ ] INNER JOIN, LEFT JOIN
- [ ] CREATE/DROP INDEX
- [ ] Index usage in WHERE clauses
- [ ] GROUP BY, HAVING
- [ ] Subqueries (scalar, IN, EXISTS)
- [ ] CTE (WITH clause)
- [ ] UNION / UNION ALL

## Phase 3: Transactions and Concurrency (3-4 weeks)

**Goal:** Full transaction support

- [ ] Transaction isolation levels
- [ ] Savepoints
- [ ] FOR UPDATE / FOR SHARE
- [ ] ROWVERSION support
- [ ] INSERT ... ON CONFLICT
- [ ] MERGE statement

## Phase 4: Production Ready (4-6 weeks)

**Goal:** Production-ready engine

- [ ] Window functions
- [ ] Recursive CTE
- [ ] Views and triggers
- [ ] All remaining v1 functions
- [ ] INFORMATION_SCHEMA
- [ ] Basic query optimization
- [ ] JSON functions
- [ ] Collation support

---

# Files Reference

| File | Content |
|------|---------|
| `Roadmap.v1.md` | This v1 roadmap |
| `Roadmap.v2.md` | v2 features roadmap |
| `Roadmap.Core.md` | Core-only roadmap |
| `Roadmap.Parser.md` | Parser-only roadmap |
| `Roadmap.Engine.md` | Engine-only roadmap |
| `WitSql.md` | Language Specification |
| `OutWit.Database.Core.TODO.md` | Core TODO list |
| `CODE_STYLE_GUIDE.md` | Code style guide |

---

**Last Updated:** 2025-01-17
