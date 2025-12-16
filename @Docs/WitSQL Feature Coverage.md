# WitSQL Feature Coverage

This document tracks the implementation status of all WitSQL language features.

## Coverage Table

| Feature                                        | Parser | Engine | Spec Line |
| ---------------------------------------------- | :----: | :----: | --------- |
| **1. DATA TYPES**                              |        |        | 10-82     |
| NULL type                                      |   ✅    |   ✅    | 14-18     |
| Integer types (TINYINT, SMALLINT, INT, BIGINT) |   ✅    |   ✅    | 20-31     |
| Floating-point (FLOAT, DOUBLE, DECIMAL)        |   ✅    |   ✅    | 33-40     |
| BOOLEAN                                        |   ✅    |   ✅    | 42-46     |
| DATE, TIME, DATETIME                           |   ✅    |   ✅    | 48-56     |
| DATETIMEOFFSET                                 |   ✅    |   ✅    | 55        |
| INTERVAL/TIMESPAN                              |   ✅    |   ✅    | 56        |
| GUID/UUID                                      |   ✅    |   ✅    | 58-62     |
| CHAR(n), VARCHAR(n), TEXT                      |   ✅    |   ✅    | 64-73     |
| BINARY(n), VARBINARY(n), BLOB                  |   ✅    |   ✅    | 75-81     |
| **2. DDL**                                     |        |        | 85-259    |
| CREATE TABLE                                   |   ✅    |   ✅    | 87-147    |
| CREATE TABLE IF NOT EXISTS                     |   ✅    |   ✅    | 90        |
| Column NOT NULL                                |   ✅    |   ✅    | 99        |
| Column PRIMARY KEY                             |   ✅    |   ✅    | 101       |
| Column AUTOINCREMENT                           |   ✅    |   ✅    | 101       |
| Column UNIQUE                                  |   ✅    |   ✅    | 102       |
| Column DEFAULT                                 |   ✅    |   ✅    | 103-104   |
| Column CHECK                                   |   ✅    |   ✅    | 105       |
| Column REFERENCES (FK)                         |   ✅    |   ✅    | 106-107   |
| Table constraint PRIMARY KEY                   |   ✅    |   ✅    | 110       |
| Table constraint UNIQUE                        |   ✅    |   ✅    | 111       |
| Table constraint FOREIGN KEY                   |   ✅    |   ✅    | 112-113   |
| Table constraint CHECK                         |   ✅    |   ✅    | 114       |
| DROP TABLE                                     |   ✅    |   ✅    | 149-160   |
| DROP TABLE IF EXISTS                           |   ✅    |   ✅    | 152       |
| ALTER TABLE ADD COLUMN                         |   ✅    |   ✅    | 166       |
| ALTER TABLE DROP COLUMN                        |   ✅    |   ✅    | 167       |
| ALTER TABLE RENAME TO                          |   ✅    |   ✅    | 168       |
| ALTER TABLE RENAME COLUMN                      |   ✅    |   ✅    | 169       |
| ALTER TABLE ALTER COLUMN                       |   ✅    |   ✅    | 170-173   |
| CREATE INDEX                                   |   ✅    |   ✅    | 186-200   |
| CREATE UNIQUE INDEX                            |   ✅    |   ✅    | 189       |
| CREATE INDEX IF NOT EXISTS                     |   ✅    |   ✅    | 189       |
| DROP INDEX                                     |   ✅    |   ✅    | 202-206   |
| DROP INDEX IF EXISTS                           |   ✅    |   ✅    | 205       |
| CREATE VIEW                                    |   ✅    |   ✅    | 208-222   |
| DROP VIEW                                      |   ✅    |   ✅    | 224-228   |
| CREATE TRIGGER                                 |   ✅    |   ✅    | 230-252   |
| DROP TRIGGER                                   |   ✅    |   ✅    | 254-258   |
| CREATE SEQUENCE                                |   ✅    |   ✅    | 616-632   |
| DROP SEQUENCE                                  |   ✅    |   ✅    | 616-632   |
| ALTER SEQUENCE                                 |   ✅    |   ✅    | 616-632   |
| **3. DML**                                     |        |        | 262-392   |
| SELECT basic                                   |   ✅    |   ✅    | 264-326   |
| SELECT DISTINCT                                |   ✅    |   ✅    | 267       |
| SELECT *                                       |   ✅    |   ✅    | 276       |
| SELECT with alias (AS)                         |   ✅    |   ✅    | 277       |
| FROM table                                     |   ✅    |   ✅    | 279-280   |
| FROM table alias                               |   ✅    |   ✅    | 280       |
| FROM subquery                                  |   ✅    |   ❌    | 282       |
| INNER JOIN                                     |   ✅    |   ✅    | 285       |
| LEFT JOIN                                      |   ✅    |   ✅    | 286       |
| RIGHT JOIN                                     |   ✅    |   ✅    | 287       |
| FULL OUTER JOIN                                |   ✅    |   ✅    | 288       |
| CROSS JOIN                                     |   ✅    |   ✅    | 289       |
| WHERE clause                                   |   ✅    |   ✅    | 269       |
| GROUP BY                                       |   ✅    |   ✅    | 270       |
| HAVING                                         |   ✅    |   ✅    | 271       |
| ORDER BY ASC/DESC                              |   ✅    |   ✅    | 272       |
| NULLS FIRST/LAST                               |   ❌    |   ❌    | 272       |
| LIMIT/OFFSET                                   |   ✅    |   ✅    | 273       |
| INSERT INTO (columns) VALUES                   |   ✅    |   ✅    | 328-356   |
| INSERT INTO VALUES (no columns)                |   ✅    |   ✅    | 331-332   |
| INSERT multiple rows                           |   ✅    |   ✅    | 345-349   |
| INSERT ... SELECT                              |   ✅    |   ✅    | 334-335   |
| UPDATE ... SET                                 |   ✅    |   ✅    | 358-378   |
| UPDATE ... WHERE                               |   ✅    |   ✅    | 363       |
| DELETE FROM                                    |   ✅    |   ✅    | 380-392   |
| DELETE ... WHERE                               |   ✅    |   ✅    | 384       |
| **4. OPERATORS**                               |        |        | 396-509   |
| Comparison (=, <>, <, <=, >, >=)               |   ✅    |   ✅    | 398-407   |
| IS NULL / IS NOT NULL                          |   ✅    |   ✅    | 408-409   |
| BETWEEN                                        |   ✅    |   ✅    | 410-411   |
| IN list                                        |   ✅    |   ✅    | 412-413   |
| IN subquery                                    |   ✅    |   ✅    | 412       |
| LIKE                                           |   ✅    |   ✅    | 414-421   |
| GLOB                                           |   ✅    |   ✅    | 416       |
| ESCAPE in LIKE                                 |   ✅    |   ✅    | 421       |
| AND, OR, NOT                                   |   ✅    |   ✅    | 423-431   |
| Arithmetic (+, -, *, /, %)                     |   ✅    |   ✅    | 433-443   |
| Unary minus/plus                               |   ✅    |   ✅    | 442-443   |
| String concatenation (\|\|)                    |   ✅    |   ✅    | 445-449   |
| Bitwise (&, \|, ~, <<, >>)                     |   ✅    |   ✅    | 451-459   |
| CASE ... WHEN ... END                          |   ✅    |   ✅    | 464-478   |
| COALESCE                                       |   ✅    |   ✅    | 480-481   |
| NULLIF                                         |   ✅    |   ✅    | 483-484   |
| IIF                                            |   ✅    |   ✅    | 486-487   |
| CAST                                           |   ✅    |   ✅    | 489-490   |
| **5. FUNCTIONS**                               |        |        | 512-673   |
| COUNT(*)                                       |   ✅    |   ✅    | 518       |
| COUNT(expr)                                    |   ✅    |   ✅    | 519       |
| COUNT(DISTINCT expr)                           |   ✅    |   ✅    | 520       |
| SUM, AVG, MIN, MAX                             |   ✅    |   ✅    | 521-524   |
| GROUP_CONCAT                                   |   ✅    |   ✅    | 525       |
| LENGTH, CHAR_LENGTH                            |   ✅    |   ✅    | 531-532   |
| OCTET_LENGTH                                   |   ✅    |   ✅    | 533       |
| UPPER, LOWER                                   |   ✅    |   ✅    | 534-535   |
| SUBSTR, SUBSTRING                              |   ✅    |   ✅    | 536-537   |
| LEFT, RIGHT                                    |   ✅    |   ✅    | 538-539   |
| TRIM, LTRIM, RTRIM                             |   ✅    |   ✅    | 540-543   |
| REPLACE                                        |   ✅    |   ✅    | 544       |
| INSTR, POSITION                                |   ✅    |   ✅    | 545-546   |
| CONCAT                                         |   ✅    |   ✅    | 547       |
| CONCAT_WS                                      |   ✅    |   ✅    | 548       |
| REVERSE                                        |   ✅    |   ✅    | 549       |
| REPEAT, SPACE                                  |   ✅    |   ✅    | 550-551   |
| LPAD, RPAD                                     |   ✅    |   ✅    | 552-553   |
| FORMAT                                         |   ❌    |   ❌    | 554       |
| ABS, SIGN, ROUND, FLOOR, CEIL                  |   ✅    |   ✅    | 560-564   |
| TRUNC                                          |   ✅    |   ✅    | 565       |
| MOD, POWER, SQRT                               |   ✅    |   ✅    | 566-568   |
| EXP, LOG, LOG10, LOG2                          |   ✅    |   ✅    | 569-572   |
| Trigonometric functions                        |   ✅    |   ✅    | 573-578   |
| PI()                                           |   ✅    |   ✅    | 576       |
| RANDOM                                         |   ✅    |   ✅    | 579-580   |
| NOW, CURRENT_TIMESTAMP                         |   ✅    |   ✅    | 586-587   |
| CURRENT_DATE, CURRENT_TIME                     |   ✅    |   ✅    | 588-589   |
| DATE, TIME extractors                          |   ❌    |   ❌    | 590-591   |
| YEAR, MONTH, DAY                               |   ✅    |   ✅    | 592-594   |
| HOUR, MINUTE, SECOND                           |   ✅    |   ✅    | 595-597   |
| DAYOFWEEK, DAYOFYEAR, etc.                     |   ✅    |   ✅    | 598-601   |
| DATEADD, DATEDIFF                              |   ✅    |   ✅    | 602-603   |
| STRFTIME                                       |   ✅    |   ✅    | 604       |
| MAKEDATE, MAKETIME                             |   ✅    |   ✅    | 605-606   |
| NEWGUID, NEWUUID                               |   ✅    |   ✅    | 614-615   |
| INCREMENT, LASTINCREMENT                       |   ❌    |   ❌    | 616-617   |
| CAST                                           |   ✅    |   ✅    | 639       |
| CONVERT                                        |   ✅    |   ✅    | 640       |
| TOSTRING, TOINT, etc.                          |   ✅    |   ✅    | 641-648   |
| HEX, UNHEX                                     |   ✅    |   ✅    | 649-650   |
| BASE64, UNBASE64                               |   ✅    |   ✅    | 651-652   |
| FORMAT                                         |   ✅    |   ✅    | 554       |
| IFNULL, NVL                                    |   ✅    |   ✅    | 660-661   |
| DATABASE, VERSION                              |   ✅    |   ✅    | 667-668   |
| TYPEOF                                         |   ✅    |   ✅    | 669       |
| ROWID                                          |   ❌    |   ❌    | 670       |
| CHANGES, LAST_INSERT_ROWID                     |   ✅    |   ✅    | 671-672   |
| **6. CTE**                                     |        |        | 676-720   |
| WITH ... AS                                    |   ❌    |   ❌    | 678-683   |
| WITH RECURSIVE                                 |   ❌    |   ❌    | 685-693   |
| **7. WINDOW FUNCTIONS**                        |        |        | 724-790   |
| OVER (PARTITION BY...)                         |   ❌    |   ❌    | 727-731   |
| ROWS/RANGE frame                               |   ❌    |   ❌    | 733-741   |
| ROW_NUMBER, RANK, etc.                         |   ❌    |   ❌    | 744-753   |
| FIRST_VALUE, LAST_VALUE, NTH_VALUE             |   ❌    |   ❌    | 755-761   |
| LAG, LEAD                                      |   ❌    |   ❌    | 762-763   |
| **8. SET OPERATIONS**                          |        |        | 794-817   |
| UNION                                          |   ❌    |   ❌    | 798       |
| UNION ALL                                      |   ❌    |   ❌    | 800-801   |
| INTERSECT                                      |   ❌    |   ❌    | 803-804   |
| EXCEPT                                         |   ❌    |   ❌    | 806-807   |
| **9. TRANSACTIONS**                            |        |        | 821-832   |
| BEGIN TRANSACTION                              |   ❌    |   ❌    | 824       |
| COMMIT                                         |   ❌    |   ❌    | 825       |
| ROLLBACK                                       |   ❌    |   ❌    | 826       |
| SAVEPOINT                                      |   ❌    |   ❌    | 829       |
| RELEASE SAVEPOINT                              |   ❌    |   ❌    | 830       |
| ROLLBACK TO SAVEPOINT                          |   ❌    |   ❌    | 831       |
| **10. COMMENTS**                               |        |        | 836-843   |
| Single-line (--)                               |   ✅    |   ✅    | 839       |
| Multi-line (/* */)                             |   ✅    |   ✅    | 841-842   |
| **11. PARAMETERS**                             |        |        | 847-859   |
| Named (@param, :param)                         |   ✅    |   ❌    | 853-854   |
| Positional (?, $1, $2)                         |   ✅    |   ❌    | 856-858   |

**Legend:** ✅ = Implemented, ❌ = Not implemented

## Summary

| Category              | Parser | Engine |
| --------------------- | ------ | ------ |
| Fully Implemented     | ~70%   | ~60%   |
| Partially Implemented | ~10%   | ~10%   |
| Not Started           | ~20%   | ~30%   |

## ✅ Recently Completed

- **CREATE/DROP/ALTER SEQUENCE** with NEXTVAL() and CURRVAL() functions
- **CREATE TRIGGER / DROP TRIGGER** with BEFORE/AFTER/INSTEAD OF, INSERT/UPDATE/DELETE events, OLD/NEW pseudo-tables
- **CREATE VIEW / DROP VIEW** with full SELECT FROM view expansion
- **ALTER TABLE ALTER COLUMN** with TYPE, SET/DROP DEFAULT, SET/DROP NOT NULL
- **CREATE INDEX IF NOT EXISTS** now properly checks existing indexes
- **NOT NULL constraint validation** on INSERT (was missing)
- **AsTimeSpan()** method added to SqlValue for complete TimeSpan support
- Column UNIQUE constraint enforcement (single and composite)
- Column CHECK constraint enforcement (INSERT and UPDATE)
- FOREIGN KEY constraint enforcement (column and table-level)
- Composite PRIMARY KEY and UNIQUE validation
- ALTER TABLE data migration (ADD/DROP/RENAME)

## Priority Items for Parser

1. CTE (WITH ... AS) - currently throws NotImplementedException
2. Window functions (OVER) - not started
3. Set operations (UNION, INTERSECT, EXCEPT) - currently throws NotImplementedException
4. Transaction statements - currently throws NotImplementedException

## Priority Items for Engine (ADO.NET/EF Core Critical)

### CRITICAL (Blocking Production Use)
1. **Transaction management** (BEGIN/COMMIT/ROLLBACK) - no ACID without this
2. **Parameter binding** (@param, :param, ?, $1) - required for ADO.NET

### HIGH (Required for EF Core)
3. **ON DELETE/UPDATE CASCADE** - referential actions (parsed but not enforced)

### MEDIUM
5. CTE execution
6. Set operations execution (UNION, INTERSECT, EXCEPT)

### LOW
7. CREATE/DROP TRIGGER
8. CREATE/DROP/ALTER SEQUENCE
