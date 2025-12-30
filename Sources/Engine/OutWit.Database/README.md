# OutWit.Database

**WitSQL Engine** - SQL execution engine for WitDatabase embedded database.

This library provides the complete SQL execution layer that transforms parsed SQL statements into query results using the underlying storage engine.

---

## Overview

OutWit.Database is the SQL execution engine built on top of OutWit.Database.Core (storage layer) and OutWit.Database.Parser (SQL parser). It implements the Volcano/Iterator model for query execution and supports a comprehensive set of SQL features.

### Key Features

- **Complete SQL Execution** - DDL, DML, transactions, window functions, CTEs
- **Iterator-Based Query Engine** - Volcano model for efficient query execution
- **Expression Evaluation** - 60+ built-in functions
- **Transaction Support** - Full ACID with savepoints and isolation levels
- **Index Support** - Seek, range scan, partial, expression, and covering indexes
- **Query Optimization** - Cost-based index selection, join ordering, plan caching
- **Window Functions** - ROW_NUMBER, RANK, LAG, LEAD, aggregates OVER
- **JSON Support** - Full JSON manipulation functions
- **.NET 9/10** - Targets latest .NET versions

---

## Installation

```xml
<PackageReference Include="OutWit.Database" Version="1.0.0" />
```

---

## Quick Start

### Create Engine Instance

```csharp
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

// Create in-memory database
var database = new WitDatabaseBuilder()
    .WithMemoryStorage()
    .Build();

using var engine = new WitSqlEngine(database, ownsStore: true);
```

### Execute SQL Queries

```csharp
// Create table
engine.Execute(@"
    CREATE TABLE Users (
        Id GUID PRIMARY KEY,
        Name VARCHAR(100) NOT NULL,
        Email VARCHAR(255) UNIQUE,
        Age INT CHECK (Age >= 0),
        CreatedAt DATETIME DEFAULT NOW()
    )
");

// Insert data
engine.Execute(@"
    INSERT INTO Users (Id, Name, Email, Age)
    VALUES (NEWGUID(), 'John Doe', 'john@example.com', 30)
");

// Query with parameters
var result = engine.Execute(
    "SELECT * FROM Users WHERE Age > @minAge",
    new Dictionary<string, object?> { ["minAge"] = 25 }
);

foreach (var row in result)
{
    Console.WriteLine($"{row["Name"]} - {row["Email"]}");
}
```

### Transactions

```csharp
engine.Execute("BEGIN TRANSACTION");

try
{
    engine.Execute("INSERT INTO Users (Id, Name) VALUES (NEWGUID(), 'Alice')");
    engine.Execute("INSERT INTO Users (Id, Name) VALUES (NEWGUID(), 'Bob')");
    engine.Execute("COMMIT");
}
catch
{
    engine.Execute("ROLLBACK");
    throw;
}
```

### Savepoints

```csharp
engine.Execute("BEGIN TRANSACTION");
engine.Execute("INSERT INTO Users (Id, Name) VALUES (NEWGUID(), 'Alice')");

engine.Execute("SAVEPOINT sp1");
engine.Execute("INSERT INTO Users (Id, Name) VALUES (NEWGUID(), 'Bob')");

// Rollback to savepoint (keeps Alice, removes Bob)
engine.Execute("ROLLBACK TO SAVEPOINT sp1");

engine.Execute("COMMIT");
```

### Query Timeout

```csharp
// Set default timeout
engine.DefaultQueryTimeout = TimeSpan.FromSeconds(30);

// Or per-query timeout
var result = engine.Execute(
    "SELECT * FROM LargeTable",
    parameters: null,
    timeout: TimeSpan.FromSeconds(5)
);
```

---

## Architecture

### Project Structure

```
OutWit.Database/
+-- Engine/                      # Main engine entry point
|   +-- WitSqlEngine.cs          # SQL engine facade
|   +-- WitSqlEngine.Query.cs
|   +-- WitSqlEngine.Ddl.*.cs    # DDL operations
|   +-- WitSqlEngine.Dml.*.cs    # DML operations
|   +-- WitSqlEngine.Transactions.cs
+-- Statements/                  # Statement execution
|   +-- StatementExecutor.cs     # Main executor
|   +-- StatementExecutor.Select.cs
|   +-- StatementExecutor.Insert.cs
|   +-- StatementExecutor.Update.cs
|   +-- StatementExecutor.Delete.cs
|   +-- StatementExecutor.Merge.cs
|   +-- StatementExecutor.Ddl.*.cs
|   +-- StatementExecutor.Transactions.cs
|   +-- StatementExecutor.Explain.cs
+-- Query/                       # Query planning
|   +-- QueryPlanner.cs          # Iterator tree builder
|   +-- QueryPlanner.Sources.cs  # FROM clause handling
|   +-- QueryPlanner.Clauses.cs  # WHERE, ORDER BY, etc.
|   +-- QueryPlanner.Cte.cs      # CTE handling
|   +-- QueryPlanCache.cs        # Query plan caching
+-- Optimizers/                  # Query optimization
|   +-- OptimizerQuery.cs        # Index selection
|   +-- OptimizerJoinOrder.cs    # Join ordering
+-- Expressions/                 # Expression evaluation
|   +-- ExpressionEvaluator.cs   # Main evaluator
|   +-- ExpressionEvaluator.Functions.cs
|   +-- ExpressionEvaluator.Aggregate.cs
|   +-- ExpressionEvaluator.Subquery.cs
|   +-- ExpressionEvaluator.Json.cs
+-- Iterators/                   # Query execution operators
|   +-- IteratorBase.cs
|   +-- IteratorTableScan.cs
|   +-- IteratorIndexSeek.cs
|   +-- IteratorIndexRangeScan.cs
|   +-- IteratorFilter.cs
|   +-- IteratorProject.cs
|   +-- IteratorSort.cs
|   +-- IteratorGroupBy.cs
|   +-- IteratorJoin.cs
|   +-- IteratorWindow.cs
|   +-- IteratorUnion.cs
|   +-- IteratorIntersect.cs
|   +-- IteratorExcept.cs
|   +-- IteratorLimit.cs
|   +-- IteratorDistinct.cs
|   +-- IteratorLocking.cs
+-- Schema/                      # Database schema management
|   +-- SchemaCatalog.cs
|   +-- SchemaCatalog.Tables.cs
|   +-- SchemaCatalog.Indexes.cs
|   +-- SchemaCatalog.Views.cs
|   +-- SchemaCatalog.Triggers.cs
|   +-- SchemaCatalog.Information.*.cs
+-- Definitions/                 # Schema definitions
|   +-- DefinitionTable.cs
|   +-- DefinitionColumn.cs
|   +-- DefinitionIndex.cs
|   +-- DefinitionView.cs
|   +-- DefinitionTrigger.cs
|   +-- DefinitionSequence.cs
+-- Values/                      # SQL value types
|   +-- WitSqlValue.cs           # Variant type
|   +-- WitSqlValue.Operations.cs
|   +-- WitSqlValue.Comparison.cs
|   +-- WitSqlValue.Json.cs
+-- Types/                       # Type system
|   +-- WitSqlType.cs
|   +-- WitDataType.cs
|   +-- WitTypeConverter.cs
+-- Sql/                         # Result types
|   +-- WitSqlResult.cs
|   +-- WitSqlRow.cs
|   +-- WitSqlColumnInfo.cs
+-- Context/                     # Execution context
|   +-- ContextExecution.cs
|   +-- ContextTrigger.cs
+-- Transactions/                # Transaction handling
    +-- TransactionHandle.cs
```

### Iterator Model

The engine uses the Volcano/Iterator model where each query operation is represented as an iterator:

```
IteratorProject (SELECT columns)
    |
IteratorSort (ORDER BY)
    |
IteratorFilter (WHERE)
    |
IteratorJoin (JOIN)
   / \
IteratorTableScan  IteratorIndexSeek
```

Each iterator implements `IResultIterator`:
- `Open()` - Initialize the iterator
- `MoveNext()` - Advance to next row
- `Current` - Get current row
- `Schema` - Get column schema
- `Dispose()` - Clean up resources

---

## Supported SQL Features

### Data Types

| Category | Types |
|----------|-------|
| Integer | `TINYINT`, `SMALLINT`, `INT`, `BIGINT` (signed/unsigned) |
| Floating | `FLOAT16`, `FLOAT`, `DOUBLE`, `DECIMAL` |
| Boolean | `BOOLEAN`, `BOOL` |
| Date/Time | `DATE`, `TIME`, `DATETIME`, `DATETIMEOFFSET`, `INTERVAL` |
| String | `CHAR(n)`, `VARCHAR(n)`, `TEXT` |
| Binary | `BINARY(n)`, `VARBINARY(n)`, `BLOB` |
| Special | `GUID`, `ROWVERSION`, `JSON` |

### DDL Statements

- `CREATE TABLE` with all constraint types (PK, FK, UNIQUE, CHECK, DEFAULT)
- `DROP TABLE [IF EXISTS]`
- `ALTER TABLE` (ADD/DROP COLUMN, ADD/DROP CONSTRAINT, RENAME)
- `CREATE INDEX` (unique, partial, expression, covering)
- `DROP INDEX`
- `CREATE VIEW` / `DROP VIEW`
- `CREATE TRIGGER` / `DROP TRIGGER` (BEFORE/AFTER/INSTEAD OF)
- `CREATE SEQUENCE` / `ALTER SEQUENCE` / `DROP SEQUENCE`
- `TRUNCATE TABLE`

### DML Statements

- `SELECT` with all clauses (WHERE, GROUP BY, HAVING, ORDER BY, LIMIT)
- `INSERT` with VALUES, SELECT, RETURNING, ON CONFLICT
- `UPDATE` with FROM clause, RETURNING
- `DELETE` with USING clause, RETURNING
- `MERGE` (UPSERT with WHEN MATCHED/NOT MATCHED)

### Joins and Set Operations

- `INNER JOIN`, `LEFT JOIN`, `RIGHT JOIN`, `FULL JOIN`, `CROSS JOIN`
- `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`

### Subqueries

- Scalar subqueries
- Table subqueries (FROM clause)
- `IN (subquery)`, `NOT IN (subquery)`
- `EXISTS`, `NOT EXISTS`
- `ANY`, `SOME`, `ALL`
- Correlated subqueries

### Common Table Expressions (CTE)

```sql
WITH ActiveUsers AS (
    SELECT * FROM Users WHERE IsActive = TRUE
)
SELECT * FROM ActiveUsers WHERE Age > 18;

-- Recursive CTE
WITH RECURSIVE Numbers(n) AS (
    SELECT 1
    UNION ALL
    SELECT n + 1 FROM Numbers WHERE n < 10
)
SELECT * FROM Numbers;
```

### Window Functions

```sql
SELECT
    Name,
    Department,
    Salary,
    ROW_NUMBER() OVER (PARTITION BY Department ORDER BY Salary DESC) AS Rank,
    SUM(Salary) OVER (PARTITION BY Department) AS DeptTotal,
    LAG(Salary) OVER (ORDER BY Salary) AS PrevSalary
FROM Employees;
```

Supported functions:
- **Ranking**: `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `PERCENT_RANK`, `CUME_DIST`
- **Value**: `LAG`, `LEAD`, `FIRST_VALUE`, `LAST_VALUE`, `NTH_VALUE`
- **Aggregate**: `SUM`, `AVG`, `COUNT`, `MIN`, `MAX` with `OVER` clause
- **Frame**: `ROWS/RANGE BETWEEN ... AND ...`

### Transactions

```sql
BEGIN TRANSACTION;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

-- Statements with row-level locking
SELECT * FROM Orders WHERE Status = 'pending' FOR UPDATE NOWAIT;

SAVEPOINT sp1;
-- More operations
ROLLBACK TO SAVEPOINT sp1;

COMMIT;
```

Supported isolation levels:
- `READ UNCOMMITTED`
- `READ COMMITTED`
- `REPEATABLE READ`
- `SERIALIZABLE`
- `SNAPSHOT`

### Built-in Functions

| Category | Functions |
|----------|-----------|
| Aggregate | `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, `GROUP_CONCAT` |
| String | `LENGTH`, `UPPER`, `LOWER`, `SUBSTR`, `TRIM`, `REPLACE`, `CONCAT`, `LEFT`, `RIGHT`, etc. |
| Numeric | `ABS`, `ROUND`, `FLOOR`, `CEIL`, `POWER`, `SQRT`, `MOD`, `SIN`, `COS`, `LOG`, etc. |
| Date/Time | `NOW`, `YEAR`, `MONTH`, `DAY`, `DATEADD`, `DATEDIFF`, `STRFTIME`, etc. |
| Conversion | `CAST`, `CONVERT`, `TOSTRING`, `TOINT`, `HEX`, `BASE64`, etc. |
| Null | `COALESCE`, `NULLIF`, `IFNULL`, `NVL` |
| JSON | `JSON_VALUE`, `JSON_QUERY`, `JSON_EXTRACT`, `JSON_SET`, `JSON_REMOVE`, etc. |
| System | `NEWGUID`, `LAST_INSERT_ROWID`, `CHANGES`, `VERSION` |
| Sequence | `INCREMENT`, `LASTINCREMENT` |

### JSON Functions

```sql
-- Extract values
SELECT JSON_VALUE(data, '$.name') FROM Documents;
SELECT JSON_QUERY(data, '$.items') FROM Documents;

-- Modify JSON
UPDATE Documents SET data = JSON_SET(data, '$.status', 'active');
UPDATE Documents SET data = JSON_REMOVE(data, '$.temp');

-- Construct JSON
SELECT JSON_OBJECT('id', Id, 'name', Name) FROM Users;
SELECT JSON_ARRAY(1, 2, 3);
```

### INFORMATION_SCHEMA

```sql
SELECT * FROM INFORMATION_SCHEMA.TABLES;
SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users';
SELECT * FROM INFORMATION_SCHEMA.INDEXES;
SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE;
SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS;
SELECT * FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS;
SELECT * FROM INFORMATION_SCHEMA.VIEWS;
```

### EXPLAIN

```sql
-- Show query plan
EXPLAIN SELECT * FROM Users WHERE Age > 18;

-- Show detailed query plan
EXPLAIN QUERY PLAN SELECT * FROM Orders o JOIN Users u ON o.UserId = u.Id;
```

---

## Query Optimization

### Index Selection

The optimizer automatically selects appropriate indexes based on:
- WHERE clause predicates
- Join conditions
- Cost estimation (row count, index selectivity)

```sql
-- Creates index
CREATE INDEX IX_Users_Age ON Users (Age);

-- Optimizer will use index for this query
SELECT * FROM Users WHERE Age > 18;
```

### Query Plan Caching

Parsed statements and query plans are cached for repeated queries:

```csharp
// First execution: parse + plan + execute
engine.Execute("SELECT * FROM Users WHERE Id = @id", new { id = 1 });

// Second execution: use cached plan
engine.Execute("SELECT * FROM Users WHERE Id = @id", new { id = 2 });

// Access cache statistics
Console.WriteLine($"Cache hits: {engine.PlanCache.Hits}");
Console.WriteLine($"Cache misses: {engine.PlanCache.Misses}");
```

### Join Order Optimization

For multi-table queries, the optimizer reorders joins to minimize intermediate result sizes:

```sql
-- Optimizer may reorder joins based on table statistics
SELECT * FROM Orders o
JOIN Users u ON o.UserId = u.Id
JOIN Products p ON o.ProductId = p.Id
WHERE u.Country = 'USA';
```

---

## Computed Columns

### STORED Computed Columns

```sql
CREATE TABLE Orders (
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    TotalPrice AS (Quantity * UnitPrice) STORED
);

-- TotalPrice is automatically calculated on INSERT/UPDATE
INSERT INTO Orders (Quantity, UnitPrice) VALUES (5, 10.00);
-- TotalPrice = 50.00
```

### VIRTUAL Computed Columns

```sql
CREATE TABLE Products (
    Price DECIMAL(10,2) NOT NULL,
    TaxRate DECIMAL(4,2) NOT NULL,
    PriceWithTax AS (Price * (1 + TaxRate)) -- VIRTUAL by default
);

-- PriceWithTax is calculated on-the-fly during SELECT
SELECT Name, PriceWithTax FROM Products;
```

---

## Triggers

```sql
-- Audit trigger
CREATE TRIGGER AuditUserChanges
AFTER UPDATE ON Users
FOR EACH ROW
BEGIN
    INSERT INTO AuditLog (TableName, OldValue, NewValue, ChangedAt)
    VALUES ('Users', OLD.Name, NEW.Name, NOW());
END;

-- Validation trigger
CREATE TRIGGER ValidateOrderAmount
BEFORE INSERT ON Orders
FOR EACH ROW
WHEN (NEW.Amount < 0)
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Amount cannot be negative';
END;
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| OutWit.Database.Core | 1.0.0 | Storage engine |
| OutWit.Database.Parser | 1.0.0 | SQL parser |
| OutWit.Common | 1.3.1 | Common utilities |
| MemoryPack | 1.21.3 | Binary serialization |

---

## Related Projects

| Project | Description |
|---------|-------------|
| OutWit.Database.Core | Storage engine (B+Tree, LSM, MVCC) |
| OutWit.Database.Parser | SQL parser (ANTLR4-based) |
| OutWit.Database.Core.BouncyCastle | BouncyCastle encryption provider |

---

## Test Coverage

| Category | Tests |
|----------|-------|
| Expression Evaluator | 194 |
| Statement Executor | 162 |
| Iterators | 119 |
| Query Planner | 50 |
| WitSqlValue | 148 |
| Integration Tests | 132 |
| Index Tests | 67 |
| ALTER TABLE Tests | 60 |
| Transaction Tests | 46 |
| CTE Tests | 43 |
| JSON Tests | 42 |
| INFORMATION_SCHEMA Tests | 42 |
| Window Function Tests | 37 |
| Optimization Tests | 51 |
| **Total** | **1395+** |

---

## License

MIT License - see LICENSE file for details.

---

## Performance Considerations

### INSERT Performance with PRIMARY KEY

WitDatabase does **not** automatically create indexes on PRIMARY KEY columns. This is by design to give you full control over your database schema.

#### AUTOINCREMENT Primary Keys (Fast ?)

When using `AUTOINCREMENT`, uniqueness is guaranteed by the internal sequence generator, so no validation scan is needed:

```sql
CREATE TABLE Users (
    Id BIGINT PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100)
);

-- Fast: ~0.02ms per insert (no uniqueness check needed)
INSERT INTO Users (Name) VALUES ('Alice');
```

#### Explicit Primary Keys (Slow Without Index ??)

When you explicitly provide PK values, the engine must validate uniqueness. **Without an index**, this requires a full table scan per insert (O(n) per insert = O(n²) total for batch inserts):

```sql
CREATE TABLE Items (
    Id INT PRIMARY KEY,  -- NOT AUTOINCREMENT
    Name VARCHAR(100)
);

-- Slow: ~0.35ms per insert with full table scan
INSERT INTO Items (Id, Name) VALUES (1, 'Item1');
```

#### Solution: Create Explicit Index (Fast ?)

For explicit PK values, create a UNIQUE index to enable O(log n) validation:

```sql
CREATE TABLE Items (
    Id INT PRIMARY KEY,
    Name VARCHAR(100)
);

-- Add explicit unique index for fast uniqueness checks
CREATE UNIQUE INDEX IX_Items_Id ON Items(Id);

-- Now fast: ~0.07ms per insert (uses index seek)
INSERT INTO Items (Id, Name) VALUES (1, 'Item1');
```

### Performance Summary

| Scenario | Without Index | With Index | Speedup |
|----------|---------------|------------|---------|
| INSERT with AUTOINCREMENT | 0.02 ms/row | N/A | ? Already fast |
| INSERT with explicit PK (500 rows) | ~175 ms | ~35 ms | **5x faster** |
| INSERT with explicit PK (10000 rows) | ~70 sec | ~1.4 sec | **50x faster** |

### Recommendation

For best INSERT performance:

1. **Prefer AUTOINCREMENT** - Let the database generate unique IDs
2. **Create explicit indexes** - If you must use explicit PKs, add a UNIQUE index
3. **Use prepared statements** - Reuse parsed statements via `engine.Prepare()`
4. **Batch in transactions** - Wrap multiple INSERTs in a transaction

```csharp
// Best practice for explicit PK scenario
engine.Execute(@"
    CREATE TABLE Products (
        SKU VARCHAR(50) PRIMARY KEY,
        Name VARCHAR(200),
        Price DECIMAL(10,2)
    )
");

// Create index for fast uniqueness validation
engine.Execute("CREATE UNIQUE INDEX IX_Products_SKU ON Products(SKU)");

// Now bulk inserts are fast
engine.Execute("BEGIN TRANSACTION");
for (int i = 0; i < 10000; i++)
{
    engine.Execute($"INSERT INTO Products (SKU, Name, Price) VALUES ('SKU{i}', 'Product {i}', {i * 1.5})");
}
engine.Execute("COMMIT");
```

---

## See Also

- [WitSql.md](../../WitSql.md) - Full WitSQL language specification
- [Roadmap.Engine.md](../../Roadmap.Engine.md) - Engine roadmap
- [STATUS.md](STATUS.md) - Implementation status
- [PERFORMANCE_ANALYSIS.md](../../Docs/PERFORMANCE_ANALYSIS.md) - Detailed performance analysis
