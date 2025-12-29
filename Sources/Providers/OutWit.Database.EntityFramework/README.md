# OutWit.Database.EntityFramework

Entity Framework Core provider for WitDatabase - a high-performance embedded database engine for .NET.

## Overview

This package provides Entity Framework Core support for WitDatabase, allowing you to use familiar EF Core patterns like DbContext, DbSet, LINQ queries, and migrations.

## Installation

```
dotnet add package OutWit.Database.EntityFramework
```

## Quick Start

### Basic Usage

```csharp
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

// Define your DbContext
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime OrderDate { get; set; }
    public User User { get; set; }
}

// Configure and use
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseWitDb("Data Source=myapp.witdb")
    .Options;

using var context = new AppDbContext(options);
```

### In-Memory Database

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseWitDbInMemory()
    .Options;

using var context = new AppDbContext(options);
// Database exists only for the lifetime of the context
```

### With Encryption

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseWitDb("Data Source=secure.witdb;Encryption=aes-gcm;Password=MySecurePassword")
    .Options;
```

### With Dependency Injection

```csharp
// In Program.cs or Startup.cs
services.AddDbContext<AppDbContext>(options =>
    options.UseWitDb(Configuration.GetConnectionString("DefaultConnection")));
```

## Supported Data Types

| C# Type | WitSQL Type | Notes |
|---------|-------------|-------|
| `bool` | `BOOLEAN` | |
| `byte` | `UTINYINT` | |
| `sbyte` | `TINYINT` | |
| `short` | `SMALLINT` | |
| `ushort` | `USMALLINT` | |
| `int` | `INT` | |
| `uint` | `UINT` | |
| `long` | `BIGINT` | |
| `ulong` | `UBIGINT` | |
| `float` | `FLOAT` | |
| `double` | `DOUBLE` | |
| `decimal` | `DECIMAL` | |
| `string` | `TEXT` | |
| `byte[]` | `BLOB` | |
| `DateTime` | `DATETIME` | |
| `DateTimeOffset` | `DATETIMEOFFSET` | |
| `DateOnly` | `DATE` | |
| `TimeOnly` | `TIME` | |
| `TimeSpan` | `INTERVAL` | |
| `Guid` | `GUID` | |
| `Enum` | `INT` | Stored as integer by default |

## Connection String Options

All connection string options from `OutWit.Database.AdoNet` are supported:

| Option | Description | Example |
|--------|-------------|---------|
| `Data Source` | Database file path or `:memory:` | `Data Source=mydb.witdb` |
| `Mode` | Connection mode | `Mode=ReadOnly` |
| `Encryption` | Encryption algorithm | `Encryption=aes-gcm` |
| `Password` | Encryption password | `Password=secret` |
| `Store` | Storage engine | `Store=btree` or `Store=lsm` |

## Features

### Completed (Phase 1-6)

- ? DbContext configuration with `UseWitDb()`
- ? In-memory database support
- ? Type mapping for all WitSQL types
- ? Basic SQL generation (SELECT, INSERT, UPDATE, DELETE)
- ? Parameter handling with `@param` syntax
- ? LIMIT/OFFSET for pagination
- ? Model validation

### In Progress (Phase 7-10)

- ?? Migrations support
- ?? EnsureCreated/EnsureDeleted
- ?? LINQ method translations
- ?? JSON column support
- ?? Computed columns

## Requirements

- .NET 9.0 or .NET 10.0
- Microsoft.EntityFrameworkCore.Relational 9.0+
- OutWit.Database.AdoNet

## Related Packages

- `OutWit.Database.Core` - Core database engine
- `OutWit.Database.AdoNet` - ADO.NET provider

## License

MIT License - see LICENSE file for details.
