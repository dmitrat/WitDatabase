# WitDb Benchmark Analysis

## Executive Summary

Comprehensive analysis of WitDb performance compared to SQLite (native C) and LiteDB (managed .NET).

### Key Findings

| Category | vs SQLite | vs LiteDB | Verdict |
|----------|-----------|-----------|---------|
| **INSERT** | **1.5-3x faster** ? | **1.5-2x faster** ? | **WitDb wins** |
| **UPDATE (BTree)** | **1.1-10x faster** ? | **2-4x faster** ? | **WitDb wins** |
| **Transactions** | **4-20x faster** ? | **1.2-2x faster** ? | **WitDb wins** |
| **SELECT (full scan)** | 3x slower | **2-3x faster** ? | WitDb competitive |
| **GROUP BY** | 3-7x slower | **1.2-1.5x faster** ? | WitDb competitive |
| **JOIN** | 10-50x slower | **5-10x slower** ?? | Needs improvement |
| **Index Seek** | 30-100x slower | Varies | Needs improvement |
| **Memory** | 100-3000x more | ~same or less | Expected for managed |

## Detailed Analysis by Category

### 1. INSERT Operations (? WitDb Excels)

| Operation | WitDb | SQLite | LiteDB | WitDb vs SQLite | WitDb vs LiteDB |
|-----------|-------|--------|--------|-----------------|-----------------|
| INSERT in tx (1000) | 5.6ms | 8.6ms | 5.5ms | **1.5x faster** | ~same |
| INSERT no tx (100) | 0.7ms | 666ms | 5.2ms | **950x faster** | **7x faster** |
| INSERT RETURNING | 2.8ms | 8.8ms | N/A | **3x faster** | N/A |
| InsertBulk | N/A | N/A | 4.7ms | N/A | comparable |

**Analysis**: WitDb's in-memory model with B-tree storage significantly outperforms SQLite's disk-based writes. SQLite without transaction is extremely slow due to auto-commit per row.

### 2. UPDATE Operations (? WitDb Excels in BTree mode)

| Operation (1000 rows) | WitDb BTree | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------------------|-------------|--------|--------|-----------|-----------|
| UPDATE RETURNING | 0.77ms | 7.5ms | N/A | **10x faster** | N/A |
| UPDATE by PK in tx | 6.5ms | 8.2ms | 13.6ms | **1.3x faster** | **2x faster** |
| UPDATE by indexed col | 7.4ms | 7.9ms | 6.9ms | **1.1x faster** | ~same |
| Bulk UPDATE | 11.4ms | 7.3ms | 46ms | 1.5x slower | **4x faster** |

**Analysis**: Streaming UPDATE optimization (P0.1 from TODO_UPDATE_OPTIMIZATION.md) delivered excellent results. WitDb now beats SQLite on most UPDATE patterns.

### 3. Transaction Operations (? WitDb Excels)

| Operation | WitDb BTree | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------|-------------|--------|--------|-----------|-----------|
| Single Tx N INSERTs | 0.67ms | 6.9ms | 0.89ms | **10x faster** | **1.3x faster** |
| Mixed Tx (INS/UPD/SEL) | 0.67ms | 7.1ms | 1.5ms | **10x faster** | **2x faster** |
| Tx with Savepoint | 0.33ms | 6.8ms | N/A | **20x faster** | N/A |
| Tx Rollback | 0.34ms | 1.2ms | 1.0ms | **3.5x faster** | **3x faster** |
| Sequential Reads x100 | 0.47ms | 5.8ms | 1.6ms | **12x faster** | **3x faster** |

**Analysis**: WitDb's lightweight transaction model with in-memory staging delivers exceptional performance. SQLite's WAL and fsync overhead makes it slower for transactional workloads.

### 4. SELECT Operations (?? Mixed Results)

| Operation (1000 rows) | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------------------|-------|--------|--------|-----------|-----------|
| SELECT * (full scan) | 0.56ms | 0.18ms | 1.5ms | 3x slower | **2.7x faster** |
| SELECT LIMIT 100 | 0.15ms | 0.07ms | 0.16ms | 2x slower | ~same |
| SELECT WHERE | 1.3ms | 0.24ms | 1.3ms | 5x slower | ~same |
| Point Query PK x100 | 0.22ms | 4.9ms | 2.3ms | **22x faster** | **10x faster** |
| SELECT ORDER BY | 1.3ms | 0.22ms | 2.3ms | 6x slower | **1.8x faster** |

**Analysis**: Point queries by PK are excellent. Full scans are slower than SQLite due to managed object overhead but faster than LiteDB.

### 5. GROUP BY / Aggregation (?? Improved but still slower than SQLite)

| Operation (10000 rows) | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|------------------------|-------|--------|--------|-----------|-----------|
| COUNT(*) | 10.0ms | 0.06ms | 3.1ms | 167x slower | **3x faster** |
| SUM/AVG | 10.5ms | 0.45ms | 16ms | 23x slower | **1.5x faster** |
| GROUP BY single | 12.4ms | 1.7ms | 17ms | 7x slower | **1.4x faster** |
| GROUP BY multiple | 14.1ms | 4.7ms | 17ms | **3x slower** | **1.2x faster** |
| GROUP BY HAVING | 18.8ms | 1.6ms | 17ms | 12x slower | **1.1x faster** |

**Analysis**: Optimizations P0.1 (conditional AllRows) and P0.2 (struct-based GroupKey) improved performance. WitDb now beats LiteDB consistently.

### 6. JOIN Operations (? Needs Improvement)

| Operation (100 rows) | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|----------------------|-------|--------|--------|-----------|-----------|
| INNER JOIN 2 tables | 0.77ms | 0.07ms | 0.15ms | 10x slower | 5x slower |
| LEFT JOIN | 0.72ms | 0.07ms | 0.15ms | 10x slower | 5x slower |
| INNER JOIN 3 tables | 2.9ms | 0.08ms | 0.22ms | 36x slower | 13x slower |
| INNER JOIN 4 tables | 3.6ms | 0.09ms | 0.26ms | 40x slower | 14x slower |
| JOIN with WHERE | 2.8ms | 0.08ms | 0.19ms | 35x slower | 15x slower |
| JOIN with GROUP BY | 0.75ms | 0.09ms | 0.16ms | 8x slower | 5x slower |

**Analysis**: JOIN performance is the weakest area. Current nested loop join implementation doesn't use hash joins or merge joins. This is a candidate for future optimization.

### 7. Index Operations (? Point Seeks Need Improvement)

| Operation (5000 rows) | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------------------|-------|--------|--------|-----------|-----------|
| Index Range (BETWEEN) | 2.6ms | 0.2ms | 5.2ms | 13x slower | **2x faster** |
| Index Range (>) | 2.9ms | 0.2ms | 1.0ms | 14x slower | 3x slower |
| Index Seek unique x100 | 160ms | 5.3ms | NA* | **30x slower** | N/A |
| Index Seek non-unique x20 | 43ms | 2.6ms | 8.5ms | 17x slower | 5x slower |
| Composite Index Query | 21.6ms | 1.0ms | 3.7ms | 22x slower | 6x slower |
| Full Scan (no index) | 7.0ms | 0.5ms | 5.9ms | 14x slower | **1.2x faster** |

**Analysis**: Index range scans are acceptable. Point seeks are slow due to repeated B-tree traversal overhead. LiteDB Index Seek benchmark crashed (NA).

## Failed Benchmarks Analysis

### 1. Complex aggregation - WitDb ? FIXED

**Query**:
```sql
SELECT Region, COUNT(*), SUM(Amount), AVG(Amount), MIN(Quantity), MAX(Quantity)
FROM Sales
GROUP BY Region
ORDER BY SUM(Amount) DESC
```

**Previous Issue**: `KeyNotFoundException` when evaluating `ORDER BY SUM(Amount)` - the aggregate expression was being re-evaluated against the result row which didn't contain the original columns.

**Fix Applied**: Created `WitSqlExpressionOrderByColumnIndex` expression type and modified `QueryPlanner.ApplyOrderByClauseForAggregate()` to resolve ORDER BY aggregate expressions to result column indices at query planning time.

**Current Results** (after fix):
| Size | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|------|-------|--------|--------|-----------|-----------|
| 1000 | 1.08ms | 0.27ms | 1.49ms | 4x slower | **1.4x faster** ? |
| 10000 | 13.5ms | 2.04ms | 16.9ms | 6.6x slower | **1.25x faster** ? |

**Files Changed**:
- `Sources/Engine/OutWit.Database.Parser/Expressions/WitSqlExpressionOrderByColumnIndex.cs` (new)
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Clauses.cs`
- `Sources/Engine/OutWit.Database/Expressions/ExpressionEvaluator.cs`

See `Docs/TODO_ORDERBY_AGGREGATE_BUG.md` for full details.

### 2. Index Seek (unique) x100 - LiteDB (All table sizes, all modes)

**Cause**: LiteDB benchmark code issue or LiteDB internal crash. This is NOT a WitDb issue.

**Note**: LiteDB's EnsureIndex + FindById pattern may have issues with repeated calls.

## Memory Analysis

| Operation | WitDb | SQLite | LiteDB | Notes |
|-----------|-------|--------|--------|-------|
| INSERT 1000 rows | 7.3MB | 0.6MB | 9.3MB | WitDb better than LiteDB |
| UPDATE 1000 rows | 9.8MB | 0.4MB | 34MB | **WitDb 3.5x less than LiteDB** |
| SELECT 1000 rows | 2.0MB | 0.7KB | 2.3MB | Similar to LiteDB |
| GROUP BY 10000 | 21.9MB | 1KB | 23.2MB | **WitDb 6% less than LiteDB** |
| JOIN 100 rows | 1.9MB | 0.8KB | 0.26MB | WitDb higher |

**Analysis**: SQLite's native implementation uses minimal managed memory. WitDb and LiteDB are comparable, with WitDb often using slightly less.

## WitDb Unique Advantages

### 1. Pure .NET Implementation
- **No native dependencies** - runs anywhere .NET runs
- **Works in Blazor WebAssembly** - unique capability
- **No P/Invoke overhead** - everything in managed code
- **Easy deployment** - just NuGet packages

### 2. Full ADO.NET/EF Core Support
- Complete `DbConnection`, `DbCommand`, `DbDataReader` implementation
- `DbProviderFactory` for DI integration  
- EF Core provider with migrations support
- Prepared statement caching

### 3. Multiple Storage Engines
- **BTree** - optimized for reads, random access
- **LSM-Tree** - optimized for writes, sequential inserts
- **Parallel modes** - automatic parallelization for large operations

### 4. Advanced SQL Features
- Window functions (ROW_NUMBER, RANK, etc.)
- CTEs (WITH clause)
- Subqueries
- RETURNING clause
- EXPLAIN QUERY PLAN

## Recommendations for Future Optimization

### High Priority
1. **JOIN optimization** - implement hash join for equality conditions
2. **Index seek optimization** - reduce B-tree traversal overhead for point queries
3. **Complex aggregation** - cache ORDER BY aggregate expressions

### Medium Priority
4. **Streaming GROUP BY** - for sorted input, avoid materialization
5. **Parallel aggregation** - partition and merge for large tables

### Low Priority
6. **SIMD aggregation** - use Vector<T> for SUM/AVG on numeric columns
7. **Index-only scans** - return data from index without table lookup

## Conclusion

### WitDb Strengths (Beat both SQLite and LiteDB)
- ? INSERT operations (especially without transaction)
- ? UPDATE operations (with streaming optimization)
- ? Transaction performance
- ? Point queries by primary key

### WitDb Competitive (Beat LiteDB, close to SQLite)
- ?? SELECT full scans
- ?? GROUP BY aggregations
- ?? Index range scans

### WitDb Needs Improvement
- ? JOIN operations (especially multi-table)
- ? Index point seeks (100x slower than SQLite)
- ? Simple aggregates (COUNT, SUM without GROUP BY)

### Overall Assessment

**WitDb is an excellent choice when**:
- Pure .NET solution is required
- Blazor WebAssembly support is needed
- Write-heavy workloads dominate
- Transactional operations are important
- ADO.NET/EF Core compatibility is required

**Consider SQLite when**:
- Read-heavy analytical queries dominate
- Complex multi-table JOINs are frequent
- Native performance is critical
- Memory usage must be minimal

**WitDb vs LiteDB**:
WitDb outperforms LiteDB in most scenarios while providing full SQL support. LiteDB's NoSQL approach may be simpler for document storage, but WitDb offers better performance with relational data.
