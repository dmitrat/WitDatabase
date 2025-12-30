# WitDatabase Performance Analysis & Optimization Roadmap

## Executive Summary

WitDatabase is a pure .NET embedded database that competes with SQLite (native C library with .NET bindings). This document analyzes current performance characteristics and provides optimization recommendations.

## Design Philosophy: No Implicit Indexes

**WitDatabase does NOT create implicit indexes on PRIMARY KEY columns.** This is a deliberate design choice:

- ? **Full control** - You decide what gets indexed
- ? **No hidden performance costs** - No surprise index maintenance overhead
- ? **Predictable behavior** - What you define is what you get
- ? **Simpler persistence** - No implicit state to manage across restarts

For fast INSERT with explicit PK values, **you must create an explicit UNIQUE index**.

---

## Current Performance

### Benchmark Results Summary

| Operation | Table Size | Without Index | With Index | Status |
|-----------|------------|---------------|------------|--------|
| **INSERT (AUTOINCREMENT PK)** | 500 | **~9 ms** | N/A | ?? Fast |
| **INSERT (explicit PK)** | 500 | ~175-200 ms | **~35 ms** | ?? Fast with index |
| **COUNT(*)** | 10,000 | **~13 ms** | N/A | ?? Fast (streaming) |
| **Full Scan** | 10,000 | 7 ms | N/A | ?? Acceptable |

### Profiling Results (500 rows)

| Scenario | Time | Per insert |
|----------|------|------------|
| INSERT without PK | 8 ms | **0.016 ms** |
| INSERT with AUTOINCREMENT | 9 ms | **0.019 ms** ? |
| INSERT with explicit PK + index | 35 ms | **0.07 ms** ? |
| INSERT with explicit PK (no index) | 175-200 ms | 0.35-0.40 ms |
| SQL Parsing only | 25 ms | 0.05 ms |

---

## Optimizations Completed

### 1. Skip UNIQUE validation for auto-generated PK values ?
**Impact**: 20x speedup for INSERT with AUTOINCREMENT

When using `BIGINT PRIMARY KEY AUTOINCREMENT` and not explicitly providing the Id value, UNIQUE validation is skipped because auto-generated values are guaranteed unique.

### 2. Skip conflict check when not needed ?  
**Impact**: Eliminates O(n) scan per insert when no ON CONFLICT clause

### 3. Lazy index loading ?
**Impact**: Avoid GetTableIndexes() call when not needed

### 4. Early NULL filtering ?
**Impact**: Skip UNIQUE check for NULL values early

### 5. Index-based UNIQUE validation ?
**Impact**: ~5x speedup when user creates index on PK

When user explicitly creates a UNIQUE index on PRIMARY KEY columns, UNIQUE validation uses O(log n) index seek instead of O(n) full table scan.

### 6. Streaming Aggregation ? (NEW)
**Impact**: O(1) memory for COUNT/SUM/AVG/MIN/MAX without GROUP BY

For simple aggregate queries without GROUP BY, uses `IteratorStreamingAggregate` that:
- Does NOT materialize all rows in memory
- Computes aggregates in single pass
- Uses constant memory regardless of table size

**Before**: `SELECT COUNT(*) FROM BigTable` stored all rows in `List<WitSqlRow>`  
**After**: Single-pass streaming with O(1) memory

```sql
-- These use streaming aggregation (fast, O(1) memory):
SELECT COUNT(*) FROM LargeTable
SELECT SUM(Amount), AVG(Amount) FROM Orders
SELECT MIN(Price), MAX(Price) FROM Products WHERE Category = 'Electronics'

-- These use full GROUP BY iterator (stores grouped rows):
SELECT Category, COUNT(*) FROM Products GROUP BY Category
SELECT Year, SUM(Sales) FROM Orders GROUP BY Year HAVING SUM(Sales) > 1000
```

---

## How to Optimize INSERT with Explicit PK

### Problem: Explicit PK Requires Uniqueness Validation

When you provide explicit PK values, the database must verify uniqueness before inserting. Without an index, this requires a full table scan:

```
INSERT complexity without index:
- 1st row: scan 0 rows
- 2nd row: scan 1 row
- 3rd row: scan 2 rows
- ...
- Nth row: scan N-1 rows
- Total: O(N²)
```

### Solution: Create UNIQUE Index

```sql
-- Step 1: Create table with explicit PK (no AUTOINCREMENT)
CREATE TABLE Products (
    SKU VARCHAR(50) PRIMARY KEY,
    Name VARCHAR(200),
    Price DECIMAL(10,2)
);

-- Step 2: Create unique index for fast uniqueness checks
CREATE UNIQUE INDEX IX_Products_SKU ON Products(SKU);

-- Now INSERTs use O(log N) index seek instead of O(N) table scan
INSERT INTO Products (SKU, Name, Price) VALUES ('ABC123', 'Widget', 9.99);
```

### Performance Comparison

| Rows | Without Index | With Index | Speedup |
|------|---------------|------------|---------|
| 100 | 7 ms | 3 ms | 2x |
| 500 | 175 ms | 35 ms | 5x |
| 1,000 | 700 ms | 75 ms | 9x |
| 5,000 | 17 sec | 400 ms | 42x |
| 10,000 | 70 sec | 800 ms | 87x |

---

## Remaining Optimization Opportunities

### 1. Statement Caching (Priority: Medium)

**Impact**: 2-5x improvement for repeated queries

Each INSERT parses the SQL (~0.05 ms per parse). For high-throughput scenarios, add LRU cache for parsed statements.

**Workaround**: Use prepared statements via `engine.Prepare()`

### 2. Batch INSERT optimization (Priority: Low)

**Impact**: 2-3x for multi-row INSERT

Optimize `INSERT INTO T VALUES (a), (b), (c)` to share parse cost.

### 3. ~~Streaming Aggregations~~ ? DONE

~~**Impact**: 100x+ for COUNT/SUM on large tables~~

~~Currently, `COUNT(*)` materializes all rows. Implement streaming to count without full materialization.~~

**Implemented**: `IteratorStreamingAggregate` now handles simple aggregates with O(1) memory.

---

## Storage Backend Comparison: B-Tree vs LSM-Tree

| Operation | B-Tree | LSM-Tree | Winner |
|-----------|--------|----------|--------|
| INSERT (AUTO PK) | 15 ms | 8 ms | LSM 1.9x faster |
| INSERT (Explicit PK) | 175 ms | 115-120 ms | LSM 1.5x faster |
| SELECT * | 2.5 ms | 1.6 ms | LSM 1.6x faster |
| COUNT(*) | ~13 ms | ~15 ms | Similar |
| Mixed Workload | 32 ms | 25 ms | LSM 1.3x faster |

**Recommendations**: 
- **LSM-Tree**: Write-heavy workloads, time-series data, logging
- **B-Tree**: Read-heavy workloads, aggregations, general purpose

---

## Best Practices for Optimal Performance

### 1. Primary Key Strategy

```sql
-- BEST: Use AUTOINCREMENT (fastest INSERTs)
CREATE TABLE Events (
    Id BIGINT PRIMARY KEY AUTOINCREMENT,
    EventType VARCHAR(50),
    Data TEXT
);

-- GOOD: Explicit PK with index
CREATE TABLE Products (
    SKU VARCHAR(50) PRIMARY KEY,
    Name VARCHAR(200)
);
CREATE UNIQUE INDEX IX_Products_SKU ON Products(SKU);

-- AVOID: Explicit PK without index (slow for large tables)
CREATE TABLE Items (
    Id INT PRIMARY KEY,  -- No index = full scan per INSERT
    Name VARCHAR(100)
);
```

### 2. Transaction Batching

```sql
-- GOOD: Batch inserts in transaction
BEGIN TRANSACTION;
INSERT INTO Log (Message) VALUES ('Event 1');
INSERT INTO Log (Message) VALUES ('Event 2');
-- ... more inserts
COMMIT;

-- AVOID: Auto-commit per statement
INSERT INTO Log (Message) VALUES ('Event 1');  -- Implicit commit
INSERT INTO Log (Message) VALUES ('Event 2');  -- Implicit commit
```

### 3. Prepared Statements

```csharp
// GOOD: Reuse prepared statement
var stmt = engine.Prepare("INSERT INTO Users (Name, Age) VALUES (@name, @age)");
for (int i = 0; i < 1000; i++)
{
    stmt.Execute(new { name = $"User{i}", age = 20 + i % 50 });
}

// AVOID: Parse SQL every time
for (int i = 0; i < 1000; i++)
{
    engine.Execute($"INSERT INTO Users (Name, Age) VALUES ('User{i}', {20 + i % 50})");
}
```

### 4. Index Strategy

```sql
-- Create indexes AFTER bulk loading
CREATE TABLE BigData (...);
-- Load 1M rows
INSERT INTO BigData SELECT ...;
-- Then create indexes
CREATE INDEX IX_BigData_Date ON BigData(CreatedAt);
CREATE INDEX IX_BigData_Status ON BigData(Status);

-- For ongoing inserts, create indexes upfront
CREATE TABLE Orders (...);
CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_Date ON Orders(OrderDate);
```

### 5. Aggregate Queries

```sql
-- GOOD: Simple aggregates use streaming (O(1) memory)
SELECT COUNT(*) FROM LargeTable;
SELECT SUM(Amount), AVG(Amount), MIN(Amount), MAX(Amount) FROM Orders;

-- Uses full GROUP BY (stores grouped data):
SELECT Category, COUNT(*) FROM Products GROUP BY Category;
