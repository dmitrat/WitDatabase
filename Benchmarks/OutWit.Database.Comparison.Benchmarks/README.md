# WitDatabase Comparison Benchmarks

Comparative performance benchmarks between **WitDatabase**, **SQLite**, and **LiteDB**.

## Overview

This benchmark suite compares:
- **WitDatabase** - Pure .NET embedded SQL database with LSM-Tree storage
- **SQLite** - Native C library with .NET bindings (Microsoft.Data.Sqlite)
- **LiteDB** - Pure .NET embedded NoSQL document database

## Key Optimizations Applied

The benchmarks use optimal WitDatabase configuration:

1. **LSM-Tree Storage** - Better write performance than B-Tree
2. **AUTOINCREMENT Primary Keys** - Skips uniqueness validation (20x faster INSERTs)
3. **UNIQUE Indexes** for explicit PKs - O(log n) validation instead of O(n)
4. **Secondary Indexes** - For JOIN and filter performance
5. **Transactions** - Batch operations for better throughput

## Benchmark Categories

### Basic Benchmarks (`InsertBenchmarks`, `SelectBenchmarks`, `UpdateDeleteBenchmarks`)

Core CRUD operations comparing all three databases:
- INSERT with auto-generated PK (fair comparison)
- INSERT with explicit PK 
- Point queries
- Full table scan
- Aggregations (COUNT, SUM)
- UPDATE/DELETE operations

### INSERT with Explicit PK (`InsertExplicitPkBenchmarks`)

Compares INSERT performance when providing explicit primary key values:
- WitDatabase with UNIQUE index
- SQLite with INTEGER PRIMARY KEY
- LiteDB with explicit _id

### JOIN Benchmarks (`JoinBenchmarks`)

SQL JOIN operations (WitDb vs SQLite only - LiteDB doesn't support SQL):
- INNER JOIN 2/3/4 tables
- LEFT JOIN
- JOIN with GROUP BY
- JOIN with ORDER BY
- JOIN with WHERE filter

### Mixed Workload Benchmarks (`MixedWorkloadBenchmarks`)

Real-world OLTP scenarios (WitDb vs SQLite):
- 80/20 read/write (typical web app)
- 50/50 read/write (balanced)
- Analytics queries (complex JOINs, GROUP BY)
- Batch operations in transaction

### Index Benchmarks (`IndexBenchmarks`)

Index performance comparison (WitDb vs SQLite):
- Query by PK (indexed)
- Query by secondary index
- Query on non-indexed column
- Aggregation with GROUP BY on indexed column

## Running Benchmarks

```bash
cd Benchmarks/OutWit.Database.Comparison.Benchmarks
dotnet run -c Release
```

Or run specific benchmark:

```bash
dotnet run -c Release -- --filter "*InsertBenchmarks*"
dotnet run -c Release -- --filter "*SelectBenchmarks*"
dotnet run -c Release -- --filter "*JoinBenchmarks*"
```

## Configuration

### WitDatabase Setup
```csharp
// LSM-Tree for best write performance
var witDb = new WitDatabaseBuilder()
    .WithLsmTree(path)
    .WithTransactions()
    .Build();

// Use AUTOINCREMENT for fast INSERTs
CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, ...)

// Or create UNIQUE index for explicit PKs
CREATE TABLE T (Id INT PRIMARY KEY, ...)
CREATE UNIQUE INDEX IX_T_Id ON T(Id)
```

### SQLite Setup
```csharp
var conn = new SqliteConnection($"Data Source={path}");
// INTEGER PRIMARY KEY auto-increments in SQLite
CREATE TABLE T (Id INTEGER PRIMARY KEY, ...)
```

### LiteDB Setup
```csharp
var db = new LiteDatabase(path);
// Auto-generates _id or use explicit _id
col.Insert(new BsonDocument { ... });
```

## Expected Results

### INSERT Performance (10,000 rows in transaction)

| Database | Auto PK | Explicit PK |
|----------|---------|-------------|
| WitDatabase (LSM) | ~50-100 ms | ~100-200 ms* |
| SQLite | ~30-50 ms | ~30-50 ms |
| LiteDB | ~100-200 ms | ~150-250 ms |

*With UNIQUE index

### SELECT Performance (10,000 rows)

| Operation | WitDatabase | SQLite | LiteDB |
|-----------|-------------|--------|--------|
| Point Query (1000x) | ~50-100 ms | ~30-50 ms | ~100-200 ms |
| Full Scan | ~10-20 ms | ~5-10 ms | ~50-100 ms |
| COUNT(*) | ~10-20 ms | ~1-2 ms | ~5-10 ms |

### Notes

- SQLite is a highly optimized native C library - pure .NET can't match it for raw speed
- WitDatabase offers competitive performance for a pure .NET solution
- LiteDB is NoSQL - can't directly compare SQL operations like JOINs
- Streaming aggregation in WitDatabase uses O(1) memory

## Database Comparison

| Feature | WitDatabase | SQLite | LiteDB |
|---------|-------------|--------|--------|
| Language | Pure .NET | Native C | Pure .NET |
| Query Language | SQL | SQL | LINQ/Fluent |
| Transactions | ACID | ACID | ACID |
| Storage | LSM-Tree/B-Tree | B-Tree | B-Tree |
| Encryption | AES-GCM, ChaCha20 | SQLCipher | Yes |
| Blazor WASM | ? | ? (native) | ? |
| JOINs | ? | ? | ? |
| Window Functions | ? | ? | ? |
| JSON Support | ? | ? | Native |
