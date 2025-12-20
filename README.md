# WitDatabase

A high-performance embedded key-value database for .NET with support for multiple storage engines, ACID transactions, and encryption.

[![.NET](https://img.shields.io/badge/.NET-9.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## Features

- ?? **Two Storage Engines**
  - **B-Tree** - Optimized for read-heavy workloads with excellent random access
  - **LSM-Tree** - Optimized for write-heavy workloads with sequential write performance

- ?? **Encryption**
  - AES-256-GCM with hardware acceleration
  - ChaCha20-Poly1305 via BouncyCastle (Blazor WASM compatible)
  - Password-based key derivation (PBKDF2)

- ?? **ACID Transactions**
  - Atomicity, Consistency, Isolation, Durability
  - Write-Ahead Logging (WAL)
  - Crash recovery

- ? **Concurrency**
  - Reader-writer locking
  - File locking for multi-process safety
  - Async/await support

- ?? **Fluent API**
  - Easy configuration with builder pattern
  - Extensible via extension methods
  - Simple static factory methods

- ?? **Provider System**
  - Pluggable storage, encryption, cache, and journal providers
  - Auto-detection of settings when reopening databases
  - Easy registration of custom providers

## Installation

```bash
# Main package
dotnet add package OutWit.Database.Core

# Optional: BouncyCastle encryption (for Blazor WASM)
dotnet add package OutWit.Database.Core.BouncyCastle
```

## Quick Start

### Simplest Usage (Static Factory Methods)

```csharp
using OutWit.Database.Core.Builder;

// Create a new database
using var db = WitDatabase.Create("mydata.db");

// Or open existing (auto-detects settings!)
using var db = WitDatabase.Open("mydata.db");

// Create or open (most common)
using var db = WitDatabase.CreateOrOpen("mydata.db");

// With encryption
using var db = WitDatabase.Create("secure.db", "my-password");
using var db = WitDatabase.Open("secure.db", "my-password");

// In-memory (for testing)
using var db = WitDatabase.CreateInMemory();
```

### Basic Operations

```csharp
using var db = WitDatabase.CreateOrOpen("mydata.db");

// Store data
db.Put("user:1"u8, """{"name": "John", "age": 30}"""u8);

// Retrieve data
var value = db.Get("user:1"u8);

// Delete data
db.Delete("user:1"u8);

// Scan range
foreach (var (key, val) in db.Scan("user:"u8.ToArray(), "user:\xff"u8.ToArray()))
{
    Console.WriteLine($"Key: {Encoding.UTF8.GetString(key)}");
}
```

### With Transactions

```csharp
using var db = WitDatabase.CreateOrOpen("mydata.db");

// Explicit transaction
using (var tx = db.BeginTransaction())
{
    tx.Put("key1"u8, "value1"u8);
    tx.Put("key2"u8, "value2"u8);
    tx.Commit(); // or tx.Rollback()
}

// Async transaction
await using (var tx = await db.BeginTransactionAsync())
{
    await tx.PutAsync(key, value);
    await tx.CommitAsync();
}
```

### Full Configuration (Builder Pattern)

```csharp
using OutWit.Database.Core.Builder;

// Create a database with custom settings
using var db = new WitDatabaseBuilder()
    .WithFilePath("mydata.db")
    .WithBTree()
    .WithEncryption("my-password")
    .WithTransactions()
    .WithFileLocking()
    .Build();

// Advanced settings for LSM-Tree
using var lsmDb = new WitDatabaseBuilder()
    .WithLsmTree("/data/lsm", opts =>
    {
        opts.EnableWal = true;
        opts.EnableBlockCache = true;
        opts.BlockCacheSizeBytes = 64 * 1024 * 1024; // 64MB
        opts.MemTableSizeLimit = 4 * 1024 * 1024;    // 4MB
        opts.BackgroundCompaction = true;
    })
    .WithEncryption("password")
    .WithTransactions()
    .Build();
```

### BouncyCastle Encryption (Blazor WASM)

```csharp
using OutWit.Database.Core.BouncyCastle;

// ChaCha20-Poly1305 - works in Blazor WebAssembly
using var db = new WitDatabaseBuilder()
    .WithFilePath("secure.db")
    .WithBouncyCastleEncryption("my-password")
    .Build();
```

## Configuration Options

### Storage

| Method | Description |
|--------|-------------|
| `WithFilePath(path)` | Use file-based storage |
| `WithMemoryStorage()` | Use in-memory storage (data lost on dispose) |
| `WithStorage(IStorage)` | Use custom storage implementation |

### Engine

| Method | Description |
|--------|-------------|
| `WithBTree()` | Use B-Tree engine (default) |
| `WithLsmTree()` | Use LSM-Tree engine |
| `WithLsmTree(directory)` | LSM-Tree with specific directory |
| `WithLsmTree(Action<LsmOptions>)` | LSM-Tree with custom options |

### Encryption

| Method | Description |
|--------|-------------|
| `WithEncryption(password)` | AES-GCM with password |
| `WithEncryption(user, password)` | AES-GCM with user/password |
| `WithAesEncryption(key)` | AES-GCM with 256-bit key |
| `WithBouncyCastleEncryption(password)` | ChaCha20-Poly1305 with password |

### Transactions & Locking

| Method | Description |
|--------|-------------|
| `WithTransactions()` | Enable ACID transactions (default) |
| `WithoutTransactions()` | Disable transactions for performance |
| `WithFileLocking()` | Enable file locking (default) |
| `WithoutFileLocking()` | Disable file locking |
| `WithLockTimeout(TimeSpan)` | Set lock timeout |

### Performance

| Method | Description |
|--------|-------------|
| `WithPageSize(int)` | Set page size (default: 4096) |
| `WithCacheSize(int)` | Set cache size in pages |

## Provider System

WitDatabase uses a pluggable provider system for extensibility. All providers implement `IProvider` interface.

### Built-in Providers

| Type | Provider Keys | Description |
|------|---------------|-------------|
| `IStorage` | `file`, `memory`, `encrypted` | Storage backends |
| `IKeyValueStore` | `btree`, `lsm`, `inmemory` | Storage engines |
| `ICryptoProvider` | `aes-gcm` | Encryption algorithms |
| `IPageCache` | `clock`, `lru` | Page caching strategies |
| `ITransactionJournal` | `rollback`, `wal` | Transaction journals |

### BouncyCastle Extension

| Type | Provider Keys | Description |
|------|---------------|-------------|
| `ICryptoProvider` | `chacha20-poly1305` | ChaCha20-Poly1305 encryption |

### Registering Custom Providers

```csharp
using OutWit.Database.Core.Providers;

// Register a custom crypto provider
ProviderRegistry.Instance.Register<ICryptoProvider>("my-crypto", 
    parameters => new MyCryptoProvider(parameters.GetRequired<byte[]>("key")));

// Use it via the registry
var crypto = ProviderRegistry.Instance.Create<ICryptoProvider>("my-crypto",
    new ProviderParameters().Set("key", myKey));

// Or list all registered providers
var keys = ProviderRegistry.Instance.GetRegisteredKeys<ICryptoProvider>();
// Returns: ["aes-gcm", "chacha20-poly1305", "my-crypto"]
```

### Auto-Detection on Open

When opening an existing database, WitDatabase automatically reads stored settings:

```csharp
// Database created with specific settings
using (var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithBTree()
    .WithTransactions()
    .WithFileLocking()
    .Build())
{
    db.Put("key"u8, "value"u8);
}

// Later: Open() auto-detects settings from header
using var db = WitDatabase.Open("data.db");
// Transactions, FileLocking settings are restored automatically!

// Inspect database without opening
var info = WitDatabase.GetDatabaseInfo("data.db");
Console.WriteLine($"Store: {info.StoreProvider}");           // "btree"
Console.WriteLine($"Encrypted: {info.RequiresEncryption}");  // false
Console.WriteLine($"Transactions: {info.HasTransactions}");  // true
```

### Settings Persisted in Header

| Setting | Persisted | Notes |
|---------|-----------|-------|
| Store type (btree/lsm) | ? | Auto-detected on reopen |
| Encryption enabled | ? | Throws if password not provided |
| Encryption provider | ? | e.g., "aes-gcm" |
| Transactions enabled | ? | Auto-detected on reopen |
| File locking enabled | ? | Auto-detected on reopen |
| Page size | ? | Mismatch throws exception |
| Cache type/size | ? | Can be changed on reopen |

## Architecture

```
???????????????????????????????????????????????????????????????????
?                      WitDatabaseBuilder                         ?
?            (Fluent API for database configuration)              ?
???????????????????????????????????????????????????????????????????
?                    TransactionalStore                           ?
?                 (ACID transactions, locking)                    ?
???????????????????????????????????????????????????????????????????
?      ????????????????????    ????????????????????              ?
?      ?   StoreBTree     ?    ?    StoreLsm      ?              ?
?      ?  (B+Tree engine) ?    ? (LSM-Tree engine)?              ?
?      ????????????????????    ????????????????????              ?
?               ?                       ?                         ?
?      ????????????????????    ????????????????????              ?
?      ?    IStorage      ?    ?MemTable+SSTables ?              ?
?      ? File/Memory/Enc  ?    ?  +WAL+BloomFilter?              ?
?      ????????????????????    ????????????????????              ?
???????????????????????????????????????????????????????????????????
?                     ProviderRegistry                            ?
?          (Pluggable providers for all components)               ?
???????????????????????????????????????????????????????????????????
```

## Storage Engines Comparison

| Feature | B-Tree | LSM-Tree |
|---------|--------|----------|
| Read performance | ????? | ??? |
| Write performance | ??? | ????? |
| Range scan | ????? | ???? |
| Space efficiency | ???? | ??? |
| Write amplification | Medium | Low |
| Read amplification | Low | Medium |

**Recommendations:**
- **B-Tree**: General purpose, read-heavy workloads, random access patterns
- **LSM-Tree**: Write-heavy workloads, time-series data, logging

## Encryption Providers

| Provider | Algorithm | Use Case |
|----------|-----------|----------|
| `aes-gcm` | AES-256-GCM | Standard (hardware accelerated) |
| `chacha20-poly1305` | ChaCha20-Poly1305 | Blazor WASM, ARM without AES-NI |

## Thread Safety

- ? Multiple concurrent readers
- ? Single writer with serialization
- ? Reader-writer lock with writer priority
- ? File locking for multi-process safety
- ? Nested transactions (throws `LockRecursionException`)

## Performance

Typical performance on modern hardware:

| Operation | B-Tree (File) | LSM-Tree |
|-----------|---------------|----------|
| Put (1K ops) | ~5ms | ~2ms |
| Put (10K ops) | ~39ms | ~15ms |
| Get (1K ops) | ~2ms | ~3ms |
| Range scan (1K entries) | ~1ms | ~2ms |

## Requirements

- .NET 9.0 or .NET 10.0
- Windows, Linux, or macOS

## Project Structure

```
WitDatabase/
??? OutWit.Database.Core/           # Main library
?   ??? Builder/                    # Fluent API
?   ??? Providers/                  # Provider system
?   ??? Tree/                       # B-Tree implementation
?   ??? LSM/                        # LSM-Tree components
?   ??? Storage/                    # Storage implementations
?   ??? Transactions/               # Transaction support
?   ??? Encryption/                 # Encryption support
?   ??? Concurrency/                # Locking subsystem
??? OutWit.Database.Core.BouncyCastle/  # BouncyCastle crypto
??? OutWit.Database.Core.Tests/     # Unit tests (~1100 tests)
??? OutWit.Database.Core.Tests.Benchmarks/  # Benchmarks
```

## Running Tests

```bash
# All tests
dotnet test

# Specific category
dotnet test --filter "FullyQualifiedName~Builder"
dotnet test --filter "FullyQualifiedName~Encryption"
dotnet test --filter "FullyQualifiedName~Transaction"
dotnet test --filter "FullyQualifiedName~Provider"
```

## Running Benchmarks

```bash
cd OutWit.Database.Core.Tests.Benchmarks
dotnet run -c Release
```

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Changelog

### v1.1.0 (Current)

- **Provider System**: Pluggable architecture for all components
  - `ProviderRegistry` for centralized provider management
  - Auto-registration via `[ModuleInitializer]`
  - `IProvider` base interface for all providers
- **Auto-Detection**: `WitDatabase.Open()` auto-detects settings from header
- **Database Info**: `WitDatabase.GetDatabaseInfo()` to inspect without opening
- **Configuration Validation**: Detects mismatched settings on reopen
- **ProviderMetadata**: Stored in database header for persistence

### v1.0.0

- Initial release
- B-Tree and LSM-Tree storage engines
- ACID transactions with WAL
- AES-GCM and ChaCha20-Poly1305 encryption
- Fluent API builder
- Password-based encryption
- ~1100 unit tests
