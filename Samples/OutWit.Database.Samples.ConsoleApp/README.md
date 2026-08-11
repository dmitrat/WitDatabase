# OutWit.Database.Samples.ConsoleApp

A console application demonstrating the core features of **WitDatabase** - a pure .NET embedded database engine.

## Overview

This sample showcases various WitDatabase capabilities through interactive examples:

1. **Basic CRUD Operations** - Create, Read, Update, Delete with SQL
2. **Transactions & Savepoints** - ACID transactions with commit, rollback, and savepoints
3. **Encryption** - AES-GCM encryption at rest with password protection
4. **LSM-Tree Storage** - Write-optimized storage engine for logging and time-series
5. **Bulk Operations** - High-performance batch inserts, updates, and deletes

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Windows, Linux, or macOS

### Step 1: Clone the Repository

```bash
git clone https://github.com/dmitrat/WitDatabase.git
cd WitDatabase
```

### Step 2: Navigate to the Sample

```bash
cd Samples/OutWit.Database.Samples.ConsoleApp
```

### Step 3: Build the Project

```bash
dotnet build
```

### Step 4: Run the Application

```bash
dotnet run
```

### Step 5: Select an Example

When the application starts, you'll see an interactive menu:

```
Select an example to run:

  1. Basic CRUD Operations
  2. Transactions & Savepoints
  3. Encryption Demo
  4. LSM-Tree Storage
  5. Bulk Operations
  0. Exit

Enter choice (0-5):
```

Enter the number of the example you want to run and press Enter.

## Examples Description

### 1. Basic CRUD Operations

Demonstrates fundamental database operations:
- Creating tables with various data types
- INSERT with RETURNING clause
- SELECT with WHERE, ORDER BY
- UPDATE with parameters
- DELETE operations
- Aggregation queries (COUNT, AVG, SUM, MIN, MAX)

### 2. Transactions & Savepoints

Shows transaction management:
- BEGIN TRANSACTION / COMMIT / ROLLBACK
- SAVEPOINT creation
- ROLLBACK TO SAVEPOINT
- Money transfer example with rollback scenarios

### 3. Encryption Demo

Demonstrates AES-GCM encryption:
- Creating encrypted database with password
- Storing sensitive data
- Reopening with correct/wrong password
- Viewing raw encrypted file content

### 4. LSM-Tree Storage

Shows write-optimized storage engine:
- Configuring LSM-Tree options
- Bulk inserting log entries
- Querying time-series data
- Viewing LSM file structure

### 5. Bulk Operations

Demonstrates high-performance batch operations:
- Bulk INSERT in transaction
- Creating indexes for performance
- Aggregation queries
- Bulk UPDATE and DELETE

## Code Examples

### Basic CRUD

```csharp
using var engine = new WitSqlEngine(database, ownsStore: true);

// Create table
engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

// Insert with RETURNING
var result = engine.Execute("INSERT INTO Users (Name) VALUES ('John') RETURNING Id, Name");

// Query with parameters
var users = engine.Execute("SELECT * FROM Users WHERE Name = @name", 
    new Dictionary<string, object?> { { "@name", "John" } });

// Update
engine.ExecuteNonQuery("UPDATE Users SET Name = @name WHERE Id = @id", params);

// Delete
engine.ExecuteNonQuery("DELETE FROM Users WHERE Id = @id", params);
```

### Transactions

```csharp
engine.Execute("BEGIN TRANSACTION");

engine.Execute("UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1");
engine.Execute("UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2");

// Savepoint for partial rollback
engine.Execute("SAVEPOINT before_third");
engine.Execute("UPDATE Accounts SET Balance = Balance + 50 WHERE Id = 3");

// Rollback to savepoint if needed
engine.Execute("ROLLBACK TO SAVEPOINT before_third");

engine.Execute("COMMIT");
```

### Encryption

```csharp
var database = new WitDatabaseBuilder()
    .WithFilePath("secure.db")
    .WithBTree()
    .WithEncryption("MySecurePassword123!")
    .WithTransactions()
    .Build();
```

### LSM-Tree (Write-Optimized)

```csharp
var database = new WitDatabaseBuilder()
    .WithLsmTree("./data", opts =>
    {
        opts.EnableWal = true;
        opts.EnableBlockCache = true;
        opts.BlockCacheSizeBytes = 64 * 1024 * 1024; // 64MB
        opts.MemTableSizeLimit = 4 * 1024 * 1024;    // 4MB
        opts.BackgroundCompaction = true;
    })
    .WithTransactions()
    .Build();
```

### Bulk Operations

```csharp
engine.Execute("BEGIN TRANSACTION");

for (int i = 0; i < 10000; i++)
{
    engine.Execute("INSERT INTO Products (SKU, Name, Price) VALUES (@sku, @name, @price)",
        new Dictionary<string, object?>
        {
            { "@sku", $"SKU-{i:D6}" },
            { "@name", $"Product {i}" },
            { "@price", 10.00m + i * 0.10m }
        });
}

engine.Execute("COMMIT");
```

## SQL Features Supported

- **DDL**: CREATE TABLE, DROP TABLE, ALTER TABLE, CREATE INDEX, CREATE VIEW
- **DML**: SELECT, INSERT, UPDATE, DELETE, MERGE
- **Transactions**: BEGIN, COMMIT, ROLLBACK, SAVEPOINT
- **Queries**: JOINs, GROUP BY, HAVING, ORDER BY, LIMIT, OFFSET
- **Functions**: 60+ built-in functions (string, numeric, date/time, JSON)
- **Window Functions**: ROW_NUMBER, RANK, LAG, LEAD, etc.
- **CTEs**: WITH clause (recursive and non-recursive)

## Project Structure

```
OutWit.Database.Samples.ConsoleApp/
+-- Program.cs                      # Main entry point with menu
+-- Examples/
|   +-- BasicCrudExample.cs         # CRUD operations demo
|   +-- TransactionExample.cs       # Transaction & savepoint demo
|   +-- EncryptionExample.cs        # Encryption demo
|   +-- LsmTreeExample.cs           # LSM-Tree storage demo
|   +-- BulkOperationsExample.cs    # Bulk operations demo
+-- README.md
+-- OutWit.Database.Samples.ConsoleApp.csproj
```

## Troubleshooting

### Build Errors

If you encounter build errors, ensure you have the correct .NET SDK installed:

```bash
dotnet --version
```

### Database File Locked

If you see "file is locked" errors, ensure no other process is using the database file. Close any previous instances of the application.

### Encryption Errors

If you see decryption errors when reopening an encrypted database, ensure you're using the exact same password.

## Related Projects

- [OutWit.Database](../../Sources/Engine/OutWit.Database/) - SQL execution engine
- [OutWit.Database.Core](../../Sources/Core/OutWit.Database.Core/) - Storage engine
- [OutWit.Database.AdoNet](../../Sources/Providers/OutWit.Database.AdoNet/) - ADO.NET provider
- [OutWit.Database.EntityFramework](../../Sources/Providers/OutWit.Database.EntityFramework/) - EF Core provider
