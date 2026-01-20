# OutWit.Database.EntityFramework

Entity Framework Core provider for WitDatabase - a high-performance embedded database engine for .NET.

## Overview

This package provides Entity Framework Core support for WitDatabase, allowing you to use familiar EF Core patterns like DbContext, DbSet, LINQ queries, and migrations.

## Installation

```bash
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

## Advanced Features

### Row Versioning (Optimistic Concurrency)

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Version { get; set; }  // Row version column
}

// In OnModelCreating
modelBuilder.Entity<Product>(entity =>
{
    entity.Property(e => e.Version).IsWitRowVersion();
});
```

### Computed Columns

```csharp
public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string FullName { get; set; }  // Computed column
}

// In OnModelCreating
modelBuilder.Entity<Employee>(entity =>
{
    entity.Property(e => e.FullName)
        .HasWitComputedColumnSql("FirstName || ' ' || LastName", stored: true);
});
```

### Concurrency Tokens

```csharp
public class Document
{
    public int Id { get; set; }
    public string Content { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

// In OnModelCreating
modelBuilder.Entity<Document>(entity =>
{
    entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
});
```

### JSON Columns

```csharp
using OutWit.Database.EntityFramework.Query.Translators;

public class Profile
{
    public int Id { get; set; }
    public string Settings { get; set; }  // JSON column
}

// In OnModelCreating
modelBuilder.Entity<Profile>(entity =>
{
    entity.Property(e => e.Settings).HasJsonColumnType();
});

// Query JSON data using extension methods
var profiles = context.Profiles
    .Where(p => p.Settings.JsonValue("$.theme") == "dark")
    .ToList();

// Available JSON extension methods:
// - JsonValue(path)     - Extract scalar value
// - JsonQuery(path)     - Extract JSON fragment
// - JsonContains(value) - Check if JSON contains value
// - JsonLength()        - Get array length
// - JsonType()          - Get JSON value type
// - JsonValid()         - Validate JSON string
```

### Enum to String Conversion

```csharp
public enum Status { Active, Inactive, Pending }

public class Task
{
    public int Id { get; set; }
    public Status Status { get; set; }
}

// In OnModelCreating - store enum as TEXT instead of INT
modelBuilder.Entity<Task>(entity =>
{
    entity.Property(e => e.Status).HasEnumToStringConversion();
});
```

## Bulk Operations

High-performance bulk operations are available via extension methods:

```csharp
using OutWit.Database.EntityFramework.Extensions;

// Bulk Insert - 3x faster than AddRange + SaveChanges
var users = Enumerable.Range(1, 10000)
    .Select(i => new User { Name = $"User{i}", Email = $"user{i}@test.com" });
int inserted = await context.BulkInsertAsync(users);

// Bulk Update
var usersToUpdate = context.Users.AsNoTracking().Take(1000).ToList();
foreach (var user in usersToUpdate)
    user.Status = "Active";
int updated = await context.BulkUpdateAsync(usersToUpdate);

// Bulk Delete
var usersToDelete = context.Users.Where(u => u.Status == "Inactive").ToList();
int deleted = await context.BulkDeleteAsync(usersToDelete);

// Bulk InsertOrUpdate (Upsert)
int affected = await context.BulkInsertOrUpdateAsync(mixedUsers);
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
| JSON | `JSON` | Use `HasJsonColumnType()` |

## LINQ Method Translations

The provider translates common LINQ methods to WitSQL:

### String Methods
- `ToUpper()`, `ToLower()`, `Trim()`, `TrimStart()`, `TrimEnd()`
- `Substring()`, `Replace()`, `Contains()`, `StartsWith()`, `EndsWith()`
- `IndexOf()`, `Length`, `Concat()`, `IsNullOrEmpty()`, `IsNullOrWhiteSpace()`

### Math Methods
- `Abs()`, `Ceiling()`, `Floor()`, `Round()`, `Truncate()`
- `Pow()`, `Sqrt()`, `Log()`, `Log10()`, `Exp()`
- `Sin()`, `Cos()`, `Tan()`, `Asin()`, `Acos()`, `Atan()`, `Atan2()`
- `Max()`, `Min()`, `Sign()`

### DateTime Methods
- `AddDays()`, `AddMonths()`, `AddYears()`
- `AddHours()`, `AddMinutes()`, `AddSeconds()`, `AddMilliseconds()`
- `Year`, `Month`, `Day`, `Hour`, `Minute`, `Second`
- `Date`, `TimeOfDay`, `DayOfWeek`, `DayOfYear`
- `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`

### JSON Methods
- `JsonValue()`, `JsonQuery()`, `JsonContains()`
- `JsonLength()`, `JsonType()`, `JsonValid()`

### Other
- `Guid.NewGuid()`

## Connection String Options

All connection string options from `OutWit.Database.AdoNet` are supported:

| Option | Description | Example |
|--------|-------------|---------|
| `Data Source` | Database file path or `:memory:` | `Data Source=mydb.witdb` |
| `Mode` | Connection mode | `Mode=ReadOnly` |
| `Encryption` | Encryption algorithm | `Encryption=aes-gcm` |
| `Password` | Encryption password | `Password=secret` |
| `Store` | Storage engine | `Store=btree` or `Store=lsm` |

## Requirements

- .NET 9.0 or .NET 10.0
- Microsoft.EntityFrameworkCore.Relational 9.0+ or 10.0+
- OutWit.Database.AdoNet

## Related Packages

- `OutWit.Database.Core` - Core database engine
- `OutWit.Database.AdoNet` - ADO.NET provider

## License

MIT License - see LICENSE file for details.

## See Also

- [ROADMAP.md](ROADMAP.md) - Version 2.0 planned features
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
