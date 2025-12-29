# OutWit.Database.AdoNet

ADO.NET data provider for WitDatabase - a high-performance embedded database engine for .NET.

## Overview

This package provides a full ADO.NET implementation for WitDatabase, allowing you to use familiar patterns like `DbConnection`, `DbCommand`, `DbDataReader`, and `DbTransaction` with WitDatabase.

## Features

- Full ADO.NET 2.0 compatible implementation
- Support for in-memory and file-based databases
- Connection pooling
- Transaction support with multiple isolation levels
- MVCC (Multi-Version Concurrency Control)
- Encryption support (AES-GCM, ChaCha20-Poly1305)
- Multiple storage engines (B-Tree, LSM-Tree)
- Async/await support throughout
- Cross-platform (.NET 9+)

## Installation

```
dotnet add package OutWit.Database.AdoNet
```

## Quick Start

### Basic Usage

```csharp
using OutWit.Database.AdoNet;

// Create and open connection
using var connection = new WitDbConnection("Data Source=mydb.witdb");
connection.Open();

// Create table
using var cmd = connection.CreateCommand();
cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100), Email VARCHAR(255))";
cmd.ExecuteNonQuery();

// Insert data
cmd.CommandText = "INSERT INTO Users (Id, Name, Email) VALUES (@id, @name, @email)";
cmd.Parameters.Add(new WitDbParameter("@id", 1));
cmd.Parameters.Add(new WitDbParameter("@name", "John Doe"));
cmd.Parameters.Add(new WitDbParameter("@email", "john@example.com"));
cmd.ExecuteNonQuery();

// Query data
cmd.CommandText = "SELECT * FROM Users";
using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader["Id"]}: {reader["Name"]} - {reader["Email"]}");
}
```

### In-Memory Database

```csharp
using var connection = new WitDbConnection("Data Source=:memory:");
connection.Open();
// Database exists only for the lifetime of the connection
```

### With Transactions

```csharp
using var connection = new WitDbConnection("Data Source=mydb.witdb");
connection.Open();

using var transaction = connection.BeginTransaction();
try
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = transaction;
    
    cmd.CommandText = "INSERT INTO Accounts (Id, Balance) VALUES (1, 1000)";
    cmd.ExecuteNonQuery();
    
    cmd.CommandText = "INSERT INTO Accounts (Id, Balance) VALUES (2, 500)";
    cmd.ExecuteNonQuery();
    
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### With Encryption

```csharp
using var connection = new WitDbConnection(
    "Data Source=secure.witdb;Encryption=aes-gcm;Password=MySecurePassword123");
connection.Open();
// All data is encrypted at rest
```

## Connection String Reference

### Core Properties

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Data Source | Path to database file or `:memory:` | Required | `Data Source=mydb.witdb` |
| Mode | Connection mode | ReadWriteCreate | `Mode=ReadOnly` |
| Read Only | Open database in read-only mode | false | `Read Only=true` |

### Storage Engine

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Store | Storage engine type | btree | `Store=lsm` |

Available storage engines:
- `btree` - B-Tree based storage (default, good for general use)
- `lsm` - LSM-Tree storage (optimized for write-heavy workloads)
- `inmemory` - In-memory storage (fastest, non-persistent)

### Encryption

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Encryption | Encryption algorithm | none | `Encryption=aes-gcm` |
| Password | Encryption password | none | `Password=secret123` |
| User | Username for key derivation | none | `User=admin` |

Available encryption algorithms:
- `aes-gcm` - AES-256-GCM (recommended)
- `chacha20-poly1305` - ChaCha20-Poly1305

### Caching

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Cache | Cache algorithm | clock | `Cache=lru` |
| CacheSize | Number of cached pages | 1000 | `CacheSize=5000` |

Available cache algorithms:
- `clock` - Clock algorithm (default)
- `lru` - Least Recently Used

### Transaction Settings

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Isolation Level | Default isolation level | ReadCommitted | `Isolation Level=Serializable` |
| MVCC | Enable MVCC | true | `MVCC=false` |
| Transactions | Enable transactions | true | `Transactions=true` |
| Journal | Journal mode | wal | `Journal=rollback` |

Available isolation levels:
- `ReadUncommitted` - Allows dirty reads
- `ReadCommitted` - Only committed data visible (default)
- `RepeatableRead` - Read locks held for transaction duration
- `Serializable` - Full serialization
- `Snapshot` - MVCC snapshot isolation

### Connection Pooling

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Pooling | Enable connection pooling | false | `Pooling=true` |
| Min Pool Size | Minimum connections in pool | 1 | `Min Pool Size=5` |
| Max Pool Size | Maximum connections in pool | 100 | `Max Pool Size=50` |

### Timeouts

| Property | Description | Default | Example |
|----------|-------------|---------|---------|
| Default Timeout | Command timeout in seconds | 30 | `Default Timeout=60` |

## Connection String Examples

### Simple File Database
```
Data Source=myapp.witdb
```

### Read-Only Access
```
Data Source=myapp.witdb;Mode=ReadOnly
```

### In-Memory Database
```
Data Source=:memory:
```

### With Encryption
```
Data Source=secure.witdb;Encryption=aes-gcm;Password=MyPassword123
```

### LSM Storage Engine
```
Data Source=./data;Store=lsm
```

### Full Configuration
```
Data Source=app.witdb;Store=btree;Cache=clock;CacheSize=5000;Encryption=aes-gcm;Password=secret;MVCC=true;Isolation Level=Snapshot
```

### With Connection Pooling
```
Data Source=app.witdb;Pooling=true;Min Pool Size=5;Max Pool Size=20
```

## Using DbProviderFactory

```csharp
// Register provider (typically done at application startup)
DbProviderFactories.RegisterFactory(
    WitDbProviderFactory.PROVIDER_INVARIANT_NAME, 
    WitDbProviderFactory.Instance);

// Use factory
var factory = DbProviderFactories.GetFactory(WitDbProviderFactory.PROVIDER_INVARIANT_NAME);
using var connection = factory.CreateConnection();
connection.ConnectionString = "Data Source=mydb.witdb";
connection.Open();
```

## ADO.NET Components

| Component | WitDatabase Class | Description |
|-----------|------------------|-------------|
| DbConnection | WitDbConnection | Database connection |
| DbCommand | WitDbCommand | SQL command execution |
| DbDataReader | WitDbDataReader | Forward-only data reader |
| DbTransaction | WitDbTransaction | Transaction management |
| DbParameter | WitDbParameter | Command parameters |
| DbParameterCollection | WitDbParameterCollection | Parameter collection |
| DbDataAdapter | WitDbDataAdapter | DataSet adapter |
| DbCommandBuilder | WitDbCommandBuilder | Auto-generate commands |
| DbConnectionStringBuilder | WitDbConnectionStringBuilder | Connection string builder |
| DbProviderFactory | WitDbProviderFactory | Provider factory |
| DbException | WitDbException | Database exceptions |

## Supported Data Types

| SQL Type | CLR Type | Notes |
|----------|----------|-------|
| INT, INTEGER | Int64 | All integers stored as 64-bit |
| BIGINT | Int64 | |
| SMALLINT, TINYINT | Int64 | Converted to Int64 |
| BOOLEAN, BOOL | Boolean | |
| FLOAT, DOUBLE | Double | |
| DECIMAL, NUMERIC | Decimal | |
| VARCHAR, TEXT | String | |
| CHAR | String | |
| BLOB, VARBINARY | Byte[] | |
| DATETIME | DateTime | |
| DATE | DateOnly | |
| TIME | TimeOnly | |
| DATETIMEOFFSET | DateTimeOffset | |
| GUID, UUID | Guid | |
| JSON | String | Validated JSON |

## Thread Safety

- `WitDbConnection` is not thread-safe; each thread should use its own connection
- Connection pooling handles concurrent connection requests safely
- Multiple readers can operate concurrently with MVCC enabled
- Write operations are serialized at the database level

## Error Handling

```csharp
try
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT * FROM NonExistentTable";
    cmd.ExecuteReader();
}
catch (WitDbException ex)
{
    Console.WriteLine($"Error Code: {ex.ErrorCode}");
    Console.WriteLine($"Message: {ex.Message}");
    
    switch (ex.ErrorCode)
    {
        case WitDbErrorCode.Syntax:
            // Handle syntax error
            break;
        case WitDbErrorCode.Constraint:
            // Handle constraint violation
            break;
        case WitDbErrorCode.Lock:
            // Handle lock timeout
            break;
    }
}
```

## Performance Tips

1. **Use connection pooling** for applications with frequent connect/disconnect patterns
2. **Use transactions** for multiple related operations
3. **Use parameters** instead of string concatenation to avoid SQL parsing overhead
4. **Choose appropriate storage engine**: B-Tree for balanced workloads, LSM for write-heavy
5. **Configure cache size** based on available memory and working set size
6. **Use async methods** for I/O-bound operations

## Compatibility

- .NET 9.0 or later
- .NET 10.0 or later
- Cross-platform (Windows, Linux, macOS)

## License

MIT License - see LICENSE file for details.

## Related Packages

- `OutWit.Database.Core` - Core database engine
- `OutWit.Database.EntityFramework` - Entity Framework Core provider (coming soon)
