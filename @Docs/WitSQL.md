# WitSQL Language Specification

**Version:** 1.0  
**Status:** Draft  

WitSQL is the SQL dialect supported by WitDB database engine. It is designed to be compatible with SQLite while leveraging .NET's rich type system.

---

## 1. Data Types

WitSQL supports the full range of .NET types for seamless integration. Types are mapped to efficient binary storage formats.

### 1.1 Null Type

| WitSQL Type | .NET Type | Storage | Description                 |
| ----------- | --------- | ------- | --------------------------- |
| `NULL`      | `null`    | 0 bytes | Represents absence of value |

### 1.2 Integer Types

| WitSQL Type | Alias              | .NET Type | Storage           | Range             |
| ----------- | ------------------ | --------- | ----------------- | ----------------- |
| `TINYINT`   | `INT8`             | `sbyte`   | 1 byte fixed      | -128 to 127       |
| `UTINYINT`  | `UINT8`            | `byte`    | 1 byte fixed      | 0 to 255          |
| `SMALLINT`  | `INT16`            | `short`   | 2 bytes fixed     | -32,768 to 32,767 |
| `USMALLINT` | `UINT16`           | `ushort`  | 2 bytes fixed     | 0 to 65,535       |
| `INT`       | `INT32`, `INTEGER` | `int`     | VarInt 1-5 bytes  | -2³¹ to 2³¹-1     |
| `UINT`      | `UINT32`           | `uint`    | VarInt 1-5 bytes  | 0 to 2³²-1        |
| `BIGINT`    | `INT64`, `LONG`    | `long`    | VarInt 1-10 bytes | -2⁶³ to 2⁶³-1     |
| `UBIGINT`   | `UINT64`, `ULONG`  | `ulong`   | VarInt 1-10 bytes | 0 to 2⁶⁴-1        |

### 1.3 Floating-Point Types

| WitSQL Type | Alias              | .NET Type | Storage  | Precision    |
| ----------- | ------------------ | --------- | -------- | ------------ |
| `FLOAT16`   | `HALF`             | `Half`    | 2 bytes  | ~3 digits    |
| `FLOAT`     | `FLOAT32`, `REAL`  | `float`   | 4 bytes  | ~7 digits    |
| `DOUBLE`    | `FLOAT64`          | `double`  | 8 bytes  | ~15 digits   |
| `DECIMAL`   | `MONEY`, `NUMERIC` | `decimal` | 16 bytes | 28-29 digits |

### 1.4 Boolean Type

| WitSQL Type | Alias  | .NET Type | Storage | Values          |
| ----------- | ------ | --------- | ------- | --------------- |
| `BOOLEAN`   | `BOOL` | `bool`    | 1 byte  | `TRUE`, `FALSE` |

### 1.5 Date and Time Types

| WitSQL Type      | Alias       | .NET Type        | Storage  | Description                     |
| ---------------- | ----------- | ---------------- | -------- | ------------------------------- |
| `DATE`           | `DATEONLY`  | `DateOnly`       | 4 bytes  | Date only (no time)             |
| `TIME`           | `TIMEONLY`  | `TimeOnly`       | 8 bytes  | Time only (no date)             |
| `DATETIME`       | `TIMESTAMP` | `DateTime`       | 8 bytes  | UTC date and time               |
| `DATETIMEOFFSET` | -           | `DateTimeOffset` | 10 bytes | Date, time, and timezone offset |
| `INTERVAL`       | `TIMESPAN`  | `TimeSpan`       | 8 bytes  | Duration/time interval          |

### 1.6 Unique Identifier

| WitSQL Type | Alias                      | .NET Type | Storage  | Description                |
| ----------- | -------------------------- | --------- | -------- | -------------------------- |
| `GUID`      | `UUID`, `UNIQUEIDENTIFIER` | `Guid`    | 16 bytes | Globally unique identifier |

### 1.7 String Types

| WitSQL Type   | .NET Type | Storage            | Description                              |
| ------------- | --------- | ------------------ | ---------------------------------------- |
| `CHAR(n)`     | `string`  | n bytes fixed      | Fixed-length UTF-8 string                |
| `VARCHAR(n)`  | `string`  | VarInt + bytes     | Variable-length UTF-8 string (max n)     |
| `TEXT`        | `string`  | VarInt + bytes     | Variable-length UTF-8 string (unlimited) |
| `NCHAR(n)`    | `string`  | Same as CHAR(n)    | Alias for CHAR (UTF-8 native)            |
| `NVARCHAR(n)` | `string`  | Same as VARCHAR(n) | Alias for VARCHAR                        |
| `NTEXT`       | `string`  | Same as TEXT       | Alias for TEXT                           |

### 1.8 Binary Types

| WitSQL Type    | .NET Type | Storage        | Description                        |
| -------------- | --------- | -------------- | ---------------------------------- |
| `BINARY(n)`    | `byte[]`  | n bytes fixed  | Fixed-length binary data           |
| `VARBINARY(n)` | `byte[]`  | VarInt + bytes | Variable-length binary (max n)     |
| `BLOB`         | `byte[]`  | VarInt + bytes | Variable-length binary (unlimited) |

---

## 2. DDL Statements (Data Definition Language)

### 2.1 CREATE TABLE

```sql
CREATE TABLE [IF NOT EXISTS] table_name (
    column_definition [, column_definition ...]
    [, table_constraint ...]
);

column_definition:
    column_name data_type [column_constraint ...]

column_constraint:
    NOT NULL
  | NULL
  | PRIMARY KEY [AUTOINCREMENT]
  | UNIQUE
  | DEFAULT literal_value
  | DEFAULT (expression)
  | CHECK (expression)
  | REFERENCES foreign_table (foreign_column) 
      [ON DELETE action] [ON UPDATE action]

table_constraint:
    PRIMARY KEY (column_list)
  | UNIQUE (column_list)
  | FOREIGN KEY (column_list) REFERENCES foreign_table (column_list)
      [ON DELETE action] [ON UPDATE action]
  | CHECK (expression)

action:
    NO ACTION | RESTRICT | CASCADE | SET NULL | SET DEFAULT
```

**Examples:**

```sql
CREATE TABLE Users (
    Id GUID PRIMARY KEY,
    Username VARCHAR(100) NOT NULL UNIQUE,
    Email VARCHAR(255) NOT NULL,
    PasswordHash BINARY(64) NOT NULL,
    CreatedAt DATETIME DEFAULT NOW(),
    IsActive BOOLEAN DEFAULT TRUE,
    Age TINYINT CHECK (Age >= 0 AND Age <= 150)
);

CREATE TABLE Orders (
    Id BIGINT PRIMARY KEY AUTOINCREMENT,
    UserId GUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    TotalAmount DECIMAL NOT NULL,
    OrderDate DATETIME NOT NULL,
    Status VARCHAR(20) DEFAULT 'pending'
);

CREATE TABLE IF NOT EXISTS Logs (
    Id BIGINT PRIMARY KEY AUTOINCREMENT,
    Message TEXT NOT NULL,
    Level INT NOT NULL,
    Timestamp DATETIME DEFAULT NOW()
);
```

### 2.2 DROP TABLE

```sql
DROP TABLE [IF EXISTS] table_name;
```

**Examples:**

```sql
DROP TABLE Users;
DROP TABLE IF EXISTS TempData;
```

### 2.3 ALTER TABLE

```sql
ALTER TABLE table_name
    ADD [COLUMN] column_name data_type [column_constraints]
  | DROP [COLUMN] column_name
  | RENAME TO new_table_name
  | RENAME [COLUMN] old_column_name TO new_column_name
  | ALTER [COLUMN] column_name SET DEFAULT value
  | ALTER [COLUMN] column_name DROP DEFAULT
  | ALTER [COLUMN] column_name SET NOT NULL
  | ALTER [COLUMN] column_name DROP NOT NULL;
```

**Examples:**

```sql
ALTER TABLE Users ADD COLUMN LastLoginAt DATETIME;
ALTER TABLE Users DROP COLUMN Age;
ALTER TABLE Users RENAME TO Accounts;
ALTER TABLE Users RENAME COLUMN Username TO Login;
ALTER TABLE Users ALTER COLUMN Email SET NOT NULL;
```

### 2.4 CREATE INDEX

```sql
CREATE [UNIQUE] INDEX [IF NOT EXISTS] index_name
    ON table_name (column_name [ASC | DESC] [, ...]);
```

**Examples:**

```sql
CREATE INDEX IX_Users_Email ON Users (Email);
CREATE UNIQUE INDEX IX_Users_Username ON Users (Username);
CREATE INDEX IX_Orders_Date ON Orders (OrderDate DESC);
CREATE INDEX IX_Orders_User_Date ON Orders (UserId, OrderDate DESC);
```

### 2.5 DROP INDEX

```sql
DROP INDEX [IF EXISTS] index_name;
```

### 2.6 CREATE VIEW

```sql
CREATE VIEW [IF NOT EXISTS] view_name [(column_list)] AS
    select_statement;
```

**Example:**

```sql
CREATE VIEW ActiveUsers AS
    SELECT Id, Username, Email 
    FROM Users 
    WHERE IsActive = TRUE;
```

### 2.7 DROP VIEW

```sql
DROP VIEW [IF EXISTS] view_name;
```

### 2.8 CREATE TRIGGER

```sql
CREATE TRIGGER [IF NOT EXISTS] trigger_name
    {BEFORE | AFTER | INSTEAD OF} {INSERT | UPDATE [OF column_list] | DELETE}
    ON table_name
    [FOR EACH ROW]
    [WHEN (condition)]
    BEGIN
        sql_statements
    END;
```

**Trigger Timing:**
- `BEFORE` - Fires before the operation; can modify NEW values or cancel operation
- `AFTER` - Fires after successful operation; for auditing/logging
- `INSTEAD OF` - Replaces the operation entirely; typically used with views

**Trigger Events:**
- `INSERT` - Fires on new row insertion
- `UPDATE [OF col1, col2, ...]` - Fires on row update (optionally only for specific columns)
- `DELETE` - Fires on row deletion

**OLD/NEW Pseudo-Tables:**
- `OLD.column_name` - Previous value (available in UPDATE and DELETE triggers)
- `NEW.column_name` - New value (available in INSERT and UPDATE triggers)
- In BEFORE triggers, modifying `NEW.column_name` changes the value to be inserted/updated

**Examples:**

```sql
-- Audit trigger logging changes
CREATE TRIGGER AuditUserUpdates
    AFTER UPDATE ON Users
    FOR EACH ROW
    BEGIN
        INSERT INTO AuditLog (TableName, RowId, OldValue, NewValue, ChangedAt)
        VALUES ('Users', OLD.Id, OLD.Name, NEW.Name, NOW());
    END;

-- Auto-update timestamp
CREATE TRIGGER UpdateTimestamp
    BEFORE UPDATE ON Users
    FOR EACH ROW
    BEGIN
        SET NEW.UpdatedAt = NOW();
    END;

-- Conditional trigger
CREATE TRIGGER PreventNegativeBalance
    BEFORE UPDATE ON Accounts
    FOR EACH ROW
    WHEN (NEW.Balance < 0)
    BEGIN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Balance cannot be negative';
    END;

-- INSTEAD OF trigger for views
CREATE TRIGGER InsertIntoUserView
    INSTEAD OF INSERT ON ActiveUsersView
    FOR EACH ROW
    BEGIN
        INSERT INTO Users (Name, Email, IsActive)
        VALUES (NEW.Name, NEW.Email, TRUE);
    END;
```

### 2.9 DROP TRIGGER

```sql
DROP TRIGGER [IF EXISTS] trigger_name;
```

---

## 3. DML Statements (Data Manipulation Language)

### 3.1 SELECT

```sql
SELECT [DISTINCT | ALL] select_list
FROM table_reference [, table_reference ...]
[WHERE condition]
[GROUP BY expression [, expression ...]]
[HAVING condition]
[ORDER BY expression [ASC | DESC] [NULLS {FIRST | LAST}] [, ...]]
[LIMIT count [OFFSET offset]];

select_list:
    * 
  | expression [AS alias] [, ...]

table_reference:
    table_name [AS alias]
  | table_reference join_type table_reference ON condition
  | (select_statement) AS alias

join_type:
    [INNER] JOIN
  | LEFT [OUTER] JOIN
  | RIGHT [OUTER] JOIN
  | FULL [OUTER] JOIN
  | CROSS JOIN
```

**Examples:**

```sql
-- Simple select
SELECT * FROM Users;

-- Select with conditions
SELECT Id, Username, Email 
FROM Users 
WHERE IsActive = TRUE AND Age >= 18
ORDER BY Username ASC;

-- Select with alias and limit
SELECT u.Username AS Name, u.Email
FROM Users AS u
WHERE u.CreatedAt > '2024-01-01'
LIMIT 10 OFFSET 20;

-- Join example
SELECT o.Id, u.Username, o.TotalAmount
FROM Orders o
INNER JOIN Users u ON o.UserId = u.Id
WHERE o.OrderDate >= '2024-01-01'
ORDER BY o.OrderDate DESC;

-- Aggregation
SELECT Status, COUNT(*) AS OrderCount, SUM(TotalAmount) AS Total
FROM Orders
GROUP BY Status
HAVING COUNT(*) > 5;

-- Subquery
SELECT * FROM Users
WHERE Id IN (SELECT UserId FROM Orders WHERE TotalAmount > 1000);
```

### 3.2 INSERT

```sql
INSERT INTO table_name [(column_list)]
    VALUES (value_list) [, (value_list) ...];

INSERT INTO table_name [(column_list)]
    select_statement;
```

**Examples:**

```sql
-- Single row insert
INSERT INTO Users (Id, Username, Email, PasswordHash)
VALUES (NEWGUID(), 'john_doe', 'john@example.com', X'abc123...');

-- Multiple rows insert  
INSERT INTO Logs (Message, Level) VALUES 
    ('Server started', 1),
    ('Connection established', 2),
    ('Request received', 2);

-- Insert from select
INSERT INTO ArchivedOrders (Id, UserId, TotalAmount)
SELECT Id, UserId, TotalAmount 
FROM Orders 
WHERE OrderDate < '2023-01-01';
```

### 3.3 UPDATE

```sql
UPDATE table_name
SET column_name = expression [, column_name = expression ...]
[WHERE condition];
```

**Examples:**

```sql
UPDATE Users SET IsActive = FALSE WHERE LastLoginAt < '2023-01-01';

UPDATE Orders 
SET Status = 'completed', CompletedAt = NOW()
WHERE Id = 12345;

UPDATE Products 
SET Price = Price * 1.1, UpdatedAt = NOW()
WHERE CategoryId = 5;
```

### 3.4 DELETE

```sql
DELETE FROM table_name
[WHERE condition];
```

**Examples:**

```sql
DELETE FROM Logs WHERE Timestamp < '2023-01-01';
DELETE FROM Users WHERE IsActive = FALSE;
```

---

## 4. Expressions and Operators

### 4.1 Comparison Operators

| Operator              | Description           | Example                           |
| --------------------- | --------------------- | --------------------------------- |
| `=`                   | Equal                 | `Age = 25`                        |
| `<>`, `!=`            | Not equal             | `Status <> 'deleted'`             |
| `<`                   | Less than             | `Price < 100`                     |
| `<=`                  | Less than or equal    | `Quantity <= 10`                  |
| `>`                   | Greater than          | `Age > 18`                        |
| `>=`                  | Greater than or equal | `Rating >= 4.0`                   |
| `IS NULL`             | Is null               | `DeletedAt IS NULL`               |
| `IS NOT NULL`         | Is not null           | `Email IS NOT NULL`               |
| `BETWEEN x AND y`     | Range inclusive       | `Age BETWEEN 18 AND 65`           |
| `NOT BETWEEN x AND y` | Outside range         | `Price NOT BETWEEN 10 AND 20`     |
| `IN (...)`            | In list               | `Status IN ('active', 'pending')` |
| `NOT IN (...)`        | Not in list           | `Id NOT IN (1, 2, 3)`             |
| `LIKE pattern`        | Pattern match         | `Name LIKE 'John%'`               |
| `NOT LIKE pattern`    | Not pattern match     | `Email NOT LIKE '%@spam.com'`     |
| `GLOB pattern`        | Unix glob pattern     | `Filename GLOB '*.txt'`           |

**LIKE Patterns:**
- `%` - matches any sequence of characters
- `_` - matches any single character
- `ESCAPE char` - escape character for literal % or _

### 4.2 Logical Operators

| Operator | Description | Example                                |
| -------- | ----------- | -------------------------------------- |
| `AND`    | Logical and | `IsActive AND IsVerified`              |
| `OR`     | Logical or  | `Status = 'new' OR Status = 'pending'` |
| `NOT`    | Logical not | `NOT IsDeleted`                        |

Precedence: `NOT` > `AND` > `OR`

### 4.3 Arithmetic Operators

| Operator | Description    | Example            |
| -------- | -------------- | ------------------ |
| `+`      | Addition       | `Price + Tax`      |
| `-`      | Subtraction    | `Total - Discount` |
| `*`      | Multiplication | `Quantity * Price` |
| `/`      | Division       | `Total / Count`    |
| `%`      | Modulo         | `Id % 10`          |
| `-expr`  | Unary minus    | `-Balance`         |
| `+expr`  | Unary plus     | `+Value`           |

### 4.4 String Operators

| Operator | Description   | Example                            |
| -------- | ------------- | ---------------------------------- |
| `\|\|`   | Concatenation | `FirstName \|\| ' ' \|\| LastName` |

### 4.5 Bitwise Operators

| Operator | Description | Example         |
| -------- | ----------- | --------------- |
| `&`      | Bitwise AND | `Flags & 0x0F`  |
| `\|`     | Bitwise OR  | `Flags \| 0x10` |
| `~`      | Bitwise NOT | `~Flags`        |
| `<<`     | Left shift  | `Value << 2`    |
| `>>`     | Right shift | `Value >> 2`    |

### 4.6 Conditional Expressions

```sql
-- CASE expression
CASE expression
    WHEN value1 THEN result1
    WHEN value2 THEN result2
    ...
    [ELSE default_result]
END

-- Searched CASE
CASE
    WHEN condition1 THEN result1
    WHEN condition2 THEN result2
    ...
    [ELSE default_result]
END

-- COALESCE - returns first non-null
COALESCE(expr1, expr2, ...)

-- NULLIF - returns NULL if equal
NULLIF(expr1, expr2)

-- IIF - inline if (shorthand for CASE)
IIF(condition, true_value, false_value)

-- CAST - type conversion
CAST(expression AS data_type)
```

**Examples:**

```sql
SELECT 
    Username,
    CASE Status
        WHEN 'active' THEN 'Active'
        WHEN 'pending' THEN 'Pending'
        ELSE 'Unknown'
    END AS StatusText
FROM Users;

SELECT COALESCE(Nickname, Username, 'Anonymous') AS DisplayName FROM Users;

SELECT IIF(IsActive, 'Yes', 'No') AS ActiveText FROM Users;
```

---

## 5. Built-in Functions

### 5.1 Aggregate Functions

| Function               | Description           | Example                    |
| ---------------------- | --------------------- | -------------------------- |
| `COUNT(*)`             | Count all rows        | `COUNT(*)`                 |
| `COUNT(expr)`          | Count non-null values | `COUNT(Email)`             |
| `COUNT(DISTINCT expr)` | Count distinct values | `COUNT(DISTINCT Status)`   |
| `SUM(expr)`            | Sum of values         | `SUM(TotalAmount)`         |
| `AVG(expr)`            | Average               | `AVG(Price)`               |
| `MIN(expr)`            | Minimum value         | `MIN(CreatedAt)`           |
| `MAX(expr)`            | Maximum value         | `MAX(Price)`               |
| `GROUP_CONCAT(expr)`   | Concatenate values    | `GROUP_CONCAT(Name, ', ')` |

### 5.2 String Functions

| Function                     | Description                        | Example                          |
| ---------------------------- | ---------------------------------- | -------------------------------- |
| `LENGTH(str)`                | String length in characters        | `LENGTH(Username)`               |
| `CHAR_LENGTH(str)`           | Same as LENGTH                     | `CHAR_LENGTH(str)`               |
| `OCTET_LENGTH(str)`          | String length in bytes             | `OCTET_LENGTH(str)`              |
| `UPPER(str)`                 | Convert to uppercase               | `UPPER(Username)`                |
| `LOWER(str)`                 | Convert to lowercase               | `LOWER(Email)`                   |
| `SUBSTR(str, start, len)`    | Substring                          | `SUBSTR(Name, 1, 10)`            |
| `SUBSTRING(str, start, len)` | Same as SUBSTR                     |                                  |
| `LEFT(str, n)`               | Left n characters                  | `LEFT(Title, 20)`                |
| `RIGHT(str, n)`              | Right n characters                 | `RIGHT(Code, 4)`                 |
| `TRIM(str)`                  | Remove leading/trailing whitespace | `TRIM(Input)`                    |
| `LTRIM(str)`                 | Remove leading whitespace          | `LTRIM(Input)`                   |
| `RTRIM(str)`                 | Remove trailing whitespace         | `RTRIM(Input)`                   |
| `TRIM(chars FROM str)`       | Remove specific characters         | `TRIM('x' FROM str)`             |
| `REPLACE(str, old, new)`     | Replace occurrences                | `REPLACE(Text, 'old', 'new')`    |
| `INSTR(str, substr)`         | Find position (1-based)            | `INSTR(Email, '@')`              |
| `POSITION(substr IN str)`    | Same as INSTR                      |                                  |
| `CONCAT(str1, str2, ...)`    | Concatenate strings                | `CONCAT(First, ' ', Last)`       |
| `CONCAT_WS(sep, str1, ...)`  | Concatenate with separator         | `CONCAT_WS(', ', City, Country)` |
| `REVERSE(str)`               | Reverse string                     | `REVERSE(str)`                   |
| `REPEAT(str, n)`             | Repeat string n times              | `REPEAT('*', 10)`                |
| `SPACE(n)`                   | Generate n spaces                  | `SPACE(5)`                       |
| `LPAD(str, len, pad)`        | Left pad                           | `LPAD(Id, 10, '0')`              |
| `RPAD(str, len, pad)`        | Right pad                          | `RPAD(Name, 20, ' ')`            |
| `FORMAT(str, args...)`       | Format string                      | `FORMAT('Hello {0}', Name)`      |

### 5.3 Numeric Functions

| Function                        | Description             | Example                       |
| ------------------------------- | ----------------------- | ----------------------------- |
| `ABS(x)`                        | Absolute value          | `ABS(-5)` → `5`               |
| `SIGN(x)`                       | Sign (-1, 0, 1)         | `SIGN(-5)` → `-1`             |
| `ROUND(x, n)`                   | Round to n decimals     | `ROUND(3.14159, 2)` → `3.14`  |
| `FLOOR(x)`                      | Round down              | `FLOOR(3.7)` → `3`            |
| `CEIL(x)` / `CEILING(x)`        | Round up                | `CEIL(3.2)` → `4`             |
| `TRUNC(x, n)`                   | Truncate to n decimals  | `TRUNC(3.14159, 2)` → `3.14`  |
| `MOD(x, y)`                     | Modulo                  | `MOD(10, 3)` → `1`            |
| `POWER(x, y)`                   | x raised to y           | `POWER(2, 10)` → `1024`       |
| `SQRT(x)`                       | Square root             | `SQRT(16)` → `4`              |
| `EXP(x)`                        | e raised to x           | `EXP(1)` → `2.718...`         |
| `LOG(x)`                        | Natural logarithm       | `LOG(10)` → `2.302...`        |
| `LOG10(x)`                      | Base-10 logarithm       | `LOG10(100)` → `2`            |
| `LOG2(x)`                       | Base-2 logarithm        | `LOG2(8)` → `3`               |
| `SIN(x)`, `COS(x)`, `TAN(x)`    | Trigonometric           | `SIN(0)` → `0`                |
| `ASIN(x)`, `ACOS(x)`, `ATAN(x)` | Inverse trig            |                               |
| `ATAN2(y, x)`                   | Two-argument arctangent |                               |
| `PI()`                          | Pi constant             | `PI()` → `3.14159...`         |
| `DEGREES(rad)`                  | Radians to degrees      | `DEGREES(PI())` → `180`       |
| `RADIANS(deg)`                  | Degrees to radians      | `RADIANS(180)` → `3.14159...` |
| `RANDOM()`                      | Random float [0, 1)     | `RANDOM()`                    |
| `RANDOM(min, max)`              | Random in range         | `RANDOM(1, 100)`              |

### 5.4 Date and Time Functions

| Function                    | Description            | Example                       |
| --------------------------- | ---------------------- | ----------------------------- |
| `NOW()`                     | Current UTC datetime   | `NOW()`                       |
| `CURRENT_TIMESTAMP`         | Same as NOW()          |                               |
| `CURRENT_DATE`              | Current UTC date       | `CURRENT_DATE`                |
| `CURRENT_TIME`              | Current UTC time       | `CURRENT_TIME`                |
| `DATE(expr)`                | Extract date part      | `DATE(CreatedAt)`             |
| `TIME(expr)`                | Extract time part      | `TIME(CreatedAt)`             |
| `YEAR(dt)`                  | Extract year           | `YEAR(CreatedAt)` → `2024`    |
| `MONTH(dt)`                 | Extract month (1-12)   | `MONTH(CreatedAt)` → `12`     |
| `DAY(dt)`                   | Extract day of month   | `DAY(CreatedAt)` → `15`       |
| `HOUR(dt)`                  | Extract hour (0-23)    | `HOUR(CreatedAt)`             |
| `MINUTE(dt)`                | Extract minute         | `MINUTE(CreatedAt)`           |
| `SECOND(dt)`                | Extract second         | `SECOND(CreatedAt)`           |
| `DAYOFWEEK(dt)`             | Day of week (1=Sunday) | `DAYOFWEEK(CreatedAt)`        |
| `DAYOFYEAR(dt)`             | Day of year (1-366)    | `DAYOFYEAR(CreatedAt)`        |
| `WEEKOFYEAR(dt)`            | Week of year           | `WEEKOFYEAR(CreatedAt)`       |
| `QUARTER(dt)`               | Quarter (1-4)          | `QUARTER(CreatedAt)`          |
| `DATEADD(part, n, dt)`      | Add interval           | `DATEADD('day', 7, NOW())`    |
| `DATEDIFF(part, dt1, dt2)`  | Difference             | `DATEDIFF('day', Start, End)` |
| `STRFTIME(format, dt)`      | Format datetime        | `STRFTIME('%Y-%m-%d', NOW())` |
| `MAKEDATE(year, dayofyear)` | Create date            | `MAKEDATE(2024, 100)`         |
| `MAKETIME(h, m, s)`         | Create time            | `MAKETIME(14, 30, 0)`         |

**DATEADD/DATEDIFF Parts:** `'year'`, `'month'`, `'day'`, `'hour'`, `'minute'`, `'second'`, `'millisecond'`

### 5.5 ID Generation Functions

| Function                  | Description             | Example                     |
| ------------------------- | ----------------------- | --------------------------- |
| `NEWGUID()`               | Generate new GUID       | `NEWGUID()`                 |
| `NEWUUID()`               | Alias for NEWGUID       | `NEWUUID()`                 |
| `INCREMENT(sequence)`     | Get next sequence value | `INCREMENT('order_id')`     |
| `LASTINCREMENT(sequence)` | Get last sequence value | `LASTINCREMENT('order_id')` |

**Examples:**

```sql
-- Using NEWGUID for primary key
INSERT INTO Users (Id, Username) VALUES (NEWGUID(), 'john');

-- Using INCREMENT for auto-incrementing ID
INSERT INTO Orders (Id, UserId, Amount) 
VALUES (INCREMENT('orders'), @UserId, @Amount);

-- Creating/resetting sequence
CREATE SEQUENCE order_id START WITH 1000;
ALTER SEQUENCE order_id RESTART WITH 5000;
DROP SEQUENCE order_id;
```

### 5.6 Conversion Functions

| Function              | Description          | Example                          |
| --------------------- | -------------------- | -------------------------------- |
| `CAST(expr AS type)`  | Convert type         | `CAST('123' AS INT)`             |
| `CONVERT(type, expr)` | Convert type (alt)   | `CONVERT(INT, '123')`            |
| `TOSTRING(expr)`      | Convert to string    | `TOSTRING(123)`                  |
| `TOINT(expr)`         | Convert to integer   | `TOINT('123')`                   |
| `TODOUBLE(expr)`      | Convert to double    | `TODOUBLE('3.14')`               |
| `TODECIMAL(expr)`     | Convert to decimal   | `TODECIMAL('123.45')`            |
| `TOBOOLEAN(expr)`     | Convert to boolean   | `TOBOOLEAN(1)`                   |
| `TODATE(expr)`        | Convert to date      | `TODATE('2024-01-01')`           |
| `TODATETIME(expr)`    | Convert to datetime  | `TODATETIME('2024-01-01 12:00')` |
| `TOGUID(expr)`        | Convert to GUID      | `TOGUID('...')`                  |
| `HEX(blob)`           | Binary to hex string | `HEX(PasswordHash)`              |
| `UNHEX(str)`          | Hex string to binary | `UNHEX('48656C6C6F')`            |
| `BASE64(blob)`        | Binary to base64     | `BASE64(Data)`                   |
| `UNBASE64(str)`       | Base64 to binary     | `UNBASE64('SGVsbG8=')`           |

### 5.7 Null Handling Functions

| Function                | Description     | Example                       |
| ----------------------- | --------------- | ----------------------------- |
| `COALESCE(expr, ...)`   | First non-null  | `COALESCE(Nick, Name, 'N/A')` |
| `NULLIF(a, b)`          | NULL if equal   | `NULLIF(Value, 0)`            |
| `IFNULL(expr, default)` | Default if null | `IFNULL(Email, 'none')`       |
| `NVL(expr, default)`    | Same as IFNULL  | `NVL(Status, 'unknown')`      |

### 5.8 System Functions

| Function              | Description               | Example                      |
| --------------------- | ------------------------- | ---------------------------- |
| `DATABASE()`          | Current database name     | `DATABASE()`                 |
| `VERSION()`           | WitDB version             | `VERSION()`                  |
| `TYPEOF(expr)`        | Type name of expression   | `TYPEOF(Column)`             |
| `ROWID`               | Internal row identifier   | `SELECT ROWID FROM Table`    |
| `CHANGES()`           | Rows affected by last DML | `SELECT CHANGES()`           |
| `LAST_INSERT_ROWID()` | Last auto-increment ID    | `SELECT LAST_INSERT_ROWID()` |

---

## 6. Common Table Expressions (CTE)

```sql
WITH cte_name [(column_list)] AS (
    select_statement
)
[, cte_name2 AS (...)]
SELECT ... FROM cte_name ...;

-- Recursive CTE
WITH RECURSIVE cte_name (column_list) AS (
    -- Anchor member
    SELECT ...
    UNION ALL
    -- Recursive member
    SELECT ... FROM cte_name WHERE ...
)
SELECT ... FROM cte_name;
```

**Examples:**

```sql
-- Simple CTE
WITH ActiveOrders AS (
    SELECT * FROM Orders WHERE Status = 'active'
)
SELECT * FROM ActiveOrders WHERE TotalAmount > 100;

-- Recursive CTE for hierarchy
WITH RECURSIVE CategoryTree (Id, Name, ParentId, Level) AS (
    -- Anchor: top-level categories
    SELECT Id, Name, ParentId, 0 AS Level
    FROM Categories
    WHERE ParentId IS NULL
    
    UNION ALL
    
    -- Recursive: child categories
    SELECT c.Id, c.Name, c.ParentId, ct.Level + 1
    FROM Categories c
    INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
)
SELECT * FROM CategoryTree ORDER BY Level, Name;
```

---

## 7. Window Functions

```sql
function_name(expr) OVER (
    [PARTITION BY expr [, ...]]
    [ORDER BY expr [ASC|DESC] [, ...]]
    [frame_clause]
)

frame_clause:
    {ROWS | RANGE} {frame_start | BETWEEN frame_start AND frame_end}

frame_start / frame_end:
    UNBOUNDED PRECEDING
  | n PRECEDING
  | CURRENT ROW
  | n FOLLOWING
  | UNBOUNDED FOLLOWING
```

### 7.1 Ranking Functions

| Function         | Description               |
| ---------------- | ------------------------- |
| `ROW_NUMBER()`   | Sequential row number     |
| `RANK()`         | Rank with gaps            |
| `DENSE_RANK()`   | Rank without gaps         |
| `NTILE(n)`       | Distribute into n buckets |
| `PERCENT_RANK()` | Relative rank (0 to 1)    |
| `CUME_DIST()`    | Cumulative distribution   |

### 7.2 Value Functions

| Function                      | Description             |
| ----------------------------- | ----------------------- |
| `FIRST_VALUE(expr)`           | First value in window   |
| `LAST_VALUE(expr)`            | Last value in window    |
| `NTH_VALUE(expr, n)`          | Nth value in window     |
| `LAG(expr, offset, default)`  | Value from previous row |
| `LEAD(expr, offset, default)` | Value from next row     |

**Examples:**

```sql
-- Row numbers within each category
SELECT 
    Name,
    Category,
    Price,
    ROW_NUMBER() OVER (PARTITION BY Category ORDER BY Price DESC) AS Rank
FROM Products;

-- Running total
SELECT 
    OrderDate,
    TotalAmount,
    SUM(TotalAmount) OVER (ORDER BY OrderDate) AS RunningTotal
FROM Orders;

-- Compare with previous value
SELECT 
    Month,
    Revenue,
    LAG(Revenue, 1) OVER (ORDER BY Month) AS PrevMonthRevenue,
    Revenue - LAG(Revenue, 1) OVER (ORDER BY Month) AS Change
FROM MonthlyRevenue;
```

---

## 8. Set Operations

```sql
-- Union (remove duplicates)
SELECT ... UNION SELECT ...

-- Union All (keep duplicates)
SELECT ... UNION ALL SELECT ...

-- Intersection
SELECT ... INTERSECT SELECT ...

-- Difference
SELECT ... EXCEPT SELECT ...
```

**Example:**

```sql
SELECT Email FROM Customers
UNION
SELECT Email FROM Subscribers
ORDER BY Email;
```

---

## 9. Transactions

```sql
BEGIN [TRANSACTION];
COMMIT;
ROLLBACK;

-- Savepoints
SAVEPOINT savepoint_name;
RELEASE SAVEPOINT savepoint_name;
ROLLBACK TO SAVEPOINT savepoint_name;
```

---

## 10. Comments

```sql
-- Single line comment

/* Multi-line
   comment */
```

---

## 11. Parameters

WitSQL supports named and positional parameters:

```sql
-- Named parameters
SELECT * FROM Users WHERE Id = @UserId;
SELECT * FROM Users WHERE Name = :name;

-- Positional parameters  
SELECT * FROM Users WHERE Id = ?;
SELECT * FROM Users WHERE Name = $1 AND Age = $2;
```

---

## 12. Reserved Words

The following are reserved keywords in WitSQL:

```
ADD, ALL, ALTER, AND, AS, ASC, AUTOINCREMENT,
BEGIN, BETWEEN, BINARY, BLOB, BOOLEAN, BY,
CASCADE, CASE, CAST, CHAR, CHECK, COLUMN, COMMIT, CONSTRAINT, CREATE, CROSS, CURRENT,
DATE, DATETIME, DECIMAL, DEFAULT, DELETE, DESC, DISTINCT, DOUBLE, DROP,
EACH, ELSE, END, ESCAPE, EXCEPT, EXISTS,
FALSE, FLOAT, FOR, FOREIGN, FROM, FULL,
GROUP, GUID, HAVING,
IF, IN, INDEX, INNER, INSERT, INT, INTEGER, INTERSECT, INTERVAL, INTO, IS,
JOIN,
KEY,
LEFT, LIKE, LIMIT,
MAX, MIN,
NOT, NULL, NULLS,
OFFSET, ON, OR, ORDER, OUTER, OVER,
PARTITION, PRIMARY,
REAL, RECURSIVE, REFERENCES, RENAME, RESTRICT, RIGHT, ROLLBACK, ROW, ROWS,
SAVEPOINT, SELECT, SEQUENCE, SET, SMALLINT,
TABLE, TEXT, THEN, TIME, TIMESTAMP, TO, TRANSACTION, TRIGGER, TRUE,
UNIQUE, UNION, UPDATE, USING,
VALUES, VARCHAR, VIEW,
WHEN, WHERE, WITH
```

---

**Document Version History:**
- v1.0 (2024-12-12): Initial specification
