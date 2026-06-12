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
    VALUES (value_list) [, (value_list) ...]
    [RETURNING select_list];

INSERT INTO table_name [(column_list)]
    select_statement
    [RETURNING select_list];
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

-- Insert with RETURNING (get generated values)
INSERT INTO Users (Username, Email) 
VALUES ('john', 'john@example.com')
RETURNING Id, CreatedAt;

-- Insert returning all columns
INSERT INTO Orders (UserId, Amount) VALUES (@UserId, @Amount)
RETURNING *;
```

### 3.3 UPDATE

```sql
UPDATE table_name
SET column_name = expression [, column_name = expression ...]
[WHERE condition]
[RETURNING select_list];
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

-- Update with RETURNING
UPDATE Users 
SET Name = 'Jane', UpdatedAt = NOW() 
WHERE Id = @Id
RETURNING Id, Name, UpdatedAt;
```

### 3.4 DELETE

```sql
DELETE FROM table_name
[WHERE condition]
[RETURNING select_list];
```

**Examples:**

```sql
DELETE FROM Logs WHERE Timestamp < '2023-01-01';
DELETE FROM Users WHERE IsActive = FALSE;

-- Delete with RETURNING (get deleted records)
DELETE FROM Users WHERE Id = @Id
RETURNING Id, Username, Email;
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
SELECT * FROM Users WHERE MigrationId = $id;

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
DATE, DATETIME, DAY, DECIMAL, DEFAULT, DELETE, DESC, DISTINCT, DOUBLE, DROP,
EACH, ELSE, END, ESCAPE, EXCEPT, EXISTS,
FALSE, FLOAT, FOR, FOREIGN, FROM, FULL,
GROUP, GUID, HAVING, HOUR,
IF, IN, INDEX, INNER, INSERT, INT, INTEGER, INTERSECT, INTERVAL, INTO, IS,
JOIN,
KEY,
LEFT, LIKE, LIMIT,
MAX, MIN, MINUTE, MONTH,
NOT, NULL, NULLS,
OFFSET, ON, OR, ORDER, OUTER, OVER,
PARTITION, PRIMARY,
REAL, RECURSIVE, REFERENCES, RENAME, RESTRICT, RETURNING, RIGHT, ROLLBACK, ROW, ROWS,
SAVEPOINT, SECOND, SELECT, SEQUENCE, SET, SMALLINT,
TABLE, TEXT, THEN, TIME, TIMESTAMP, TO, TRANSACTION, TRIGGER, TRUE,
UNIQUE, UNION, UPDATE, USING,
VALUES, VARCHAR, VIEW,
WHEN, WHERE, WITH, YEAR
```

---

## 13. Schema Information

### 13.1 INFORMATION_SCHEMA Views

WitSQL provides INFORMATION_SCHEMA views for metadata discovery:

```sql
-- Tables metadata
SELECT * FROM INFORMATION_SCHEMA.TABLES;
-- Columns: TABLE_NAME, TABLE_TYPE

-- Columns metadata  
SELECT * FROM INFORMATION_SCHEMA.COLUMNS;
-- Columns: TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, COLUMN_DEFAULT,
--          IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
--          NUMERIC_PRECISION, NUMERIC_SCALE

-- Primary keys
SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE;

-- Foreign keys
SELECT * FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS;

-- Indexes
SELECT * FROM INFORMATION_SCHEMA.INDEXES;

-- Views
SELECT * FROM INFORMATION_SCHEMA.VIEWS;
```

### 13.2 Extended Type Specifications

```sql
-- DECIMAL with precision and scale
column_name DECIMAL(precision, scale)

-- VARCHAR with max length
column_name VARCHAR(max_length)

-- Examples
Price DECIMAL(18, 4)        -- 18 total digits, 4 after decimal
Email VARCHAR(255)          -- max 255 characters
```

### 13.3 Named Constraints

```sql
CREATE TABLE Orders (
    Id BIGINT PRIMARY KEY,
    UserId GUID NOT NULL,
    Amount DECIMAL(18, 2) NOT NULL,
    
    CONSTRAINT FK_Orders_Users 
        FOREIGN KEY (UserId) REFERENCES Users(Id),
    
    CONSTRAINT CHK_Orders_Amount 
        CHECK (Amount > 0),
    
    CONSTRAINT UQ_Orders_Code 
        UNIQUE (OrderCode)
);

-- Drop constraint
ALTER TABLE Orders DROP CONSTRAINT FK_Orders_Users;
```

---

## 14. Transactions and Isolation

### 14.1 Isolation Levels

```sql
-- Set isolation level for transaction
SET TRANSACTION ISOLATION LEVEL level;

-- Supported levels:
--   READ UNCOMMITTED
--   READ COMMITTED (default)
--   REPEATABLE READ
--   SERIALIZABLE
--   SNAPSHOT

-- Example
BEGIN TRANSACTION;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
-- statements
COMMIT;
```

### 14.2 Locking Hints

```sql
-- Row-level locks in SELECT
SELECT * FROM Orders WHERE Id = 1 FOR UPDATE;
SELECT * FROM Orders WHERE Id = 1 FOR SHARE;

-- No wait / skip locked
SELECT * FROM Orders WHERE Status = 'pending' 
FOR UPDATE NOWAIT;

SELECT * FROM Orders WHERE Status = 'pending' 
FOR UPDATE SKIP LOCKED;
```

---

## 15. Concurrency Control

### 15.1 Row Version Type

```sql
-- ROWVERSION type for optimistic concurrency
CREATE TABLE Products (
    Id BIGINT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Price DECIMAL(18, 2) NOT NULL,
    Version ROWVERSION NOT NULL
);

-- Update with concurrency check
UPDATE Products 
SET Name = 'New Name', Price = 99.99
WHERE Id = 1 AND Version = @OldVersion;
```

| WitSQL Type  | .NET Type | Storage  | Description                      |
| ------------ | --------- | -------- | -------------------------------- |
| `ROWVERSION` | `byte[]`  | 8 bytes  | Auto-incrementing version stamp  |
| `TIMESTAMP`  | `byte[]`  | 8 bytes  | Alias for ROWVERSION             |

---

## 16. UPSERT and MERGE Operations

### 16.1 INSERT OR REPLACE

```sql
INSERT OR REPLACE INTO table_name (columns)
VALUES (values);
```

### 16.2 INSERT ON CONFLICT

```sql
INSERT INTO table_name (columns)
VALUES (values)
ON CONFLICT (conflict_columns) DO UPDATE 
SET column = expression [, ...];

INSERT INTO table_name (columns)
VALUES (values)
ON CONFLICT (conflict_columns) DO NOTHING;
```

### 16.3 MERGE Statement

```sql
MERGE INTO target_table AS target
USING source_table AS source
ON (target.key = source.key)
WHEN MATCHED THEN
    UPDATE SET target.col = source.col
WHEN NOT MATCHED THEN
    INSERT (columns) VALUES (source.columns);
```

**Examples:**

```sql
-- Upsert user
INSERT INTO Users (Id, Name, Email)
VALUES (@Id, @Name, @Email)
ON CONFLICT (Id) DO UPDATE 
SET Name = EXCLUDED.Name, Email = EXCLUDED.Email;

-- Insert if not exists
INSERT INTO Settings (Key, Value)
VALUES ('theme', 'dark')
ON CONFLICT (Key) DO NOTHING;
```

---

## 17. Additional DML Statements

### 17.1 TRUNCATE TABLE

```sql
TRUNCATE TABLE table_name;
-- Removes all rows, resets auto-increment
-- Faster than DELETE, cannot be rolled back
```

### 17.2 UPDATE with FROM

```sql
UPDATE target_table
SET column = expression
FROM other_table
WHERE condition;
```

### 17.3 DELETE with FROM

```sql
DELETE FROM target_table
USING other_table
WHERE condition;
```

---

## 18. Subquery Operators

### 18.1 EXISTS

```sql
SELECT * FROM Orders o
WHERE EXISTS (
    SELECT 1 FROM OrderItems oi 
    WHERE oi.OrderId = o.Id
);

SELECT * FROM Customers c
WHERE NOT EXISTS (
    SELECT 1 FROM Orders o 
    WHERE o.CustomerId = c.Id
);
```

### 18.2 ANY / SOME / ALL

```sql
-- ANY/SOME - true if any row matches
SELECT * FROM Products 
WHERE Price > ANY (SELECT Price FROM DiscountedProducts);

-- ALL - true if all rows match
SELECT * FROM Products 
WHERE Price > ALL (SELECT Price FROM BudgetProducts);
```

---

## 19. Advanced Index Features

### 19.1 Partial (Filtered) Indexes

```sql
CREATE INDEX IX_Orders_Pending 
ON Orders (OrderDate)
WHERE Status = 'pending';
```

### 19.2 Expression Indexes

```sql
CREATE INDEX IX_Users_LowerEmail 
ON Users (LOWER(Email));

CREATE INDEX IX_Orders_Year 
ON Orders (YEAR(OrderDate));
```

### 19.3 Covering Indexes (INCLUDE)

```sql
CREATE INDEX IX_Orders_Customer 
ON Orders (CustomerId)
INCLUDE (OrderDate, TotalAmount);
```

---

## 20. Computed Columns

```sql
CREATE TABLE Orders (
    Id BIGINT PRIMARY KEY,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18, 2) NOT NULL,
    
    -- Computed column (stored)
    TotalPrice AS (Quantity * UnitPrice) STORED,
    
    -- Computed column (virtual)
    Discount AS (TotalPrice * 0.1)
);

-- Alter table to add computed column
ALTER TABLE Orders 
ADD COLUMN SubTotal AS (Quantity * UnitPrice);
```

---

## 21. JSON Support

### 21.1 JSON Type

| WitSQL Type | .NET Type      | Storage        | Description        |
| ----------- | -------------- | -------------- | ------------------ |
| `JSON`      | `JsonDocument` | VarInt + bytes | JSON document      |
| `JSONB`     | `JsonDocument` | VarInt + bytes | Binary JSON format |

### 21.2 JSON Functions

| Function                          | Description                    | Example                               |
| --------------------------------- | ------------------------------ | ------------------------------------- |
| `JSON_VALUE(json, path)`          | Extract scalar value           | `JSON_VALUE(Data, '$.name')`          |
| `JSON_QUERY(json, path)`          | Extract object/array           | `JSON_QUERY(Data, '$.items')`         |
| `JSON_EXTRACT(json, path)`        | Extract any value              | `JSON_EXTRACT(Data, '$.id')`          |
| `JSON_SET(json, path, value)`     | Set value at path              | `JSON_SET(Data, '$.status', 'done')` |
| `JSON_INSERT(json, path, value)`  | Insert if not exists           |                                       |
| `JSON_REPLACE(json, path, value)` | Replace if exists              |                                       |
| `JSON_REMOVE(json, path)`         | Remove at path                 | `JSON_REMOVE(Data, '$.temp')`         |
| `JSON_TYPE(json)`                 | Get JSON value type            | `JSON_TYPE(Data)` → `'object'`        |
| `JSON_VALID(str)`                 | Check if valid JSON            | `JSON_VALID('{"a":1}')` → `TRUE`      |
| `JSON_ARRAY(values...)`           | Create JSON array              | `JSON_ARRAY(1, 2, 3)`                 |
| `JSON_OBJECT(pairs...)`           | Create JSON object             | `JSON_OBJECT('a', 1, 'b', 2)`         |

**Examples:**

```sql
CREATE TABLE Products (
    Id BIGINT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Metadata JSON
);

-- Query JSON
SELECT Name, JSON_VALUE(Metadata, '$.category') AS Category
FROM Products
WHERE JSON_VALUE(Metadata, '$.inStock') = 'true';

-- Update JSON
UPDATE Products
SET Metadata = JSON_SET(Metadata, '$.lastUpdated', NOW())
WHERE Id = 1;
```

---

## 22. User-Defined Functions

### 22.1 Scalar Functions

```sql
CREATE FUNCTION function_name (parameters)
RETURNS return_type
[DETERMINISTIC]
AS
BEGIN
    -- function body
    RETURN expression;
END;
```

### 22.2 Table-Valued Functions

```sql
CREATE FUNCTION function_name (parameters)
RETURNS TABLE (column_definitions)
AS
BEGIN
    RETURN SELECT ...;
END;
```

**Examples:**

```sql
-- Scalar function
CREATE FUNCTION FormatPrice(price DECIMAL)
RETURNS VARCHAR(20)
DETERMINISTIC
AS
BEGIN
    RETURN '$' || CAST(ROUND(price, 2) AS VARCHAR);
END;

-- Usage
SELECT Name, FormatPrice(Price) FROM Products;

-- Drop function
DROP FUNCTION [IF EXISTS] function_name;
```

---

## 23. Stored Procedures

```sql
CREATE PROCEDURE procedure_name (parameters)
AS
BEGIN
    -- procedure body
END;

-- Execute procedure
CALL procedure_name(arguments);
EXECUTE procedure_name(arguments);
```

**Example:**

```sql
CREATE PROCEDURE TransferFunds(
    @FromAccount BIGINT,
    @ToAccount BIGINT,
    @Amount DECIMAL
)
AS
BEGIN
    BEGIN TRANSACTION;
    
    UPDATE Accounts SET Balance = Balance - @Amount 
    WHERE Id = @FromAccount;
    
    UPDATE Accounts SET Balance = Balance + @Amount 
    WHERE Id = @ToAccount;
    
    COMMIT;
END;

CALL TransferFunds(1001, 1002, 500.00);
```

---

## 24. Collation

```sql
-- Column-level collation
CREATE TABLE Users (
    Id BIGINT PRIMARY KEY,
    Name VARCHAR(100) COLLATE NOCASE,
    Code VARCHAR(20) COLLATE BINARY
);

-- Expression collation
SELECT * FROM Users 
WHERE Name = 'john' COLLATE NOCASE;

ORDER BY Name COLLATE NOCASE;
```

**Supported Collations:**
- `BINARY` - byte-by-byte comparison
- `NOCASE` - case-insensitive ASCII
- `UNICODE` - Unicode-aware comparison
- `UNICODE_CI` - Unicode case-insensitive

---

## 25. Query Analysis

### 25.1 EXPLAIN

```sql
-- Show query execution plan
EXPLAIN select_statement;

-- Show detailed plan with estimates
EXPLAIN ANALYZE select_statement;

-- Show plan in different formats
EXPLAIN (FORMAT JSON) select_statement;
EXPLAIN (FORMAT TEXT) select_statement;
```

---

## 26. Database Administration

### 26.1 Database Commands

```sql
-- Create database (for multi-database support)
CREATE DATABASE database_name;

-- Drop database
DROP DATABASE [IF EXISTS] database_name;

-- Attach external database file
ATTACH DATABASE 'path/to/file.db' AS alias;

-- Detach database
DETACH DATABASE alias;
```

### 26.2 Maintenance Commands

```sql
-- Reclaim unused space
VACUUM;
VACUUM table_name;

-- Update statistics for query optimizer
ANALYZE;
ANALYZE table_name;

-- Check database integrity
PRAGMA integrity_check;
```

### 26.3 PRAGMA Statements

```sql
-- Get/set database settings
PRAGMA setting_name;
PRAGMA setting_name = value;

-- Common pragmas
PRAGMA page_size;
PRAGMA cache_size = 10000;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA auto_vacuum = INCREMENTAL;
```

---

## 27. Bulk Operations

### 27.1 Bulk Update

```sql
-- Update multiple rows with different values
UPDATE table_name
SET column = CASE id
    WHEN 1 THEN value1
    WHEN 2 THEN value2
    WHEN 3 THEN value3
END
WHERE id IN (1, 2, 3);

-- EF Core ExecuteUpdate style
UPDATE Products
SET Price = Price * 1.1, UpdatedAt = NOW()
WHERE CategoryId = 5;
```

### 27.2 Bulk Delete

```sql
-- EF Core ExecuteDelete style
DELETE FROM Logs
WHERE CreatedAt < DATEADD('month', -6, NOW());
```

---

## 28. Multiple Result Sets

```sql
-- Return multiple result sets
BEGIN
    SELECT * FROM Orders WHERE UserId = @UserId;
    SELECT * FROM OrderItems WHERE OrderId IN 
        (SELECT Id FROM Orders WHERE UserId = @UserId);
END;
```

---

## 29. Reserved Words (Extended)

The following keywords are added to the reserved words list:

```
ANALYZE, ANY, ALL, APPLY, ATTACH,
BINARY, BULK,
CALL, COLLATE, CONFLICT, COVERING, CROSS,
DATABASE, DETACH, DETERMINISTIC,
EXCLUDED, EXECUTE, EXPLAIN,
FILTERED, FORMAT, FUNCTION,
INCLUDE, INCREMENTAL,
JSONB,
LATERAL, LEVEL, LOCKED,
MATCHED, MERGE,
NOWAIT,
OUTER,
PARTIAL, PRAGMA, PROCEDURE,
REPLACE, RETURNS, ROWVERSION,
SCHEMA, SERIALIZABLE, SHARE, SKIP, SNAPSHOT, SOME, STORED,
TRUNCATE,
UNCOMMITTED, USING,
VACUUM, VIRTUAL
```

---

**Document Version History:**
- v1.0 (2024-12-12): Initial specification
- v1.1 (2024-12-19): Added RETURNING clause, date extraction functions
- v1.2 (2024-12-XX): Added ADO.NET/EF Core compatibility features:
  - INFORMATION_SCHEMA views
  - Named constraints
  - Isolation levels and locking hints
  - ROWVERSION for concurrency
  - UPSERT/MERGE operations
  - TRUNCATE, EXISTS, ANY/ALL operators
  - Partial and expression indexes
  - Computed columns
  - JSON support
  - User-defined functions and stored procedures
  - Collation support
  - EXPLAIN query analysis
  - Database administration commands
  - Bulk operations
  - Multiple result sets
