# OutWit.Database - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database (SQL Engine).

---

## Version 2.0 - Planned Features

### Priority 0: Performance Critical

| Feature | Description | Expected Improvement |
|---------|-------------|---------------------|
| Index-Only Scans | Return data from index without row fetch | 2-5x for covered queries |
| Query Plan Caching | Improved cache with parameterized plans | 2-5x for repeated queries |
| Prepared Statement Pool | Reuse prepared statements | 2-3x for OLTP workloads |

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| Parallel Query Execution | Multi-threaded table scans |
| SIMD Aggregation | SIMD-accelerated SUM/COUNT/AVG |
| Lazy Result Materialization | Stream results without full materialization |
| Adaptive Query Execution | Runtime plan adjustment based on statistics |

### Priority 2: SQL Features

**User-Defined Functions**

| Feature | Description |
|---------|-------------|
| CREATE FUNCTION execution | Execute UDF definitions |
| RETURNS TABLE support | Table-valued functions |
| DETERMINISTIC handling | Optimization hints for UDFs |
| DROP FUNCTION execution | Remove UDFs |

**Stored Procedures**

| Feature | Description |
|---------|-------------|
| CREATE PROCEDURE execution | Execute procedure definitions |
| DROP PROCEDURE execution | Remove procedures |
| CALL / EXECUTE execution | Invoke procedures |
| Parameter handling | IN/OUT/INOUT parameters |

**Query Analysis**

| Feature | Description |
|---------|-------------|
| EXPLAIN ANALYZE | Actual execution statistics |
| EXPLAIN (FORMAT JSON/TEXT) | Alternative output formats |
| Query profiling | Per-operator timing and row counts |

**Database Administration**

| Feature | Description |
|---------|-------------|
| VACUUM execution | Reclaim unused space |
| ANALYZE execution | Update statistics |
| PRAGMA support | Database configuration |

---

## Implementation Details

### Index-Only Scans (Priority 0)

When all required columns are in the index, skip row fetch:

```csharp
public class IteratorIndexOnlyScan : IteratorBase
{
    // For queries like: SELECT Name FROM Users WHERE Name LIKE 'A%'
    // When index exists on (Name)
    
    private readonly ISecondaryIndex _index;
    private readonly List<string> _projectedColumns;
    
    // Returns data directly from index entries
    // without fetching full row from table
}
```

### Parallel Query Execution (Priority 1)

```csharp
public class IteratorParallelScan : IteratorBase
{
    private readonly int _degreeOfParallelism;
    private readonly ConcurrentQueue<WitSqlRow> _resultQueue;
    
    // Partition table into ranges
    // Scan partitions in parallel
    // Merge results
}
```

### EXPLAIN ANALYZE (Priority 2)

```csharp
public class QueryExecutionStats
{
    public TimeSpan PlanningTime { get; }
    public TimeSpan ExecutionTime { get; }
    public List<OperatorStats> Operators { get; }
}

public class OperatorStats
{
    public string OperatorName { get; }
    public long RowsProduced { get; }
    public long RowsScanned { get; }
    public TimeSpan ExecutionTime { get; }
    public long MemoryUsed { get; }
}
```

---

## Performance Targets

| Metric | Current | Target |
|--------|---------|--------|
| Index-covered queries | N/A | 2-5x faster |
| Repeated parameterized queries | Plan rebuilt | Full reuse |
| Large table scans | Single-threaded | 2-4x with parallel |
| Aggregations | Standard | 2-3x with SIMD |

---

## Files to Modify

| Feature | Files |
|---------|-------|
| Index-Only Scans | `Iterators/IteratorIndexOnlyScan.cs` (new), `Query/QueryPlanner.cs` |
| Parallel Execution | `Iterators/IteratorParallelScan.cs` (new) |
| EXPLAIN ANALYZE | `Statements/StatementExecutor.Explain.cs` |
| UDF Execution | `Statements/StatementExecutor.Ddl.Function.cs` (new) |

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
