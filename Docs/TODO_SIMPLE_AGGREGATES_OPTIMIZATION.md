# TODO: Simple Aggregates Optimization

## Current Status: ? Needs Improvement

WitDb simple aggregates (without GROUP BY) are 20-170x slower than SQLite.

### Benchmark Results (10000 rows)

| Operation | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------|-------|--------|--------|-----------|-----------|
| COUNT(*) | 10.0ms | 0.06ms | 3.1ms | 167x slower | **3x faster** |
| SUM(Amount) | 10.7ms | 0.45ms | 16.1ms | 24x slower | **1.5x faster** |
| AVG(Amount) | 10.1ms | 0.41ms | 15.6ms | 25x slower | **1.5x faster** |
| MIN/MAX | 11.0ms | 0.66ms | 16.4ms | 17x slower | **1.5x faster** |

### Root Cause Analysis

1. **Row-by-row processing**
   - Each row fully materialized as `WitSqlRow`
   - Column access via dictionary lookup
   - Heavy object allocation

2. **No COUNT(*) optimization**
   - SQLite stores row count in B-tree metadata
   - WitDb scans entire table

3. **No MIN/MAX index optimization**
   - When index exists on column, MIN = first key, MAX = last key
   - WitDb scans entire table regardless

4. **Expression evaluation overhead**
   - Each aggregate argument evaluated per row
   - Function dispatch per row

**Key files**:
- `Sources/Engine/OutWit.Database/Iterators/IteratorStreamingAggregate.cs`
- `Sources/Engine/OutWit.Database/Iterators/IteratorGroupBy.cs`

### Optimization Strategy

## Phase 1: COUNT(*) Metadata (High Priority)

**Target**: 1000x improvement for COUNT(*) without WHERE

### Implementation

1. **Store row count in table metadata**:
   ```csharp
   public class TableMetadata
   {
       public long RowCount { get; set; }
       public long? MinRowId { get; set; }
       public long? MaxRowId { get; set; }
       // Update on INSERT/DELETE
   }
   ```

2. **Query planner shortcut**:
   ```csharp
   // SELECT COUNT(*) FROM Table (no WHERE)
   if (IsSimpleCountStar(select) && select.WhereClause == null)
   {
       return new IteratorConstant(table.Metadata.RowCount);
   }
   ```

**Files to modify**:
- `Sources/Engine/OutWit.Database/Definitions/DefinitionTable.cs`
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.cs`
- `Sources/Engine/OutWit.Database/Iterators/IteratorConstant.cs` (new)

## Phase 2: MIN/MAX Index Optimization (High Priority)

**Target**: 100x improvement when index exists

### Implementation

1. **Detect MIN/MAX on indexed column**:
   ```csharp
   // SELECT MIN(Age) FROM Users
   // If index exists on Age, just read first/last key
   if (IsMinMax(aggregate) && HasIndex(table, columnName))
   {
       return aggregate.FunctionName == "MIN" 
           ? index.GetFirstKey()
           : index.GetLastKey();
   }
   ```

2. **Add B-tree edge access methods**:
   ```csharp
   public class BTree
   {
       public TValue? GetMinKey() => GetEdgeKey(leftMost: true);
       public TValue? GetMaxKey() => GetEdgeKey(leftMost: false);
   }
   ```

**Files to modify**:
- `Sources/Engine/OutWit.Database.Core/BTree/BTree.cs`
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.cs`

## Phase 3: Batch Accumulation (Medium Priority)

**Target**: 3-5x improvement for SUM/AVG

### Implementation

1. **Process rows in batches**:
   ```csharp
   // Instead of: foreach row -> accumulate
   // Do: read batch of values -> SIMD accumulate
   
   const int BatchSize = 1024;
   var values = new double[BatchSize];
   
   while (hasMoreRows)
   {
       int count = FillBatch(values);
       sum += Vector.Sum(values.AsSpan(0, count));
   }
   ```

2. **Column-oriented batch read**:
   - Read only aggregated column, not full row
   - Skip deserialization of other columns

**Files to modify**:
- `Sources/Engine/OutWit.Database/Iterators/IteratorStreamingAggregate.cs`
- `Sources/Engine/OutWit.Database/Iterators/IteratorBatchAggregate.cs` (new)

## Phase 4: SIMD Aggregation (Low Priority)

**Target**: 2-4x improvement on numeric columns

### Implementation

1. **Use Vector<T> for numeric aggregates**:
   ```csharp
   public static double SumSimd(ReadOnlySpan<double> values)
   {
       var sum = Vector<double>.Zero;
       int i = 0;
       
       for (; i <= values.Length - Vector<double>.Count; i += Vector<double>.Count)
       {
           sum += new Vector<double>(values.Slice(i));
       }
       
       double result = Vector.Sum(sum);
       for (; i < values.Length; i++)
           result += values[i];
           
       return result;
   }
   ```

2. **Type-specific accumulators**:
   - Int64 accumulator for integer SUM
   - Double accumulator for real SUM
   - Avoid boxing

**Files to modify**:
- `Sources/Engine/OutWit.Database/Expressions/SimdAggregator.cs` (new)

## Expected Results

| Optimization | Current | Target | Improvement |
|--------------|---------|--------|-------------|
| COUNT(*) metadata | 10.0ms | 0.01ms | 1000x |
| MIN/MAX index | 11.0ms | 0.05ms | 200x |
| Batch accumulation | 10.5ms | 2ms | 5x |
| SIMD (optional) | 2ms | 0.5ms | 4x |

### Success Metrics

After Phase 1-2:
- COUNT(*): < 0.1ms (currently 10ms)
- MIN/MAX with index: < 0.1ms (currently 11ms)

After Phase 3:
- SUM/AVG: < 3ms (currently 10.5ms)

Target: **Within 10x of SQLite for simple aggregates**

## Test Plan

1. **Unit tests**:
   - COUNT(*) returns correct count
   - Row count updated on INSERT/DELETE
   - MIN/MAX with NULL values
   - SIMD accumulator accuracy

2. **Integration tests**:
   - COUNT(*) with WHERE uses scan
   - MIN on non-indexed column uses scan
   - Mixed aggregates (COUNT + SUM)

3. **Benchmark validation**:
   - Re-run AggregateBenchmarks
   - Compare with/without optimizations

## Progress Tracking

- [ ] Phase 1: COUNT(*) Metadata
  - [ ] Add RowCount to TableMetadata
  - [ ] Update on INSERT/DELETE
  - [ ] Query planner shortcut
  - [ ] Unit tests
- [ ] Phase 2: MIN/MAX Index
  - [ ] B-tree GetMinKey/GetMaxKey
  - [ ] Query planner detection
  - [ ] Unit tests
- [ ] Phase 3: Batch Accumulation
  - [ ] Column batch reader
  - [ ] IteratorBatchAggregate
- [ ] Phase 4: SIMD Aggregation

## Alternative: Keep Current for MVP

For MVP, the current performance is acceptable because:
- **WitDb beats LiteDB** in all aggregate operations
- Main use case is OLTP, not analytics
- Complex aggregates (GROUP BY) are already optimized

Consider implementing Phase 1-2 only, as they provide best ROI.

## References

- Current streaming aggregate: `IteratorStreamingAggregate.cs`
- Benchmark: `AggregateBenchmarks.cs`
