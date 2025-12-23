# WitDatabase - Roadmap v2

**Version:** 1.0  
**Based on:** WitSql.md specification v1.2  
**Last Updated:** 2025-01-17

---

## Legend

| Symbol | Meaning |
|--------|---------|
| [x] | Implemented |
| [ ] | Not implemented |

**Priority Legend:**
- **P2** = Optional (nice-to-have features)

---

## Overview

This document contains features deferred to version 2 of WitDatabase. These are optional/advanced features that are not required for initial ADO.NET and EF Core compatibility.

---

## Overall v2 Progress Summary

| Component | Total Features | Implemented | Missing | Progress |
|-----------|----------------|-------------|---------|----------|
| **OutWit.Database.Core** | 9 | 0 | 9 | 0% |
| **OutWit.Database.Parser** | 18 | 0 | 18 | 0% |
| **OutWit.Database** (Engine) | 25+ | 0 | 25+ | 0% |

---

# Part 1: Core Components (v2)

## 1. Cursor Support

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `ICursor` interface | [ ] | P2 | Scrollable cursors |
| Forward-only mode | [ ] | P2 | Basic cursor |
| Scrollable mode | [ ] | P2 | Bidirectional |
| Fetch size (batching) | [ ] | P2 | Performance optimization |

## 2. Advanced Statistics

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `ANALYZE` command support | [ ] | P2 | Update statistics |
| Column cardinality estimation | [ ] | P2 | Query optimization |
| Histogram statistics | [ ] | P2 | Distribution analysis |

## 3. VACUUM / Compaction API

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| Explicit `Vacuum()` method for BTree | [ ] | P2 | Reclaim space |
| Incremental vacuum support | [ ] | P2 | Background compaction |
| Compaction progress/status API | [ ] | P2 | Monitoring |

---

# Part 2: Parser Components (v2)

## 4. User-Defined Functions (SS22)

| Feature | Status | Priority | Spec |
|---------|--------|----------|------|
| `CREATE FUNCTION ... RETURNS ... AS BEGIN END` | [ ] | P2 | SS22.1 |
| `RETURNS TABLE (...)` | [ ] | P2 | SS22.2 |
| `DETERMINISTIC` modifier | [ ] | P2 | SS22.1 |
| `DROP FUNCTION [IF EXISTS]` | [ ] | P2 | SS22 |

**Example:**
```sql
CREATE FUNCTION FormatPrice(price DECIMAL)
RETURNS VARCHAR(20)
DETERMINISTIC
AS
BEGIN
    RETURN '$' || CAST(ROUND(price, 2) AS VARCHAR);
END;
```

## 5. Stored Procedures (SS23)

| Feature | Status | Priority | Spec |
|---------|--------|----------|------|
| `CREATE PROCEDURE ... AS BEGIN END` | [ ] | P2 | SS23 |
| `DROP PROCEDURE [IF EXISTS]` | [ ] | P2 | SS23 |
| `CALL procedure(args)` | [ ] | P2 | SS23 |
| `EXECUTE procedure(args)` | [ ] | P2 | SS23 |

**Example:**
```sql
CREATE PROCEDURE TransferFunds(
    @FromAccount BIGINT,
    @ToAccount BIGINT,
    @Amount DECIMAL
)
AS
BEGIN
    BEGIN TRANSACTION;
    
    UPDATE Accounts SET Balance = Balance - @Amount 
    WHERE Id = @FromAccount;
    
    UPDATE Accounts SET Balance = Balance + @Amount 
    WHERE Id = @ToAccount;
    
    COMMIT;
END;

CALL TransferFunds(1001, 1002, 500.00);
```

## 6. EXPLAIN / Query Analysis (SS25)

| Feature | Status | Priority | Spec |
|---------|--------|----------|------|
| `EXPLAIN select_statement` | [ ] | P2 | SS25.1 |
| `EXPLAIN ANALYZE` | [ ] | P2 | SS25.1 |
| `EXPLAIN (FORMAT JSON/TEXT)` | [ ] | P2 | SS25.1 |

**Example:**
```sql
EXPLAIN SELECT * FROM Users WHERE Age > 18;

EXPLAIN ANALYZE SELECT * FROM Orders WHERE Status = 'pending';

EXPLAIN (FORMAT JSON) SELECT * FROM Products;
```

## 7. Database Administration (SS26)

| Feature | Status | Priority | Spec |
|---------|--------|----------|------|
| `CREATE DATABASE` | [ ] | P2 | SS26.1 |
| `DROP DATABASE [IF EXISTS]` | [ ] | P2 | SS26.1 |
| `ATTACH DATABASE 'path' AS alias` | [ ] | P2 | SS26.1 |
| `DETACH DATABASE alias` | [ ] | P2 | SS26.1 |
| `VACUUM [table_name]` | [ ] | P2 | SS26.2 |
| `ANALYZE [table_name]` | [ ] | P2 | SS26.2 |
| `PRAGMA name [= value]` | [ ] | P2 | SS26.3 |

**Examples:**
```sql
CREATE DATABASE mydb;
DROP DATABASE IF EXISTS tempdb;

ATTACH DATABASE 'path/to/file.db' AS archive;
DETACH DATABASE archive;

VACUUM;
VACUUM Products;

ANALYZE;
ANALYZE Orders;

PRAGMA page_size;
PRAGMA cache_size = 10000;
PRAGMA journal_mode = WAL;
```

---

# Part 3: Engine Components (v2)

## 8. User-Defined Functions Execution

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `CREATE FUNCTION` execution | [ ] | P2 | Function definition storage |
| `RETURNS TABLE` support | [ ] | P2 | Table-valued functions |
| `DETERMINISTIC` handling | [ ] | P2 | Caching optimization |
| `DROP FUNCTION` execution | [ ] | P2 | Function removal |
| Function invocation | [ ] | P2 | Runtime execution |

## 9. Stored Procedures Execution

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `CREATE PROCEDURE` execution | [ ] | P2 | Procedure storage |
| `DROP PROCEDURE` execution | [ ] | P2 | Procedure removal |
| `CALL` / `EXECUTE` execution | [ ] | P2 | Procedure invocation |
| Parameter handling | [ ] | P2 | Input/output params |
| Local variables | [ ] | P2 | Procedure state |

## 10. Query Analysis

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `EXPLAIN` execution | [ ] | P2 | Show query plan |
| `EXPLAIN ANALYZE` | [ ] | P2 | Execute and measure |
| `EXPLAIN (FORMAT JSON/TEXT)` | [ ] | P2 | Output formats |
| Query plan visualization | [ ] | P2 | Plan tree |
| Cost estimation display | [ ] | P2 | Optimizer info |

## 11. Database Administration

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `CREATE DATABASE` | [ ] | P2 | Multi-database |
| `DROP DATABASE` | [ ] | P2 | Database removal |
| `ATTACH DATABASE` | [ ] | P2 | External databases |
| `DETACH DATABASE` | [ ] | P2 | Detach external |
| `VACUUM` execution | [ ] | P2 | Space reclamation |
| `ANALYZE` execution | [ ] | P2 | Statistics update |
| `PRAGMA` support | [ ] | P2 | Configuration |

## 12. Cursor Support (Engine)

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| `DECLARE CURSOR` | [ ] | P2 | Cursor declaration |
| `OPEN CURSOR` | [ ] | P2 | Cursor opening |
| `FETCH` | [ ] | P2 | Row fetching |
| `CLOSE CURSOR` | [ ] | P2 | Cursor cleanup |
| Scrollable cursors | [ ] | P2 | Bidirectional |

## 13. Advanced Query Optimization

| Feature | Status | Priority | Notes |
|---------|--------|----------|-------|
| Statistics-based optimization | [ ] | P2 | Use ANALYZE data |
| Histogram-based selectivity | [ ] | P2 | Better estimates |
| Adaptive query execution | [ ] | P2 | Runtime adjustment |

---

# Implementation Timeline

## v2 Development Plan

v2 features will be implemented after v1 is complete and stable:

### Phase 1: Core Enhancements (2-3 weeks)
- Cursor support
- VACUUM API
- Advanced statistics

### Phase 2: User-Defined Objects (4-6 weeks)
- User-defined functions (Parser + Engine)
- Stored procedures (Parser + Engine)
- Local variables and control flow

### Phase 3: Query Analysis (2-3 weeks)
- EXPLAIN infrastructure
- Query plan formatting
- Cost estimation display

### Phase 4: Database Administration (3-4 weeks)
- Multi-database support
- ATTACH/DETACH
- PRAGMA implementation

---

# Prerequisites

v2 features depend on the following v1 components being complete:

| v2 Feature | v1 Prerequisites |
|------------|------------------|
| Cursor Support | Query execution, Result sets |
| User-Defined Functions | Expression evaluation, Type system |
| Stored Procedures | Transactions, Multiple statements |
| EXPLAIN | Query planning, Index selection |
| VACUUM | B+Tree storage, Page management |
| Multi-database | File storage, Connection management |

---

# Files Reference

| File | Content |
|------|---------|
| `Roadmap.v1.md` | v1 features roadmap |
| `Roadmap.v2.md` | This v2 roadmap |
| `Roadmap.Core.md` | Core-only roadmap |
| `Roadmap.Parser.md` | Parser-only roadmap |
| `Roadmap.Engine.md` | Engine-only roadmap |
| `WitSql.md` | Language Specification |

---

**Last Updated:** 2025-01-17
