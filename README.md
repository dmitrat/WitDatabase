# WitDatabase

A high-performance embedded key-value database for .NET with support for multiple storage engines, ACID transactions, and encryption.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

## Features

- **Two Storage Engines**
  - **B-Tree** - Optimized for read-heavy workloads with excellent random access
  - **LSM-Tree** - For sustained high-volume ingest into tables with few or no secondary
    indexes; see `Docs/WitSQL.md` § 14.9 for where it wins and where it does not

- **Encryption**
  - AES-256-GCM with hardware acceleration
  - ChaCha20-Poly1305 via BouncyCastle (Blazor WASM compatible)
  - Password-based key derivation (PBKDF2)

- **ACID Transactions**
  - Atomicity, Consistency, Isolation, Durability
  - Write-Ahead Logging (WAL)
  - Crash recovery

- **Concurrency**
  - Reader-writer locking
  - File locking for multi-process safety
  - Async/await support

- **SQL Support**
  - Full SQL parser (WitSQL dialect)
  - ADO.NET provider
  - Entity Framework Core provider
  - Window functions, CTEs, subqueries
  - `LATERAL` / `CROSS APPLY` / `OUTER APPLY`, `VALUES` as a table source, `TOP n`
  - 60+ built-in functions

- **User-Defined Functions and Stored Procedures**
  - `CREATE FUNCTION` — a scalar expression over its parameters, callable anywhere an expression
    may appear: a `SELECT` list, a `WHERE`, a `CHECK`, a computed column, an index key
  - `CREATE PROCEDURE` / `CALL` — a body of statements, invoked as one unit of work, and reachable
    from ADO.NET through `CommandType.StoredProcedure`
  - SQL bodies only: no external code and no assembly loading
  - Reported by `INFORMATION_SCHEMA.ROUTINES` and `.PARAMETERS`

- **Fluent API**
  - Easy configuration with builder pattern
  - Extensible via extension methods
  - Simple static factory methods

- **Provider System**
  - Pluggable storage, encryption, cache, and journal providers
  - Auto-detection of settings when reopening databases
  - Easy registration of custom providers

## Packages

| Package | Description |
|---------|-------------|
| [OutWit.Database.Core](Sources/Core/OutWit.Database.Core/) | Core storage engine (B+Tree, LSM-Tree, MVCC) |
| [OutWit.Database.Core.BouncyCastle](Sources/Core/OutWit.Database.Core.BouncyCastle/) | ChaCha20-Poly1305 encryption provider |
| [OutWit.Database.Core.IndexedDb](Sources/Core/OutWit.Database.Core.IndexedDb/) | IndexedDB storage for Blazor WebAssembly |
| [OutWit.Database.Parser](Sources/Engine/OutWit.Database.Parser/) | SQL parser (ANTLR4-based) |
| [OutWit.Database](Sources/Engine/OutWit.Database/) | SQL execution engine |
| [OutWit.Database.AdoNet](Sources/Providers/OutWit.Database.AdoNet/) | ADO.NET provider |
| [OutWit.Database.EntityFramework](Sources/Providers/OutWit.Database.EntityFramework/) | Entity Framework Core provider |

## Installation

```bash
# Core storage engine
dotnet add package OutWit.Database.Core

# SQL engine with ADO.NET
dotnet add package OutWit.Database.AdoNet

# Entity Framework Core
dotnet add package OutWit.Database.EntityFramework

# Optional: BouncyCastle encryption (for Blazor WASM)
dotnet add package OutWit.Database.Core.BouncyCastle

# Optional: IndexedDB storage (for Blazor WASM)
dotnet add package OutWit.Database.Core.IndexedDb
```

## Quick Start

### Key-Value Storage (Core API)

```csharp
using OutWit.Database.Core.Builder;

// Create a new database
using var db = WitDatabase.Create("mydata.db");

// Or with encryption
using var db = WitDatabase.Create("secure.db", "my-password");

// Store and retrieve data
db.Put("user:1"u8, """{"name": "John", "age": 30}"""u8);
var value = db.Get("user:1"u8);
db.Delete("user:1"u8);
```

### SQL with ADO.NET

```csharp
using OutWit.Database.AdoNet;

using var connection = new WitDbConnection("Data Source=mydb.witdb");
connection.Open();

using var cmd = connection.CreateCommand();
cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
cmd.ExecuteNonQuery();

cmd.CommandText = "INSERT INTO Users (Id, Name) VALUES (@id, @name)";
cmd.Parameters.AddWithValue("@id", 1);
cmd.Parameters.AddWithValue("@name", "John Doe");
cmd.ExecuteNonQuery();
```

### Functions and Stored Procedures

```csharp
using var cmd = connection.CreateCommand();

// A function is a scalar expression over its parameters, callable anywhere an
// expression may appear - a SELECT list, a WHERE, a CHECK, an index key.
cmd.CommandText = "CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END";
cmd.ExecuteNonQuery();

cmd.CommandText = "SELECT Doubled(21)";
var answer = cmd.ExecuteScalar();          // 42

// A procedure is a body of statements. The last statement's result is the call's.
cmd.CommandText = @"
    CREATE PROCEDURE RecentUsers AS BEGIN
        SELECT * FROM Users ORDER BY Id DESC;
    END";
cmd.ExecuteNonQuery();

// Invoked the ordinary ADO.NET way.
using var call = connection.CreateCommand();
call.CommandType = CommandType.StoredProcedure;
call.CommandText = "RecentUsers";

using var reader = call.ExecuteReader();
```

Bodies are SQL — no external code and no assembly loading. See
[Docs/WitSQL.md](Docs/WitSQL.md) § 2.10–2.11 for the rules: what a body may contain, how determinism
decides whether a function may key an index, and why a trigger body may not `CALL`.

### Entity Framework Core

```csharp
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseWitDb("Data Source=myapp.witdb");
}

// Usage
using var context = new AppDbContext();
context.Database.EnsureCreated();
context.Users.Add(new User { Name = "John" });
context.SaveChanges();
```

### Blazor WebAssembly

```csharp
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.IndexedDb;
using OutWit.Database.Core.BouncyCastle;

// In Blazor component
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("MyDatabase", JSRuntime)
    .WithBouncyCastleEncryption("password")  // Works in browser
    .WithBTree()
    .Build();

await ((StorageIndexedDb)db.Store).InitializeAsync();
```

## Configuration

### Storage Engines

| Method | Description |
|--------|-------------|
| `WithFilePath(path)` | Use file-based storage |
| `WithMemoryStorage()` | Use in-memory storage |
| `WithBTree()` | Use B-Tree engine (default) |
| `WithLsmTree()` | Use LSM-Tree engine |

### Encryption

| Method | Description |
|--------|-------------|
| `WithEncryption(password)` | AES-GCM with password |
| `WithBouncyCastleEncryption(password)` | ChaCha20-Poly1305 |

### Transactions

| Method | Description |
|--------|-------------|
| `WithTransactions()` | Enable ACID transactions |
| `WithMvcc()` | Enable MVCC |
| `WithFileLocking()` | Enable file locking |

## Architecture

```
+---------------------------------------------------------------+
|                      WitDatabaseBuilder                       |
|            (Fluent API for database configuration)            |
+---------------------------------------------------------------+
|                    TransactionalStore                         |
|                 (ACID transactions, locking)                  |
+---------------------------------------------------------------+
|      +----------------+    +----------------+                 |
|      |   StoreBTree   |    |    StoreLsm    |                 |
|      |  (B+Tree engine)|    | (LSM-Tree engine)|              |
|      +----------------+    +----------------+                 |
+---------------------------------------------------------------+
|                     ProviderRegistry                          |
|          (Pluggable providers for all components)             |
+---------------------------------------------------------------+
```

## Performance

Measured with `Benchmarks/OutWit.Database.Benchmarks` (BenchmarkDotNet, ShortRun, in-process) on a
Ryzen 9 5950X under .NET 10, against SQLite (`Microsoft.Data.Sqlite`) and LiteDB. **Every figure is
the median of at least two full passes**, and anything that moved more than 10% between identical
passes is excluded rather than quoted. **Default configuration** means a bare `Data Source=…`
connection string - MVCC on, durable commit, B+Tree - which is what an ADO.NET or EF Core consumer
gets.

### Reads and lookups, default configuration

| Operation | WitDatabase | LiteDB | SQLite |
|---|---|---|---|
| Point query by primary key, x100 | **0.24 ms** | 1.23 ms | 4.83 ms |
| Seek on a UNIQUE index, x100 | **0.50 ms** | 2.00 ms | 5.01 ms |
| Sequential reads, x100 | **0.31 ms** | 8.77 ms | 5.18 ms |
| Index range scan (`BETWEEN`) | **0.83 ms** | 4.63 ms | 0.20 ms |
| `SELECT … LIMIT 100` | **0.08 ms** | 0.15 ms | 0.07 ms |
| Full scan, 1,000 rows | 0.77 ms | 1.34 ms | **0.17 ms** |
| `ORDER BY`, 1,000 rows | 1.62 ms | 1.78 ms | **0.23 ms** |

### Writes, default configuration

| Operation | WitDatabase | LiteDB | SQLite |
|---|---|---|---|
| 100 inserts in one transaction | 2.99 ms | **1.17 ms** | 7.38 ms |
| Transaction rollback | **0.46 ms** | 7.15 ms | 1.36 ms |
| Bulk `UPDATE` | **3.10 ms** | 9.62 ms | 7.19 ms |
| 100 inserts **without** a transaction | 203 ms | **9.5 ms** | 688 ms |

### Sustained ingest, 500,000 rows in batches of 1,000

| | B+Tree | LSM |
|---|---|---|
| No secondary indexes | 15.10 µs/row | 15.36 µs/row |
| Three secondary indexes | **23.16 µs/row** | 36.86 µs/row |

Read all of it honestly:

- **Lookups and indexed access are the strong side** - several times faster than LiteDB and, on
  small operations, than SQLite. Part of the SQLite margin is the P/Invoke crossing its managed
  wrapper pays per call; that is not an engine result, but it *is* what a .NET consumer experiences,
  because there is no other way to reach SQLite from managed code.
- **Scans and sorts are the weak side** - SQLite is 4-8x faster there, and the gap is allocation: a
  scan costs roughly 2.4 KB per row returned. LiteDB, also managed, pays about the same, so this
  does not move the comparison against it.
- **Autocommit is expensive by choice.** Every statement outside a transaction is durable, which is
  what makes 203 ms against LiteDB's 9.5 - and 3.3x *faster* than SQLite doing the same thing.
  Batch writes in a transaction when throughput matters.
- **`Store=lsm` is not simply "write-optimised".** It reaches parity at high volume without
  secondary indexes and is 1.6x slower with three of them. See `Docs/WitSQL.md` § 14.9 before
  choosing it.

Reproduce with:

```bash
dotnet run -c Release --project Benchmarks/OutWit.Database.Benchmarks -- --filter "*"
dotnet run -c Release --project Benchmarks/OutWit.Database.Benchmarks -- verify
```

The second command is the equivalence check: it runs every benchmark body once and compares what
WitDatabase, SQLite and LiteDB actually return. A timing comparison between engines that do not
compute the same thing is not a measurement, so that check is meant to be green before any figure
above is believed.

> **Earlier figures withdrawn twice, and the second time is worth recording.** Releases before
> 11.1.0 advertised transaction ratios measured on the least discriminating workload in the suite,
> and before that "4-20x faster" numbers from a benchmark project no longer in the repository. The
> tables above replace both. What changed in between: the benchmark suite had no assertion of any
> kind in 113 methods, so nothing had ever checked that the three engines compute the same answer,
> and several published claims turned out to describe an engine that no longer existed.

## Requirements

- .NET 10.0
- Windows, Linux, or macOS

## Project Structure

```
WitDatabase/
+-- Sources/
|   +-- Core/
|   |   +-- OutWit.Database.Core/           # Storage engine
|   |   +-- OutWit.Database.Core.BouncyCastle/  # ChaCha20 encryption
|   |   +-- OutWit.Database.Core.IndexedDb/     # Blazor WASM storage
|   +-- Engine/
|   |   +-- OutWit.Database.Parser/         # SQL parser
|   |   +-- OutWit.Database/                # SQL engine
|   +-- Providers/
|       +-- OutWit.Database.AdoNet/         # ADO.NET provider
|       +-- OutWit.Database.EntityFramework/ # EF Core provider
+-- Tools/
|   +-- OutWit.Database.Studio/             # Database management tool
+-- Samples/
+-- Benchmarks/
```

## Documentation

- [Docs/WitSQL.md](Docs/WitSQL.md) - The WitSQL language specification: types, statements,
  functions, routines, transactions and concurrency
- [Docs/KnownIssues.md](Docs/KnownIssues.md) - Known issues
- [CHANGELOG.md](CHANGELOG.md) - What changed in each release, and why
- [Sources/Core/OutWit.Database.Core/EXTENSIBILITY.md](Sources/Core/OutWit.Database.Core/EXTENSIBILITY.md) - Extension guide

The audit and the phase plans are working papers rather than documentation and are no longer shipped
in `Docs/`. Source comments that cite one by name - `AUDIT-2026-07.md`, `NEXT-SESSION-PLAN.md`,
`PHASE*-*.md` - refer to files that remain in the git history.

## Running Tests

```bash
# All tests
dotnet test

# Specific project
dotnet test Sources/Core/OutWit.Database.Core.Tests
```

## License

Licensed under the Apache License, Version 2.0. See `LICENSE`.

## Attribution (optional)

If you use WitDatabase in a product, a mention is appreciated (but not required), for example:
"Powered by WitDatabase https://witdatabase.io/".

## Trademark / Project name

"WitDatabase" and the WitDatabase logo are used to identify the official project by Dmitry Ratner.

You may:
- refer to the project name in a factual way (e.g., "built with WitDatabase");
- use the name to indicate compatibility (e.g., "WitDatabase-compatible").

You may not:
- use "WitDatabase" as the name of a fork or a derived product in a way that implies it is the official project;
- use the WitDatabase logo to promote forks or derived products without permission.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history. Each package carries its own `ROADMAP.md` for
what is planned in that layer.
