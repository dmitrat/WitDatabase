# OutWit.Database.Parser - Roadmap

**Version:** 1.0  
**Based on:** WitSql.md specification v1.2 and OutWit.Database.Parser.TODO.md  
**Last Updated:** 2024-12-21

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

---

## Progress Summary

| Category | Implemented | Missing | Progress |
|----------|-------------|---------|----------|
| Data Types | 29 | 0 | 100% |
| DDL - CREATE TABLE | 17 | 0 | 100% |
| DDL - DROP/ALTER TABLE | 10 | 0 | 100% |
| DDL - INDEX | 8 | 0 | 100% |
| DDL - VIEW | 4 | 0 | 100% |
| DDL - TRIGGER | 9 | 1 | 90% |
| DDL - SEQUENCE | 5 | 0 | 100% |
| DML - SELECT | 15 | 0 | 100% |
| DML - INSERT | 7 | 1 | 88% |
| DML - UPDATE | 3 | 1 | 75% |
| DML - DELETE | 3 | 1 | 75% |
| DML - TRUNCATE/MERGE | 1 | 3 | 25% |
| CTE / Set Operations | 6 | 0 | 100% |
| Subqueries | 7 | 0 | 100% |
| Operators | 20 | 0 | 100% |
| Conditional Expressions | 6 | 0 | 100% |
| Literals & Parameters | 6 | 0 | 100% |
| Collation | 0 | 4 | 0% |
| Aggregate Functions | 5 | 0 | 100% |
| String Functions | 20 | 0 | 100% |
| Numeric Functions | 20 | 0 | 100% |
| Date/Time Functions | 17 | 0 | 100% |
| ID/Conversion/Null Functions | 14 | 0 | 100% |
| System Functions | 6 | 0 | 100% |
| JSON Functions | 11 | 0 | 100% |
| Window Functions | 18 | 0 | 100% |
| Transactions | 8 | 0 | 100% |
| User-Defined Functions | 0 | 4 | 0% - Deferred to v2 |
| Stored Procedures | 0 | 4 | 0% - Deferred to v2 |
| EXPLAIN | 0 | 3 | 0% - Deferred to v2 |
| Database Administration | 0 | 7 | 0% - Deferred to v2 |
| Comments | 2 | 0 | 100% |
| **TOTAL (v1)** | **275** | **7** | **98%** |

---

## 1. Data Types [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `NULL` type | [x] | SS1.1 |
| `TINYINT` / `INT8` | [x] | SS1.2 |
| `UTINYINT` / `UINT8` | [x] | SS1.2 |
| `SMALLINT` / `INT16` | [x] | SS1.2 |
| `USMALLINT` / `UINT16` | [x] | SS1.2 |
| `INT` / `INT32` / `INTEGER` | [x] | SS1.2 |
| `UINT` / `UINT32` | [x] | SS1.2 |
| `BIGINT` / `INT64` / `LONG` | [x] | SS1.2 |
| `UBIGINT` / `UINT64` / `ULONG` | [x] | SS1.2 |
| `FLOAT16` / `HALF` | [x] | SS1.3 |
| `FLOAT` / `FLOAT32` / `REAL` | [x] | SS1.3 |
| `DOUBLE` / `FLOAT64` | [x] | SS1.3 |
| `DECIMAL` / `MONEY` / `NUMERIC` | [x] | SS1.3 |
| `DECIMAL(precision, scale)` | [x] | SS13.2 |
| `BOOLEAN` / `BOOL` | [x] | SS1.4 |
| `DATE` / `DATEONLY` | [x] | SS1.5 |
| `TIME` / `TIMEONLY` | [x] | SS1.5 |
| `DATETIME` / `TIMESTAMP` | [x] | SS1.5 |
| `DATETIMEOFFSET` | [x] | SS1.5 |
| `INTERVAL` / `TIMESPAN` | [x] | SS1.5 |
| `GUID` / `UUID` / `UNIQUEIDENTIFIER` | [x] | SS1.6 |
| `CHAR(n)` / `NCHAR(n)` | [x] | SS1.7 |
| `VARCHAR(n)` / `NVARCHAR(n)` | [x] | SS1.7 |
| `TEXT` / `NTEXT` | [x] | SS1.7 |
| `BINARY(n)` | [x] | SS1.8 |
| `VARBINARY(n)` | [x] | SS1.8 |
| `BLOB` | [x] | SS1.8 |
| `ROWVERSION` | [x] | SS15.1 |
| `JSON` / `JSONB` | [x] | SS21.1 |

---

## 2. DDL - CREATE TABLE (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE TABLE table_name (...)` | [x] | SS2.1 |
| `CREATE TABLE IF NOT EXISTS` | [x] | SS2.1 |
| `NOT NULL` / `NULL` | [x] | SS2.1 |
| `PRIMARY KEY` | [x] | SS2.1 |
| `PRIMARY KEY AUTOINCREMENT` | [x] | SS2.1 |
| `UNIQUE` | [x] | SS2.1 |
| `DEFAULT literal_value` | [x] | SS2.1 |
| `DEFAULT (expression)` | [x] | SS2.1 |
| `CHECK (expression)` | [x] | SS2.1 |
| `REFERENCES table(col)` | [x] | SS2.1 |
| `ON DELETE/UPDATE action` | [x] | SS2.1 |
| `PRIMARY KEY (column_list)` | [x] | SS2.1 |
| `UNIQUE (column_list)` | [x] | SS2.1 |
| `FOREIGN KEY (cols) REFERENCES ...` | [x] | SS2.1 |
| `CONSTRAINT name ...` | [x] | SS13.3 |
| Computed columns `AS (expr)` | [x] | SS20 |
| `STORED` / `VIRTUAL` modifiers | [x] | SS20 |

---

## 3. DDL - DROP/ALTER TABLE (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `DROP TABLE table_name` | [x] | SS2.2 |
| `DROP TABLE IF EXISTS` | [x] | SS2.2 |
| `ALTER TABLE ... ADD [COLUMN]` | [x] | SS2.3 |
| `ALTER TABLE ... ADD CONSTRAINT` | [x] | SS2.3 |
| `ALTER TABLE ... DROP [COLUMN]` | [x] | SS2.3 |
| `ALTER TABLE ... DROP CONSTRAINT` | [x] | SS13.3 |
| `ALTER TABLE ... RENAME TO` | [x] | SS2.3 |
| `ALTER TABLE ... RENAME COLUMN` | [x] | SS2.3 |
| `ALTER TABLE ... SET/DROP DEFAULT` | [x] | SS2.3 |
| `ALTER TABLE ... SET/DROP NOT NULL` | [x] | SS2.3 |

---

## 4. DDL - INDEX (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE INDEX name ON table (cols)` | [x] | SS2.4 |
| `CREATE UNIQUE INDEX` | [x] | SS2.4 |
| `CREATE INDEX IF NOT EXISTS` | [x] | SS2.4 |
| `ASC` / `DESC` column order | [x] | SS2.4 |
| `DROP INDEX [IF EXISTS]` | [x] | SS2.5 |
| `WHERE condition` (partial index) | [x] | SS19.1 |
| Expression indexes `(LOWER(col))` | [x] | SS19.2 |
| `INCLUDE (cols)` (covering index) | [x] | SS19.3 |

---

## 5. DDL - VIEW [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE VIEW name AS SELECT` | [x] | SS2.6 |
| `CREATE VIEW IF NOT EXISTS` | [x] | SS2.6 |
| `CREATE VIEW name (cols) AS` | [x] | SS2.6 |
| `DROP VIEW [IF EXISTS]` | [x] | SS2.7 |

---

## 6. DDL - TRIGGER (90%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE TRIGGER BEFORE/AFTER/INSTEAD OF` | [x] | SS2.8 |
| `CREATE TRIGGER IF NOT EXISTS` | [x] | SS2.8 |
| `INSERT` / `UPDATE` / `DELETE` events | [x] | SS2.8 |
| `UPDATE OF col_list` | [x] | SS2.8 |
| `FOR EACH ROW` | [x] | SS2.8 |
| `WHEN (condition)` | [x] | SS2.8 |
| `BEGIN ... END` body | [x] | SS2.8 |
| `OLD.column` / `NEW.column` | [x] | SS2.8 |
| `DROP TRIGGER [IF EXISTS]` | [x] | SS2.9 |
| `SIGNAL SQLSTATE` | [ ] | SS2.8 |

---

## 7. DDL - SEQUENCE [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE SEQUENCE name START WITH n` | [x] | SS5.5 |
| `ALTER SEQUENCE name RESTART WITH n` | [x] | SS5.5 |
| `DROP SEQUENCE name` | [x] | SS5.5 |
| `INCREMENT(sequence)` function | [x] | SS5.5 |
| `LASTINCREMENT(sequence)` function | [x] | SS5.5 |

---

## 8. DML - SELECT (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `SELECT *` / `SELECT column_list` | [x] | SS3.1 |
| `SELECT expression AS alias` | [x] | SS3.1 |
| `SELECT DISTINCT` / `SELECT ALL` | [x] | SS3.1 |
| `FROM table_name [AS alias]` | [x] | SS3.1 |
| `FROM (subquery) AS alias` | [x] | SS3.1 |
| `INNER/LEFT/RIGHT/FULL JOIN` | [x] | SS3.1 |
| `CROSS JOIN` | [x] | SS3.1 |
| `WHERE condition` | [x] | SS3.1 |
| `GROUP BY` / `HAVING` | [x] | SS3.1 |
| `ORDER BY expr [ASC/DESC] [NULLS FIRST/LAST]` | [x] | SS3.1 |
| `LIMIT count [OFFSET offset]` | [x] | SS3.1 |
| `FOR UPDATE` | [x] | SS14.2 |
| `FOR SHARE` | [x] | SS14.2 |
| `FOR UPDATE NOWAIT` | [x] | SS14.2 |
| `FOR UPDATE SKIP LOCKED` | [x] | SS14.2 |

---

## 9. DML - INSERT (88%)

| Feature | Status | Spec |
|---------|--------|------|
| `INSERT INTO table (cols) VALUES (...)` | [x] | SS3.2 |
| Multi-row `VALUES (...), (...)` | [x] | SS3.2 |
| `INSERT INTO ... SELECT` | [x] | SS3.2 |
| `INSERT ... RETURNING` | [x] | SS3.2 |
| `INSERT OR REPLACE` | [x] | SS16.1 |
| `INSERT ... ON CONFLICT DO UPDATE` | [x] | SS16.2 |
| `INSERT ... ON CONFLICT DO NOTHING` | [x] | SS16.2 |
| `EXCLUDED.column` reference | [ ] | SS16 |

---

## 10. DML - UPDATE (75%)

| Feature | Status | Spec |
|---------|--------|------|
| `UPDATE table SET col = expr` | [x] | SS3.3 |
| `UPDATE ... WHERE condition` | [x] | SS3.3 |
| `UPDATE ... RETURNING` | [x] | SS3.3 |
| `UPDATE ... FROM other_table` | [ ] | SS17.2 |

---

## 11. DML - DELETE (75%)

| Feature | Status | Spec |
|---------|--------|------|
| `DELETE FROM table` | [x] | SS3.4 |
| `DELETE FROM ... WHERE` | [x] | SS3.4 |
| `DELETE FROM ... RETURNING` | [x] | SS3.4 |
| `DELETE ... USING other_table` | [ ] | SS17.3 |

---

## 12. DML - TRUNCATE/MERGE (25%)

| Feature | Status | Spec |
|---------|--------|------|
| `TRUNCATE TABLE table_name` | [x] | SS17.1 |
| `MERGE INTO target USING source ON` | [ ] | SS16.3 |
| `WHEN MATCHED THEN UPDATE` | [ ] | SS16.3 |
| `WHEN NOT MATCHED THEN INSERT` | [ ] | SS16.3 |

---

## 13. CTE / Set Operations [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `WITH cte_name AS (SELECT ...)` | [x] | SS6 |
| `WITH cte_name (cols) AS (...)` | [x] | SS6 |
| Multiple CTEs | [x] | SS6 |
| `WITH RECURSIVE ...` | [x] | SS6 |
| `UNION` / `UNION ALL` | [x] | SS8 |
| `INTERSECT` / `EXCEPT` | [x] | SS8 |

---

## 14. Subqueries (100%)

| Feature | Status | Spec |
|---------|--------|------|
| Scalar subquery in SELECT | [x] | SS3.1 |
| Table subquery in FROM | [x] | SS3.1 |
| Subquery with `IN` | [x] | SS3.1 |
| `EXISTS` / `NOT EXISTS` | [x] | SS18.1 |
| `expression > ANY (subquery)` | [x] | SS18.2 |
| `expression > SOME (subquery)` | [x] | SS18.2 |
| `expression > ALL (subquery)` | [x] | SS18.2 |

---

## 15. Operators [x] (100%)

### Comparison Operators

| Feature | Status | Spec |
|---------|--------|------|
| `=`, `<>`, `!=` | [x] | SS4.1 |
| `<`, `<=`, `>`, `>=` | [x] | SS4.1 |
| `IS NULL` / `IS NOT NULL` | [x] | SS4.1 |
| `BETWEEN x AND y` | [x] | SS4.1 |
| `IN (...)` / `NOT IN (...)` | [x] | SS4.1 |
| `LIKE` / `NOT LIKE` / `ESCAPE` | [x] | SS4.1 |
| `GLOB pattern` | [x] | SS4.1 |

### Logical Operators

| Feature | Status | Spec |
|---------|--------|------|
| `AND` / `OR` / `NOT` | [x] | SS4.2 |

### Arithmetic Operators

| Feature | Status | Spec |
|---------|--------|------|
| `+`, `-`, `*`, `/`, `%` | [x] | SS4.3 |
| Unary `-expr`, `+expr` | [x] | SS4.3 |

### String / Bitwise Operators

| Feature | Status | Spec |
|---------|--------|------|
| `||` concatenation | [x] | SS4.4 |
| `&`, `|`, `~`, `<<`, `>>` | [x] | SS4.5 |

---

## 16. Conditional Expressions [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `CASE expr WHEN val THEN result END` | [x] | SS4.6 |
| `CASE WHEN cond THEN result END` | [x] | SS4.6 |
| `COALESCE(...)` | [x] | SS4.6 |
| `NULLIF(a, b)` | [x] | SS4.6 |
| `IIF(cond, true_val, false_val)` | [x] | SS4.6 |
| `CAST(expr AS type)` | [x] | SS4.6 |

---

## 17. Literals & Parameters [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| Integer / Float literals | [x] | SS1.2-1.3 |
| String literals `'text'` | [x] | SS1.7 |
| `TRUE` / `FALSE` / `NULL` | [x] | SS1.1, SS1.4 |
| Hex blob `X'...'` | [x] | SS1.8 |
| Named parameters `@param`, `:param` | [x] | SS11 |
| Positional `?`, `$1`, `$2` | [x] | SS11 |

---

## 18. Collation (0%)

| Feature | Status | Spec |
|---------|--------|------|
| `COLLATE collation_name` in column | [ ] | SS24 |
| `COLLATE` in expression | [ ] | SS24 |
| `COLLATE` in ORDER BY | [ ] | SS24 |
| `BINARY`, `NOCASE`, `UNICODE`, `UNICODE_CI` | [ ] | SS24 |

---

## 19. Functions

### Aggregate Functions [x] (100%)

| Function | Status | Spec |
|----------|--------|------|
| `COUNT(*)`, `COUNT(expr)`, `COUNT(DISTINCT)` | [x] | SS5.1 |
| `SUM`, `AVG`, `MIN`, `MAX` | [x] | SS5.1 |
| `GROUP_CONCAT(expr, sep)` | [x] | SS5.1 |

### String Functions [x] (100%)

| Function | Status | Spec |
|----------|--------|------|
| `LENGTH`, `CHAR_LENGTH`, `OCTET_LENGTH` | [x] | SS5.2 |
| `UPPER`, `LOWER` | [x] | SS5.2 |
| `SUBSTR`, `SUBSTRING` | [x] | SS5.2 |
| `LEFT(str, n)` | [x] | SS5.2 |
| `RIGHT(str, n)` | [x] | SS5.2 |
| `TRIM`, `LTRIM`, `RTRIM` | [x] | SS5.2 |
| `REPLACE`, `INSTR`, `POSITION` | [x] | SS5.2 |
| `CONCAT`, `CONCAT_WS` | [x] | SS5.2 |
| `REVERSE`, `REPEAT`, `SPACE` | [x] | SS5.2 |
| `LPAD`, `RPAD`, `FORMAT` | [x] | SS5.2 |

### Numeric Functions [x] (100%)

| Function | Status | Spec |
|----------|--------|------|
| `ABS`, `SIGN`, `ROUND`, `FLOOR`, `CEIL` | [x] | SS5.3 |
| `TRUNC`, `MOD`, `POWER`, `SQRT` | [x] | SS5.3 |
| `EXP`, `LOG`, `LOG10`, `LOG2` | [x] | SS5.3 |
| Trigonometric functions | [x] | SS5.3 |
| `PI`, `DEGREES`, `RADIANS` | [x] | SS5.3 |
| `RANDOM()`, `RANDOM(min, max)` | [x] | SS5.3 |

### Date/Time Functions [x] (100%)

| Function | Status | Spec |
|----------|--------|------|
| `NOW()`, `CURRENT_TIMESTAMP/DATE/TIME` | [x] | SS5.4 |
| `DATE(expr)`, `TIME(expr)` | [x] | SS5.4 |
| `YEAR`, `MONTH`, `DAY`, `HOUR`, etc. | [x] | SS5.4 |
| `DATEADD`, `DATEDIFF`, `STRFTIME` | [x] | SS5.4 |
| `MAKEDATE`, `MAKETIME` | [x] | SS5.4 |

### JSON Functions (100%)

| Function | Status | Spec |
|----------|--------|------|
| `JSON_VALUE`, `JSON_QUERY`, `JSON_EXTRACT` | [x] | SS21.2 |
| `JSON_SET`, `JSON_INSERT`, `JSON_REPLACE` | [x] | SS21.2 |
| `JSON_REMOVE`, `JSON_TYPE`, `JSON_VALID` | [x] | SS21.2 |
| `JSON_ARRAY`, `JSON_OBJECT` | [x] | SS21.2 |

---

## 20. Window Functions [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `OVER ()` | [x] | SS7 |
| `OVER (PARTITION BY ...)` | [x] | SS7 |
| `OVER (ORDER BY ...)` | [x] | SS7 |
| `ROWS/RANGE frame_clause` | [x] | SS7 |
| `UNBOUNDED PRECEDING/FOLLOWING` | [x] | SS7 |
| `ROW_NUMBER()`, `RANK()`, `DENSE_RANK()` | [x] | SS7.1 |
| `NTILE(n)`, `PERCENT_RANK()`, `CUME_DIST()` | [x] | SS7.1 |
| `FIRST_VALUE`, `LAST_VALUE`, `NTH_VALUE` | [x] | SS7.2 |
| `LAG`, `LEAD` | [x] | SS7.2 |

---

## 21. Transactions (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `BEGIN [TRANSACTION]` | [x] | SS9 |
| `COMMIT` | [x] | SS9 |
| `ROLLBACK` | [x] | SS9 |
| `SAVEPOINT name` | [x] | SS9 |
| `RELEASE SAVEPOINT name` | [x] | SS9 |
| `ROLLBACK TO SAVEPOINT name` | [x] | SS9 |
| `SET TRANSACTION ISOLATION LEVEL` | [x] | SS14.1 |
| Isolation level keywords | [x] | SS14.1 |

---

## 22. User-Defined Functions (0%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE FUNCTION ... RETURNS ... AS BEGIN END` | [ ] | SS22.1 |
| `RETURNS TABLE (...)` | [ ] | SS22.2 |
| `DETERMINISTIC` modifier | [ ] | SS22.1 |
| `DROP FUNCTION [IF EXISTS]` | [ ] | SS22 |

---

## 23. Stored Procedures (0%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE PROCEDURE ... AS BEGIN END` | [ ] | SS23 |
| `DROP PROCEDURE [IF EXISTS]` | [ ] | SS23 |
| `CALL procedure(args)` | [ ] | SS23 |
| `EXECUTE procedure(args)` | [ ] | SS23 |

---

## 24. EXPLAIN (0%)

| Feature | Status | Spec |
|---------|--------|------|
| `EXPLAIN select_statement` | [ ] | SS25.1 |
| `EXPLAIN ANALYZE` | [ ] | SS25.1 |
| `EXPLAIN (FORMAT JSON/TEXT)` | [ ] | SS25.1 |

---

## 25. Database Administration (0%)

| Feature | Status | Spec |
|---------|--------|------|
| `CREATE DATABASE` | [ ] | SS26.1 |
| `DROP DATABASE [IF EXISTS]` | [ ] | SS26.1 |
| `ATTACH DATABASE 'path' AS alias` | [ ] | SS26.1 |
| `DETACH DATABASE alias` | [ ] | SS26.1 |
| `VACUUM [table_name]` | [ ] | SS26.2 |
| `ANALYZE [table_name]` | [ ] | SS26.2 |
| `PRAGMA name [= value]` | [ ] | SS26.3 |

---

## 26. Comments [x] (100%)

| Feature | Status | Spec |
|---------|--------|------|
| `-- single line comment` | [x] | SS10 |
| `/* multi-line comment */` | [x] | SS10 |

---

## Implementation Priorities

### Phase 1: MVP for ADO.NET (P0)

| Feature | Priority |
|---------|----------|
| `INSERT OR REPLACE` | P0 |
| `INSERT ... ON CONFLICT DO UPDATE` | P0 |
| `INSERT ... ON CONFLICT DO NOTHING` | P0 |
| `ANY` / `SOME` / `ALL` operators | P0 |
| `LEFT(str, n)` / `RIGHT(str, n)` | P1 |

### Phase 2: EF Core Compatibility (P0)

| Feature | Priority |
|---------|----------|
| `SET TRANSACTION ISOLATION LEVEL` | P0 |
| `FOR UPDATE` / `FOR SHARE` | P0 |
| `NOWAIT` / `SKIP LOCKED` | P0 |
| `CONSTRAINT name ...` | P0 |
| `ALTER TABLE ... DROP CONSTRAINT` | P0 |
| `MERGE INTO ... USING ...` | P0 |
| `UPDATE ... FROM` | P1 |
| `DELETE ... USING` | P1 |

### Phase 3: Production Ready (P1)

| Feature | Priority |
|---------|----------|
| Partial indexes `WHERE` | P1 |
| Expression indexes | P1 |
| Covering indexes `INCLUDE` | P1 |
| Computed columns | P1 |
| Collation support | P1 |
| JSON functions | P1 |

### Phase 4: Advanced Features (P2)

| Feature | Priority |
|---------|----------|
| `CREATE FUNCTION` / `DROP FUNCTION` | P2 - Deferred to v2 |
| `CREATE PROCEDURE` / `DROP PROCEDURE` | P2 - Deferred to v2 |
| `CALL` / `EXECUTE` | P2 - Deferred to v2 |
| `EXPLAIN` / `EXPLAIN ANALYZE` | P2 - Deferred to v2 |
| Database administration | P2 - Deferred to v2 |
