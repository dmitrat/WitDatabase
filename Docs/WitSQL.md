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

> **Partly implemented as of 2026-07-29.** Reading `OLD.column` / `NEW.column` works, and so does
> `SIGNAL` — but **assigning to `NEW` does not parse**, so the `SET NEW.UpdatedAt = NOW()` example
> below does not run. That makes the BEFORE-trigger idiom the feature exists for unavailable.
> Planned, not withdrawn. Executable specification: `TriggerBodyCanAssignToNewParsesTest` in
> `Sources/Engine/OutWit.Database.Parser.Tests/AuditVerification/ParserFindingsTests.cs`.


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

**A trigger body may contain only `SELECT`, `INSERT`, `UPDATE`, `DELETE` and `MERGE`** — no DDL, no
transaction control, and no `CALL`. A trigger runs inside a loop over rows, and DDL against the
object that loop is walking is not something the engine can survive: `DROP TABLE` fired from a
trigger on the table being written reports success and destroys it. `CALL` is refused for the same
reason one step removed — a procedure body *is* allowed DDL, precisely because a `CALL` at the top
level is a statement rather than a row loop. See § 2.11.

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

### 2.10 User-Defined Functions

```sql
CREATE FUNCTION [IF NOT EXISTS] function_name ( [param type [, param type]...] )
    RETURNS type
    [LANGUAGE SQL]
    AS BEGIN RETURN expression; END;

DROP FUNCTION [IF EXISTS] function_name;
```

**A function body is one expression, not a list of statements.** It is evaluated over its arguments
and nothing else — there is no row behind it and no statement runs. That is what lets a function be
called from anywhere an expression may appear, including places evaluated per row.

```sql
CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END;

SELECT Doubled(Price) FROM Orders;
SELECT * FROM Orders WHERE Doubled(Price) > 100;
SELECT * FROM Orders ORDER BY Doubled(Price);

CREATE TABLE Items (
    Id       INT PRIMARY KEY,
    Price    INT CHECK (Doubled(Price) < 1000),
    Doubled2 AS (Doubled(Price)),          -- a computed column takes no type of its own
    Tax      INT DEFAULT (Doubled(5))
);

CREATE INDEX IX_Doubled ON Orders ((Doubled(Price)));
```

**Rules, all enforced when the function is declared:**

- Every name in the body must be one of the parameters. A body has no row to read a column from.
- Every function it calls must exist — a built-in, or another user-defined function.
- **A function may not call itself.** An expression body has nothing to stop the recursion with, and
  an exhausted stack cannot be caught.
- **`LANGUAGE SQL` only.** No external code, no assembly loading. Any other language is refused.
- The declared `RETURNS` type is **applied** to the result, using the same conversion a column write
  uses. `NULL` stays `NULL`.

**Determinism.** A function is deterministic when its body reads no table and calls nothing whose
answer moves — `NOW()`, `RANDOM()`, `NEWGUID()`, a sequence. It is decided from the body when the
function is declared, reported by `INFORMATION_SCHEMA.ROUTINES.IS_DETERMINISTIC`, and it composes: a
function calling a non-deterministic one is non-deterministic. **Only a deterministic function may
key an index**, because an index key is computed once when the row is written and never recomputed.

**Dropping.** `DROP FUNCTION` is refused while anything still names it — a `CHECK`, a computed
column, a `DEFAULT`, an index expression, a view, a procedure body, or another function. There is no
`CASCADE`: a schema expression left naming a function that does not exist makes the object it belongs
to unusable.

### 2.11 Stored Procedures

```sql
CREATE PROCEDURE [IF NOT EXISTS] procedure_name [ ( [param type [, param type]...] ) ]
    [LANGUAGE SQL]
    AS BEGIN
        sql_statements
    END;

DROP PROCEDURE [IF EXISTS] procedure_name;

CALL procedure_name ( [argument [, argument]...] );
```

A procedure body is a list of statements. The parameter list may be omitted entirely.

```sql
CREATE PROCEDURE ArchiveOrder(OrderId INT) AS BEGIN
    INSERT INTO OrdersArchive SELECT * FROM Orders WHERE Id = OrderId;
    DELETE FROM Orders WHERE Id = OrderId;
END;

CALL ArchiveOrder(42);
```

**The last statement's result is the call's result**, so a body ending in a `SELECT` returns rows:

```sql
CREATE PROCEDURE RecentOrders AS BEGIN SELECT * FROM Orders ORDER BY Created DESC; END;

CALL RecentOrders();
```

One result set only — a body with two `SELECT`s hands back the second.

**A body may contain** `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, DDL, and `CALL` of another
procedure.

**A body may not contain transaction control** — `BEGIN TRANSACTION`, `COMMIT`, `ROLLBACK`,
`SAVEPOINT`. Committing inside a routine would commit the statement that called it, leaving the rest
of that statement running outside any transaction, and nothing would report it. Refused when the
procedure is declared.

**A body may not declare or drop a routine**, and **a trigger body may not `CALL` a procedure.** The
second is what lets a procedure contain DDL at all: a `CALL` at the top level is a statement, while a
trigger runs inside a loop over rows, and DDL against the object that loop is walking is not
something the engine can survive.

**Atomicity.** A `CALL` is one statement to its caller, so it is one unit of work: a body that fails
part-way leaves nothing behind, including the DDL it had already run.

**Recursion is allowed and bounded.** A procedure may call itself; statements may nest 32 deep, after
which the call is refused with an error the caller can catch. (A *function* may not recurse — see
2.10 — because a function is evaluated inside an expression and never passes through that counter.)

**From ADO.NET**, a procedure is invoked the ordinary way:

```csharp
using var command = connection.CreateCommand();
command.CommandType = CommandType.StoredProcedure;
command.CommandText = "ArchiveOrder";

var parameter = command.CreateParameter();
parameter.ParameterName = "OrderId";
parameter.Value = 42;
command.Parameters.Add(parameter);

command.ExecuteNonQuery();
```

Arguments are bound, never interpolated, and they are passed in the order they were added to the
collection.

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

-- Triggers
SELECT * FROM INFORMATION_SCHEMA.TRIGGERS;

-- Sequences
SELECT * FROM INFORMATION_SCHEMA.SEQUENCES;

-- Functions and procedures
SELECT * FROM INFORMATION_SCHEMA.ROUTINES;
-- Columns: SPECIFIC_CATALOG, SPECIFIC_SCHEMA, SPECIFIC_NAME,
--          ROUTINE_CATALOG, ROUTINE_SCHEMA, ROUTINE_NAME, ROUTINE_TYPE,
--          DATA_TYPE, ROUTINE_BODY, ROUTINE_DEFINITION,
--          IS_DETERMINISTIC, SQL_DATA_ACCESS, PARAMETER_STYLE, IS_USER_DEFINED_CAST

-- Their parameters
SELECT * FROM INFORMATION_SCHEMA.PARAMETERS;
-- Columns: SPECIFIC_CATALOG, SPECIFIC_SCHEMA, SPECIFIC_NAME,
--          ORDINAL_POSITION, PARAMETER_MODE, IS_RESULT, PARAMETER_NAME,
--          DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
```

`ROUTINE_TYPE` is `FUNCTION` or `PROCEDURE`; `DATA_TYPE` is the return type and is `NULL` for a
procedure. `ROUTINE_DEFINITION` is rendered from the stored body on demand, and is `NULL` when the
body cannot be written back faithfully — never a placeholder. `PARAMETER_MODE` is always `IN`: there
are no `OUT` parameters.

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

## 14.9 Choosing a storage engine

`Store=btree` is the default and is the right choice for almost everything. `Store=lsm` is a real
alternative for one shape of workload, and the boundary is narrow enough that it has to be stated
precisely rather than as "LSM is write-optimised".

Measured on a Ryzen 9 5950X, .NET 10, 500,000 rows written in batches of 1,000, three rounds each,
microseconds per row:

| | B+Tree | LSM | |
|---|---|---|---|
| Sustained ingest, **no secondary indexes** | 15.10 | 15.36 | parity |
| Sustained ingest, **3 secondary indexes** | 23.16 | **36.86** | **LSM 1.6x slower** |

Driven at the storage layer, without the SQL engine in the way, LSM is **10-13% faster** at 500,000
and 1,000,000 rows - so the structure does deliver its advantage, and secondary indexes are what
take it away. Each index gets its own LSM store today, with its own write-ahead log and its own
compaction, so the cost of maintaining one is 7.2 µs per row against the B+Tree's 2.7.

**Choose `Store=lsm` when all of these hold:**

- writes dominate reads, and they arrive as a sustained stream rather than in occasional small
  transactions;
- the table is large - the advantage appears above roughly half a million rows, because below that
  the in-memory table never fills and the structure never does the sequential work it exists for;
- the table carries **few or no secondary indexes**.

**Keep the default otherwise** - for read-heavy work, for small transactions, for autocommit (where
LSM is still several times behind), and for any table with several indexes.

Both engines are durable and both honour `SyncWrites`, `MemTableSize` and the rest of the
connection-string settings; the choice is about the shape of the workload, not about safety.

## 14.10 Configuration: which combinations are supported

WitDatabase is a construction kit: a workload chooses a store, a transaction model, a parallel mode,
encryption, a journal and a cache. Every combination below has been **built and run** - the matrix is
`CombinationMatrixTests`, 153 cases, and every one of them opens a database, runs the same workload and
compares the answers. Every combination is run a second time with **two connections open over it at
once** - the shape an ASP.NET Core host produces - and all of them work. What is written here is
measured, not intended.

**The rule this table exists to make true:** a setting is either honoured or refused at `Open` with an
explanation. Nothing is accepted and ignored.

### Stores

| `Store` | Persistent | Notes |
|---|---|---|
| `btree` (default) | yes | Page-oriented. Honours `PageSize`, `CacheSize`, `Cache`. |
| `lsm` | yes | Directory-based. Honours the LSM settings below. See 14.9 for when to choose it. |
| `inmemory` | **no** | Keeps nothing after the last connection closes, whatever `Data Source` says. |

### Transaction model

| Setting | Effect |
|---|---|
| `Transactions=true;MVCC=true` (default) | Multi-version store. Concurrent transactions across connections. |
| `Transactions=true;MVCC=false` | Lock-based transactional store. A journal may be configured. Concurrent transactions across connections are **not** supported in this mode - a second session's `BEGIN` fails. |
| `Transactions=false` | No transaction layer. `BeginTransaction` throws; statements are autocommitted. |

### Journal

`Journal=wal` and `Journal=rollback` require `MVCC=false`. **With MVCC, or with transactions off, the
connection is refused at `Open`** - the MVCC store keeps its own versions and takes no journal, so
accepting the setting would mean ignoring it.

### Cache

`Cache=clock` (default) and `Cache=lru` select the page cache for the B+Tree store and for its secondary
index stores. `CacheSize` is the number of pages. Neither applies to `Store=lsm`, which has no page
cache; use `EnableBlockCache` and `BlockCacheSize` there.

### Concurrency: there is nothing to configure

**`Parallel Mode` and `Max Writers` were removed in 12.0.0, and a connection string that still carries
one is refused at `Open`** with a message naming the reason. Thread safety is a property of each store
rather than a choice a caller makes:

| store | what it does about concurrent access |
|---|---|
| `btree` | Serialised whenever it is built - main store and every secondary index store. It has no locking of its own. |
| `lsm` | Locks internally. |
| `inmemory` | Locks internally. |

The reasoning is measured, not asserted. With no wrapper and no transaction layer, two writers meeting
inside one leaf split of the B+Tree store threw and lost a row in **five runs out of five**; serialising
costs a single thread nothing (median **1.001x** over five interleaved passes of 20,000 operations). The
one thing the setting still selected - the LSM store's write buffer - was measured through a database
and is **slower** there (**1.04x** with batched transactions, **1.14x** with autocommit), because a
transaction layer serialises writers before they can contend for it. Its 0.80x win needs four threads
inside the store at once, which no configuration with transactions produces.

A caller driving a store directly, without the engine, can still wrap it: `LsmParallelStore` is public.

### LSM settings

`MemTableSize`, `SyncWrites`, `EnableWal`, `BlockSize`, `CompactionTrigger`, `EnableBlockCache`,
`BlockCacheSize` and `BackgroundCompaction` reach both the main store and every secondary index store.
They are ignored by the other stores.

### Opening a database that already exists: what the file remembers

**Settings are named when a database is created. Opening it needs only `Data Source=`** - the file
records the configuration it was made with, and supplies whatever the connection string does not say.
Measured, not intended: `ConfigurationRestoreTests` creates a database with each setting, reopens it
with `Data Source=` and nothing else, and compares the engine it gets with the one the full connection
string builds.

| setting | restored from the file? |
|---|---|
| `Store`, `PageSize`, `Transactions`, `MVCC`, `Journal` | yes |
| `Cache`, `CacheSize` | yes |
| `MemTableSize`, `BlockSize`, `CompactionTrigger`, `EnableWal`, `SyncWrites`, `EnableBlockCache`, `BlockCacheSize` | yes |
| `Encryption` | the flag is recorded; the **password** is yours to supply |
| `Synchronous Commit`, `FileLocking` | **no** - see below |
| `Isolation Level` | **no** - a property of the session, not of the data |

**A connection string always wins.** Anything it names is used; the file fills in the rest. So a
database created with `Cache=lru` and opened with `Data Source=db;Cache=clock` gets a clock cache.

**`Synchronous Commit` and `FileLocking` are deliberately not restored**, and the reason is not
symmetry. Both trade a guarantee away, and restoring them would let a *file* make a database quietly
less durable, or less exclusive, than the defaults promise - for a caller who said nothing about
either. Name them again in the connection string when you want them.

**Encryption:** the header records that a database is encrypted and with which provider, so opening one
without a password is refused rather than misread. Nothing else can be restored for an encrypted
database except its transaction model: the header is inside the encrypted page, so a non-default
`PageSize`, `Cache` or `CacheSize` has to be named again.

**A transaction model you name and the database does not have is still refused at `Open`.** The MVCC
store keeps every value under a versioned key and no other configuration does, so a database written
with `MVCC=true` - the default - and opened with `MVCC=false` used to open without a word of complaint
and then report every table as missing. The rows were never lost; they were invisible, and the natural
next step - creating the schema on what looks like an empty database - wrote over one that was intact.
Both directions are refused, with a message naming the setting. Not naming a model is not a
disagreement: it is restored.

`MVCC=false` and `Transactions=false` write the same layout as each other, so either can open the
other's database.

**LSM databases record this in a `provider.meta` file** beside their SSTables, because a directory has
no page to put a header in. Before 12.2.0 they recorded nothing, and `WitDatabase.Open` on one built a
store with no transaction layer and reported every table as missing.

**Databases created before 12.2.0** record only the store, the encryption provider and the feature
flags; everything else falls back to the defaults, exactly as it did. They open unchanged, and a build
older than 12.2.0 reads a database created by this one.

### Durability by configuration

Measured, one process kill per configuration, 20 rows committed and the process killed with no close
and no dispose (`DurabilityByConfigurationTests`):

| configuration | a committed transaction after a process kill |
|---|---|
| default (`MVCC=true`, `Synchronous Commit=true`) | **survives** |
| `MVCC=false`, with or without `Journal=wal` / `Journal=rollback` | **survives** |
| `Store=lsm`, with either transaction model | **survives** |
| encrypted (`Encryption=aes-gcm`) | **survives** |
| `Synchronous Commit=false` | **lost** - documented, this is what the setting trades |
| `Transactions=false` | **lost** - there is no commit to make durable |

The last two lose everything written since the database was opened, including the table: the reopened
database reports it as not found. `Transactions=false` is the one worth reading twice - autocommit is
durable *because* each statement runs in an implicit transaction, and with the transaction layer
switched off there is none.

### Selecting a provider from another package

`Encryption=chacha20-poly1305` comes from `OutWit.Database.Core.BouncyCastle`, and **referencing the
package is not enough to make the keyword work**. The package registers its provider from a module
initializer, which the runtime executes when the assembly is loaded - and an assembly nothing has
touched is never loaded. Call it once at startup:

```csharp
OutWit.Database.Core.BouncyCastle.BouncyCastleProviderRegistration.EnsureRegistered();
```

The fluent route documented in that package's README - `WithBouncyCastleEncryption(...)` - needs nothing,
because calling an extension method on a type in the assembly loads it. A connection string does not.
Without either, `Open` refuses with `Encryption provider 'chacha20-poly1305' is not registered`. The same
applies to any provider a third party registers from its own module initializer.

### Storage that has no synchronous operations (Blazor WASM, IndexedDb)

A database can be **built** and **closed** over a storage that offers only asynchronous operations -
`BuildAsync`, then `await engine.DisposeAsync()` or `await database.DisposeAsync()`, under either
transaction model. It cannot yet be **written to**: the implicit transaction behind every statement
commits, the commit flushes, and the flush writes the database header through the synchronous
`IStorage.WritePage`.

Measured with a storage whose synchronous members throw: the build is asynchronous throughout,
`CREATE TABLE` succeeds (it writes nothing), and the first `INSERT` throws. The gap is not the close - it
is that there is no asynchronous statement path: `WitSqlEngine` offers `Execute` and `Query` only, and
`DbCommand.ExecuteNonQueryAsync` runs the synchronous path on a thread-pool thread, which a browser does
not have.

`OutWit.Database.Core.IndexedDb` is the package this affects. Treat it as unfinished rather than
supported until an asynchronous statement path exists.

### Settings that are read but not enforced

`Isolation Level` is recorded and reported, and the engine does not vary its answers by it. Measured,
both on a scan and on a single-key lookup: a transaction opened at `Serializable`, `RepeatableRead` or
`Snapshot` **sees a row another connection commits after it began**, which each of those three levels
exists to prevent. `ReadCommitted` behaves correctly, and is the control - it must see the row, and does.

Treat every level as `ReadCommitted` until this changes. It is listed rather than removed because
ADO.NET consumers set it as a matter of course, and refusing it would break them for a setting that has
never done anything.

### A note on spelling

Every keyword above works whether or not `Store` is also named. Before 12.0.0 it did not: the whole
pass-through parameter set was discarded unless `Store=` appeared in the connection string, so
`Data Source=db;PageSize=16384` silently used the default page size while
`Data Source=db;Store=btree;PageSize=16384` - the same engine - honoured it.

A value that cannot be read as the setting's type is now an error at `Open` rather than a silent
default: `PageSize=large` is refused.

## 15. Concurrency Control

### 15.0 The concurrency model

> **One process. One engine per database. Many connections. One writer at a time.**

WitDatabase is an embedded, file-backed database, and this is the model a consumer may rely on. It was
never written down before 5.0.0, and before 5.0.0 it was not enforced consistently either.

| | Supported |
|---|---|
| Several `WitDbConnection`s in one process, to one database | **Yes** — they share one engine |
| A connection seeing another's committed work | **Yes** — rows *and* `COUNT(*)`, including tables created after it opened |
| Concurrent readers | **Yes** |
| Concurrent writers | Serialised: one writer at a time, transparently |
| Concurrent **transactions** in different connections | **Yes with MVCC** (the default). With `MVCC=false` a transaction holds a database-wide write lock |
| A second **process** opening the same database | **No** — `DatabaseAlreadyOpenException` |
| A second engine in the same process (e.g. two `WitDatabase` instances) | **No** — same exception |
| Two `Data Source=:memory:` connections sharing data | **No** — each is its own database, as in SQLite without `Cache=Shared` |
| One database opened with **different options** in one process | **No** — `InvalidOperationException` naming the mismatch |

**How connections share.** The first connection to a database builds the engine; the rest attach to it,
and it is disposed — releasing the exclusive lock — when the last one closes. What is shared is the
storage *and the schema catalog*; each connection keeps its own session, so transactions are independent.
That division matters: with a per-connection catalog, a table created by one connection was
`Table not found` to another, and a row inserted by one was visible to the other's scan while that
other's `COUNT(*)` still said zero.

Connections are therefore cheap handles, and pooling them buys little — the expensive thing is the
engine, and it is already shared.

**Why single-process.** Two engines over one database each keep their own page cache, memtable and
write-ahead log, with nothing coordinating them. Measured before the limit was enforced: two engines over
one LSM directory diverged, one seeing a row the other could not.

**Why exclusivity is the goal and not a limitation we tolerate.** A file database that lets two processes
in has to keep their caches coherent, and this engine's caches — page cache, memtable, write-ahead log —
are per engine. The alternative was measured rather than imagined: two engines over one LSM directory
opened, both answered confidently, and **silently disagreed** about which rows existed. Refusing at open
is the cheaper correct answer, and it is the same choice LiteDB makes by default; SQLite starts from
multi-process but offers the same exclusivity as `PRAGMA locking_mode=EXCLUSIVE`, and stops guaranteeing
anything on network filesystems, where locking primitives are unreliable.

**If several places need the same database, put a service in front of it.** That is the supported answer
rather than a workaround: one process owns the file, and callers reach it through an API. The engine
already does the hard half — many connections and many sessions inside one process, each with its own
transaction, sharing one engine — so a wrapper is a transport, not a concurrency design.

**How it is enforced.** An exclusive lock is taken on a `<database>.lock` sidecar for as long as the
engine is open, and a second engine is refused with `DatabaseAlreadyOpenException`. The operating system
releases the lock when the owning process exits, so a process that dies without shutting down cleanly
does **not** leave the database permanently locked. The sidecar file itself remains on disk after a
clean shutdown; its presence does not mean anyone holds it, and `EnsureDeleted` removes it along with the
rest of the database.

**Opening waits briefly rather than refusing at once.** A host restart overlaps the outgoing process with
the incoming one, and a guard that refuses on the first attempt turns that window into a startup failure.
So `Build` retries with backoff for **five seconds** by default before raising
`DatabaseAlreadyOpenException` — long enough for an ordinary shutdown to finish flushing, short enough
that a database somebody really is using is reported quickly. `WithOpenTimeout(TimeSpan.Zero)` restores
the single attempt. SQLite covers the same window with `busy_timeout`.

`FileLocking=false` in a connection string disables the guard. It exists for filesystems where advisory
locking is unreliable — network shares in particular — and it does **not** disable the in-process
serialisation between writers, which is not optional.

**What turning it off actually costs, measured rather than described.** With the guard off, nothing else
enforces one engine per database *portably*, and the two platforms then behave differently:

- **Windows** still refuses a second engine, but only as a side effect: the write-ahead log opens with a
  share mode that excludes a second writer. That is not a guarantee the engine makes.
- **Linux admits it.** .NET emulates `FileShare` with advisory `flock`, where every mode except
  `FileShare.None` becomes a *shared* lock — so a second engine opens the same LSM database, and the two
  then **silently disagree**: the engine that opened second replays the write-ahead log and sees the
  first engine's rows, while the first engine cannot see the second's rows at all, because they live in
  another engine's memtable and nothing invalidates or notifies it. Both writes survive; what is lost is
  agreement, and nothing warns.

So `FileLocking=false` is safe for the case it exists for — **one** engine on a filesystem whose locking
cannot be trusted — and is not a way to run two. On Linux it silently permits exactly the configuration
the guard exists to refuse.

### 15.0.3 Ambient transactions

A connection opened inside a `TransactionScope` **enlists in it**, and the scope decides the outcome: no
`Complete()` means the work is rolled back. Enlistment happens at `Open` and only there — a connection
opened before the scope began is not part of it, exactly as in SqlClient, and can be joined by hand with
`EnlistTransaction`. `Enlist=false` in the connection string turns the automatic half off.

The database enlists as the **single resource manager** of the transaction, which is what lets it skip
two-phase commit. It has no durable prepare record, so it cannot act as one participant among several:
**a second database in the same scope is refused** with a message saying so, rather than joining and
committing on its own. Use one database per scope, or coordinate the two yourself.

A local `BeginTransaction` and an ambient transaction cannot both be in progress on one connection;
whichever comes second is refused.

### 15.0.1 Read-only connections

`Read Only=true` — or equivalently `Mode=ReadOnly` — makes a connection refuse anything that could change
data or schema: `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, every `CREATE`/`ALTER`/`DROP`, and the bulk API.
`SELECT`, `EXPLAIN` and transaction control are allowed, so wrapping reads in a transaction works as it
does anywhere else; a transaction that then attempts a write is refused on the write.

**It is a property of the connection, not of the file.** A read-only connection and a writing connection
can address the same database at the same time, share one engine, and see each other's committed work —
which is the shape it exists for. It is therefore *not* a way to open a database on read-only media; that
is a separate capability and is not built.

The restriction is fixed when the connection opens and cannot be lifted afterwards, and it is
**fail-closed**: a read-only session permits a named list of statement kinds and refuses everything else,
so a statement kind added to WitSQL later is refused until it is judged safe.

Both settings were **parsed and ignored before 5.0.0** — a write through a read-only connection
succeeded.

**Before 5.0.0** two things were different, and they pull in opposite directions.

*Exclusivity was a side effect* of how each store happened to open its files: a B+Tree database refused a
second engine on every platform, an LSM database refused one only on Windows and only with the
write-ahead log enabled, and an LSM database with the log disabled refused none anywhere. It is now
enforced deliberately and identically everywhere.

*A second connection did not work at all.* Every connection built its own engine, so opening two to one
database failed — which is to say the shape above, several scoped `DbContext`s in one host, was not
available. That is the change 5.0.0 exists for.

So: **a second connection now succeeds where it used to fail**, and **a second process now fails
predictably where it used to depend on the platform and the store**. Code that relied on opening one LSM
database from two processes on Linux will get `DatabaseAlreadyOpenException`; that configuration was
unsafe — the two engines diverged.

### 15.0.2 Row locks and deadlocks

`SELECT … FOR UPDATE` and `SELECT … FOR SHARE` take row locks inside an MVCC transaction. Three things a
consumer may rely on:

**A row lock is held to the end of the transaction**, and released on `COMMIT` or `ROLLBACK` — two-phase
locking, as in PostgreSQL and SQL Server. There is no statement that releases one lock early.

**A deadlock is reported, not waited out.** When two transactions each wait for a row the other holds, the
one whose wait *closes the cycle* is refused immediately with `DeadlockException`, which names the other
participants. Before 6.0.0 both sides waited out the full lock timeout and each got a
`TimeoutException`, with nothing to say a cycle was the cause.

**The transaction that is told about the deadlock is the one that must roll back.** It is named as the
victim, and rolling it back releases its locks and lets the others proceed. `DeadlockVictimStrategy` on
the detector does *not* choose the victim here: the other participants are blocked waiting for a lock, and
there is no way to abort a transaction from another thread. The strategy still applies to the detector's
on-demand and background APIs, where nobody is blocked.

`NOWAIT` and `SKIP LOCKED` cannot deadlock — they give up rather than wait — and so are never reported as
one: `NOWAIT` raises `RowLockException`, `SKIP LOCKED` skips the row.

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

> **Status: not implemented as of 2026-07-29.** This section describes intended behaviour, not
> shipped behaviour — `CREATE FUNCTION` does not parse. It is **planned**, not withdrawn: PostgreSQL and
> SQL Server both provide this, and WitDatabase aims to substitute for those without the calling
> application noticing. What it needs is a subsystem rather than a grammar rule — a function catalog, integration with the expression evaluator, and persistence — so it is
> tracked outside the grammar work. Executable specification: `CreateFunctionIsSupportedTest` in
> `Sources/Engine/OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.cs`.


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

> **Status: not implemented as of 2026-07-29.** This section describes intended behaviour, not
> shipped behaviour — `CREATE PROCEDURE` does not parse. It is **planned**, not withdrawn: PostgreSQL and
> SQL Server both provide this, and WitDatabase aims to substitute for those without the calling
> application noticing. What it needs is a subsystem rather than a grammar rule — a procedural interpreter with variables, control flow and CALL — so it is
> tracked outside the grammar work. Executable specification: `CreateProcedureIsSupportedTest` in
> `Sources/Engine/OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.cs`.


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
