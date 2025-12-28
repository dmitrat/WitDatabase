# OutWit.Database.Parser - v2 Roadmap

**Version:** 2.0  
**Last Updated:** 2025-02-05

---

## v1 Status: 100% Complete

All v1 features (298 features) are implemented. See [STATUS.md](STATUS.md) for details.

**Test Coverage:** 1000+ tests passing

---

## v2 Planned Features

### User-Defined Functions

| Feature | Priority | Spec |
|---------|----------|------|
| `CREATE FUNCTION ... RETURNS ... AS BEGIN END` | P2 | SS22.1 |
| `RETURNS TABLE (...)` | P2 | SS22.2 |
| `DETERMINISTIC` modifier | P2 | SS22.1 |
| `DROP FUNCTION [IF EXISTS]` | P2 | SS22 |

### Stored Procedures

| Feature | Priority | Spec |
|---------|----------|------|
| `CREATE PROCEDURE ... AS BEGIN END` | P2 | SS23 |
| `DROP PROCEDURE [IF EXISTS]` | P2 | SS23 |
| `CALL procedure(args)` | P2 | SS23 |
| `EXECUTE procedure(args)` | P2 | SS23 |

### Extended EXPLAIN

| Feature | Priority | Spec |
|---------|----------|------|
| `EXPLAIN ANALYZE` | P2 | SS25.1 |
| `EXPLAIN (FORMAT JSON/TEXT)` | P2 | SS25.1 |

### Database Administration

| Feature | Priority | Spec |
|---------|----------|------|
| `CREATE DATABASE` | P2 | SS26.1 |
| `DROP DATABASE [IF EXISTS]` | P2 | SS26.1 |
| `ATTACH DATABASE 'path' AS alias` | P2 | SS26.1 |
| `DETACH DATABASE alias` | P2 | SS26.1 |
| `VACUUM [table_name]` | P2 | SS26.2 |
| `ANALYZE [table_name]` | P2 | SS26.2 |
| `PRAGMA name [= value]` | P2 | SS26.3 |

---

## See Also

- [README.md](README.md) - Documentation
- [STATUS.md](STATUS.md) - Implementation status
- [../../WitSQL.md](../../WitSQL.md) - Language specification
