# TODO: JOIN Operations Optimization

## Current Status: ? Needs Improvement

WitDb JOIN operations are 10-50x slower than SQLite and 5-15x slower than LiteDB.

### Benchmark Results (100 rows per table)

| Operation | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------|-------|--------|--------|-----------|-----------|
| INNER JOIN 2 tables | 0.77ms | 0.07ms | 0.15ms | 10x slower | 5x slower |
| LEFT JOIN | 0.72ms | 0.07ms | 0.15ms | 10x slower | 5x slower |
| INNER JOIN 3 tables | 2.9ms | 0.08ms | 0.22ms | 36x slower | 13x slower |
| INNER JOIN 4 tables | 3.6ms | 0.09ms | 0.26ms | 40x slower | 14x slower |
| JOIN with WHERE | 2.8ms | 0.08ms | 0.19ms | 35x slower | 15x slower |
| JOIN with GROUP BY | 0.75ms | 0.09ms | 0.16ms | 8x slower | 5x slower |

### Root Cause Analysis

Current implementation uses **Nested Loop Join** exclusively:
- For each row in outer table, scans entire inner table
- O(N × M) complexity for two tables
- O(N × M × K) for three tables, etc.
- No index utilization for join conditions

**File**: `Sources/Engine/OutWit.Database/Iterators/IteratorNestedLoopJoin.cs`

### Optimization Strategy

## Phase 1: Hash Join for Equality Conditions (High Priority)

**Target**: 5-10x improvement for equi-joins

### Implementation Plan

1. **Create `IteratorHashJoin` class**
   - Build hash table on smaller relation (build phase)
   - Probe hash table with larger relation (probe phase)
   - O(N + M) complexity instead of O(N × M)

2. **Hash Join Algorithm**:
   ```csharp
   // Build phase - O(M) where M is smaller table
   Dictionary<HashKey, List<WitSqlRow>> hashTable = new();
   foreach (row in smallerTable)
   {
       var key = ComputeJoinKey(row, joinColumns);
       hashTable.GetOrAdd(key).Add(row);
   }
   
   // Probe phase - O(N) where N is larger table
   foreach (row in largerTable)
   {
       var key = ComputeJoinKey(row, joinColumns);
       if (hashTable.TryGetValue(key, out var matches))
       {
           foreach (var match in matches)
               yield CombineRows(row, match);
       }
   }
   ```

3. **Query Planner Changes**:
   - Detect equi-join conditions (e.g., `a.Id = b.ForeignId`)
   - Estimate table sizes to choose build vs probe side
   - Select hash join when both tables > 100 rows

**Files to modify**:
- `Sources/Engine/OutWit.Database/Iterators/IteratorHashJoin.cs` (new)
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Sources.cs`
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Helpers.cs`

## Phase 2: Index Nested Loop Join (Medium Priority)

**Target**: 3-5x improvement when index exists on join column

### Implementation Plan

1. **Detect indexed join conditions**:
   - If inner table has index on join column, use index seek instead of scan

2. **Algorithm**:
   ```csharp
   foreach (row in outerTable)
   {
       var joinValue = row[joinColumn];
       // Use index seek instead of full scan
       var matches = innerTable.IndexSeek(joinColumn, joinValue);
       foreach (var match in matches)
           yield CombineRows(row, match);
   }
   ```

**Files to modify**:
- `Sources/Engine/OutWit.Database/Iterators/IteratorIndexNestedLoopJoin.cs` (new)
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Sources.Indexes.cs`

## Phase 3: Merge Join for Sorted Input (Low Priority)

**Target**: Optimal for pre-sorted data or when ORDER BY matches join

### Implementation Plan

1. **Detect sorted input**:
   - Both inputs sorted on join columns
   - Or one input sorted + other can use index

2. **Algorithm**: Single pass merge O(N + M)

## Expected Results

| Phase | Complexity Before | Complexity After | Expected Speedup |
|-------|-------------------|------------------|------------------|
| Hash Join | O(N × M) | O(N + M) | 5-10x |
| Index NL Join | O(N × M) | O(N × log M) | 3-5x |
| Merge Join | O(N × M) | O(N + M) | 5-10x |

### Success Metrics

After Phase 1 (Hash Join):
- 2-table JOIN: < 0.2ms (currently 0.77ms)
- 3-table JOIN: < 0.5ms (currently 2.9ms)
- 4-table JOIN: < 1.0ms (currently 3.6ms)

Target: **Within 3x of LiteDB performance**

## Test Plan

1. **Unit tests for IteratorHashJoin**:
   - Basic equi-join
   - Multi-column join keys
   - NULL handling in join columns
   - Empty tables
   - Duplicate keys

2. **Integration tests**:
   - Query planner selects correct join type
   - JOIN + WHERE optimization
   - JOIN + GROUP BY optimization

3. **Benchmark validation**:
   - Re-run JoinBenchmarks
   - Verify speedup targets met

## Progress Tracking

- [ ] Phase 1: Hash Join
  - [ ] Create IteratorHashJoin
  - [ ] Add join key hashing
  - [ ] Integrate with QueryPlanner
  - [ ] Unit tests
  - [ ] Benchmark validation
- [ ] Phase 2: Index Nested Loop Join
- [ ] Phase 3: Merge Join

## References

- Current implementation: `IteratorNestedLoopJoin.cs`
- Query planning: `QueryPlanner.Sources.cs`
- Benchmark: `JoinBenchmarks.cs`
