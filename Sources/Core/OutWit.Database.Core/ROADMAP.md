# OutWit.Database.Core - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.Core.

---

## Version 2.0 - Planned Features

### Priority 0: Performance Critical

| Feature | Description | Expected Improvement |
|---------|-------------|---------------------|
| Cursor Caching | Pool and reuse B-tree cursors | 3-5x faster seeks |
| B-tree Seek Optimization | Optimize node traversal | 2-3x faster lookups |
| Lazy Row Loading | Load row data on demand | 1.5-2x for projections |

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| Cursor Support | ICursor interface with forward-only and scrollable cursors |
| Fetch Batching | Batch retrieval for large result sets |
| VACUUM API | Explicit Vacuum() method for B+Tree space reclamation |
| Incremental Vacuum | Background space reclamation |
| Compaction Progress | API to monitor LSM-Tree compaction status |

### Priority 2: Enhancements

| Feature | Description |
|---------|-------------|
| Statistics Histograms | Better cardinality estimation for query optimization |
| SIMD Operations | SIMD-accelerated comparisons and aggregations |
| Memory-Mapped Files | Optional mmap support for large databases |
| Leveled Compaction | Alternative LSM-Tree compaction strategy |
| Page Compression | LZ4/Snappy compression for storage |

---

## Implementation Details

### Cursor Caching (Priority 0)

```csharp
public interface ICursor : IDisposable
{
    bool MoveNext();
    bool MovePrevious();
    bool SeekTo(ReadOnlySpan<byte> key);
    ReadOnlySpan<byte> CurrentKey { get; }
    ReadOnlySpan<byte> CurrentValue { get; }
    void Reset();
}

public class BTreeCursor : ICursor
{
    private uint[] _pathNodes;  // Cached node path
    private int _pathDepth;
    
    public void SeekFrom(ReadOnlySpan<byte> key, bool fromLast = false);
}
```

### VACUUM API (Priority 1)

```csharp
public interface IVacuumable
{
    void Vacuum();
    Task VacuumAsync(CancellationToken cancellationToken = default);
    VacuumProgress GetVacuumProgress();
}

public struct VacuumProgress
{
    public long PagesProcessed { get; }
    public long TotalPages { get; }
    public long BytesReclaimed { get; }
}
```

---

## Files to Modify

| Feature | Files |
|---------|-------|
| Cursor Caching | `Tree/BTree.cs`, `Tree/BTreeNode.cs` |
| VACUUM | `Tree/BTree.cs`, `Stores/StoreBTree.cs` |
| Compaction Progress | `LSM/Compactor.cs`, `Stores/StoreLsm.cs` |

---

## See Also

- [README.md](README.md) - Project documentation
- [EXTENSIBILITY.md](EXTENSIBILITY.md) - Extension guide
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
