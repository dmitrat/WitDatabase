# OutWit.Database.Core - v2 Roadmap

**Version:** 2.0  
**Last Updated:** 2025-02-05

---

## v1 Status: 100% Complete

All v1 features are implemented. See [STATUS.md](STATUS.md) for details.

**Test Coverage:** 1811+ tests passing

---

## v2 Planned Features

### Cursor Support

| Feature | Priority | Description |
|---------|----------|-------------|
| `ICursor` interface | P2 | Forward-only and scrollable cursors |
| Fetch size (batching) | P2 | Batch retrieval for large result sets |

### Advanced Statistics

| Feature | Priority | Description |
|---------|----------|-------------|
| `ANALYZE` command support | P2 | Collect table/index statistics |
| Column cardinality estimation | P2 | For query optimizer |
| Histogram support | P2 | Value distribution statistics |

### VACUUM / Compaction API

| Feature | Priority | Description |
|---------|----------|-------------|
| Explicit `Vacuum()` for BTree | P2 | Reclaim unused space |
| Incremental vacuum | P2 | Background space reclamation |
| Compaction progress API | P2 | Monitor compaction status |

### LSM-Tree Enhancements

| Feature | Priority | Description |
|---------|----------|-------------|
| Leveled compaction | P2 | Alternative compaction strategy |
| Compression support | P2 | Page-level compression |

---

## See Also

- [README.md](README.md) - Documentation
- [STATUS.md](STATUS.md) - Implementation status
- [EXTENSIBILITY.md](EXTENSIBILITY.md) - Extension guide
