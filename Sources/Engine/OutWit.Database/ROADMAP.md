# OutWit.Database (Engine) - v2 Roadmap

**Version:** 3.0  
**Last Updated:** 2025-02-05

---

## v1 Status: 100% Complete

All SQL execution features are implemented.

See [STATUS.md](STATUS.md) for details.

**Test Coverage:** 1395+ tests passing

---

## v2 Planned Features

### User-Defined Functions

| Feature | Priority | Description |
|---------|----------|-------------|
| `CREATE FUNCTION` execution | P2 | Execute UDF definitions |
| `RETURNS TABLE` support | P2 | Table-valued functions |
| `DETERMINISTIC` handling | P2 | Optimization hints |
| `DROP FUNCTION` execution | P2 | Remove UDFs |

### Stored Procedures

| Feature | Priority | Description |
|---------|----------|-------------|
| `CREATE PROCEDURE` execution | P2 | Execute procedure definitions |
| `DROP PROCEDURE` execution | P2 | Remove procedures |
| `CALL` / `EXECUTE` execution | P2 | Invoke procedures |

### Extended Query Analysis

| Feature | Priority | Description |
|---------|----------|-------------|
| `EXPLAIN ANALYZE` | P2 | Actual execution statistics |
| `EXPLAIN (FORMAT JSON/TEXT)` | P2 | Alternative output formats |

### Database Administration

| Feature | Priority | Description |
|---------|----------|-------------|
| `CREATE DATABASE` | P2 | Create new database files |
| `DROP DATABASE` | P2 | Delete database files |
| `ATTACH DATABASE` | P2 | Attach external databases |
| `DETACH DATABASE` | P2 | Detach attached databases |
| `VACUUM` execution | P2 | Reclaim unused space |
| `ANALYZE` execution | P2 | Update statistics |
| `PRAGMA` support | P2 | Database configuration |

### Advanced Optimization

| Feature | Priority | Description |
|---------|----------|-------------|
| Statistics histograms | P2 | Better cardinality estimation |
| Adaptive query execution | P2 | Runtime plan adjustment |
| Predicate pushdown | P2 | Push filters to storage |

---

## See Also

- [README.md](README.md) - Documentation
- [STATUS.md](STATUS.md) - Implementation status
- [../../WitSQL.md](../../WitSQL.md) - Language specification
