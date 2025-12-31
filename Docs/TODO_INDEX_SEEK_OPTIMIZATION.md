# TODO: Index Point Seek Optimization

## Current Status: ? Needs Improvement

WitDb index point seeks are 30-100x slower than SQLite.

### Benchmark Results (5000 rows)

| Operation | WitDb | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------|-------|--------|--------|-----------|-----------|
| Index Seek unique x100 | 160ms | 5.3ms | NA* | 30x slower | N/A |
| Index Seek non-unique x20 | 43ms | 2.6ms | 8.5ms | 17x slower | 5x slower |
| Composite Index Query | 21.6ms | 1.0ms | 3.7ms | 22x slower | 6x slower |
| Index Range (BETWEEN) | 2.6ms | 0.2ms | 5.2ms | 13x slower | **2x faster** |
| Index Range (>) | 2.9ms | 0.2ms | 1.0ms | 14x slower | 3x slower |

*LiteDB crashed on this benchmark

### Root Cause Analysis

1. **Full B-tree traversal per query**
   - Each index seek starts from root
   - No cursor caching between queries
   - Heavy GC pressure from allocations

2. **Row materialization overhead**
   - Each found rowid requires full row read
   - No lazy loading of row data

3. **No index-only scans**
   - Even when query only needs indexed columns
   - Always fetches full row

**Key files**:
- `Sources/Engine/OutWit.Database.Core/BTree/BTree.cs`
- `Sources/Engine/OutWit.Database/Iterators/IteratorIndexScan.cs`

### Optimization Strategy

## Phase 1: Cursor Caching (High Priority)

**Target**: 3-5x improvement for repeated point queries

### Implementation

1. **B-tree cursor pooling**:
   ```csharp
   // Cache cursors per index for reuse
   private readonly ConcurrentDictionary<string, Stack<BTreeCursor>> _cursorPool;
   
   public BTreeCursor GetCursor(string indexName)
   {
       if (_cursorPool.TryGetValue(indexName, out var stack) && stack.TryPop(out var cursor))
       {
           cursor.Reset();
           return cursor;
       }
       return new BTreeCursor(this);
   }
   
   public void ReturnCursor(string indexName, BTreeCursor cursor)
   {
       _cursorPool.GetOrAdd(indexName, _ => new Stack<BTreeCursor>()).Push(cursor);
   }
   ```

2. **Cursor state preservation**:
   - Keep current node path in cursor
   - For sequential seeks, start from last position if close

**Files to modify**:
- `Sources/Engine/OutWit.Database.Core/BTree/BTreeCursor.cs` (new)
- `Sources/Engine/OutWit.Database.Core/BTree/BTree.cs`

## Phase 2: Seek Optimization (High Priority)

**Target**: 2-3x improvement for point seeks

### Implementation

1. **Binary search optimization in nodes**:
   ```csharp
   // Current: linear search in node keys
   // Optimized: SIMD-accelerated binary search for large nodes
   public int FindKeyIndex(ReadOnlySpan<byte> key)
   {
       if (KeyCount < 16)
           return LinearSearch(key);
       return BinarySearchSimd(key);
   }
   ```

2. **Key comparison caching**:
   - Cache comparison results for common prefixes
   - Use span-based comparisons to avoid allocations

3. **Bloom filter for non-existence**:
   - Quick rejection for keys not in index
   - Avoid B-tree traversal for misses

**Files to modify**:
- `Sources/Engine/OutWit.Database.Core/BTree/BTreeNode.cs`
- `Sources/Engine/OutWit.Database.Core/BTree/BTreeKeyComparer.cs`

## Phase 3: Index-Only Scans (Medium Priority)

**Target**: 2-5x improvement when query uses only indexed columns

### Implementation

1. **Detect index-covering queries**:
   ```sql
   -- This query only needs data from index
   SELECT Id, Name FROM Users WHERE Name = 'John'
   -- If index on (Name, Id), no table access needed
   ```

2. **Query planner enhancement**:
   - Track which columns are in each index
   - Choose covering index when available
   - Skip table row fetch

**Files to modify**:
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Sources.Indexes.cs`
- `Sources/Engine/OutWit.Database/Iterators/IteratorIndexOnlyScan.cs` (new)

## Phase 4: Lazy Row Loading (Low Priority)

**Target**: Reduce memory for queries that don't read all columns

### Implementation

1. **Deferred row materialization**:
   - Return row handle, not full row
   - Load columns on demand

2. **Column projection pushdown**:
   - Only deserialize requested columns

## Expected Results

| Phase | Current | Target | Improvement |
|-------|---------|--------|-------------|
| Cursor Caching | 160ms | 50ms | 3x |
| Seek Optimization | 50ms | 20ms | 2.5x |
| Index-Only Scans | 20ms | 10ms | 2x |
| Combined | 160ms | 10-15ms | 10-15x |

### Success Metrics

After all phases:
- Index Seek unique x100: < 15ms (currently 160ms)
- Index Seek non-unique x20: < 10ms (currently 43ms)
- Composite Index Query: < 5ms (currently 21.6ms)

Target: **Within 3x of SQLite for index seeks**

## Test Plan

1. **Unit tests**:
   - Cursor reuse correctness
   - Seek after insert/delete
   - Concurrent cursor usage

2. **Integration tests**:
   - Index selection by query planner
   - Index-only scan detection
   - Mixed workload (seeks + range scans)

3. **Benchmark validation**:
   - Re-run IndexBenchmarks
   - Memory profiling for allocations

## Progress Tracking

- [ ] Phase 1: Cursor Caching
  - [ ] Implement BTreeCursor
  - [ ] Add cursor pooling
  - [ ] Unit tests
- [ ] Phase 2: Seek Optimization
  - [ ] Binary search in nodes
  - [ ] Span-based key comparison
  - [ ] Bloom filter (optional)
- [ ] Phase 3: Index-Only Scans
  - [ ] Covering index detection
  - [ ] IteratorIndexOnlyScan
- [ ] Phase 4: Lazy Row Loading

## References

- Current B-tree: `Sources/Engine/OutWit.Database.Core/BTree/`
- Index iterator: `IteratorIndexScan.cs`
- Benchmark: `IndexBenchmarks.cs`
