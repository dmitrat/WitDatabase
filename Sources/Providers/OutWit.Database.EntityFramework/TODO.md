# OutWit.Database.EntityFramework - Implementation TODO

**Version:** 1.0  
**Last Updated:** 2025-02-05

---

## Overview

This package provides an Entity Framework Core provider for WitDatabase, enabling full ORM support including migrations, LINQ queries, change tracking, and all standard EF Core features.

**Target:** Full EF Core 9.0/10.0 compatibility with support for all standard features.

**Prerequisite:** `OutWit.Database.AdoNet` must be completed first.

---

## Implementation Progress

### Completed Phases

- [x] **Phase 1:** Core Provider Infrastructure (P0) - COMPLETED
- [x] **Phase 2:** Database Provider (P0) - COMPLETED  
- [x] **Phase 3:** SQL Generation (P0) - COMPLETED (basic)
- [x] **Phase 4:** Type Mapping (P0) - COMPLETED
- [x] **Phase 5:** Model Building (P0) - COMPLETED (basic)
- [x] **Phase 6:** Update Pipeline (P0) - COMPLETED (basic)
- [x] **Phase 7:** Migrations (P1) - COMPLETED
- [x] **Phase 8:** Database Creation (P1) - COMPLETED
- [x] **Phase 9:** Function Translations (P1) - COMPLETED
- [x] **Phase 10:** Advanced Features (P2) - COMPLETED (basic)

### Pending Features

- [ ] JSON column support (ToJson/FromJson)
- [ ] Full integration tests with real database

### Current Test Status

- **252 tests passing** (net9.0 and net10.0)
- **0 tests skipped**

---

## Implementation Plan

### Phase 1: Core Provider Infrastructure (P0) - COMPLETED

#### 1.1 WitDbContextOptionsExtension

Implemented in: `Infrastructure/WitDbContextOptionsExtension.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Info` property | Done | Extension info for logging |
| `ApplyServices()` | Done | Register provider services |
| `Validate()` | Done | Validate options |
| `ConnectionString` property | Done | Database connection string |
| `Connection` property | Done | Existing connection |
| `InMemory` property | Done | In-memory mode |

#### 1.2 WitDbContextOptionsBuilder

Implemented in: `Extensions/WitDbContextOptionsBuilderExtensions.cs`

| Method | Status | Description |
|--------|--------|-------------|
| `UseWitDb(connectionString)` | Done | Configure with connection string |
| `UseWitDb(connection)` | Done | Configure with existing connection |
| `UseWitDbInMemory()` | Done | Configure for in-memory |
| `EnableSensitiveDataLogging()` | Done | Log parameter values |
| `UseQuerySplittingBehavior()` | Done | Split/single query mode |

---

### Phase 2: Database Provider (P0) - COMPLETED

#### 2.1 WitDatabaseProvider

Implemented in: `Infrastructure/WitDatabaseProvider.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Name` property | Done | Provider name ("OutWit.Database.EntityFramework") |
| `IsConfigured()` | Done | Check if provider is configured |

#### 2.2 WitRelationalConnection

Implemented in: `Storage/WitRelationalConnection.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `CreateDbConnection()` | Done | Create WitDbConnection |
| `ConnectionString` property | Done | Connection string |

---

### Phase 3: SQL Generation (P0) - COMPLETED (basic)

#### 3.1 WitSqlGenerationHelper

Implemented in: `Storage/WitSqlGenerationHelper.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `DelimitIdentifier()` | Done | Quote identifiers with `"` |
| `EscapeIdentifier()` | Done | Escape special characters |
| `GenerateParameterName()` | Done | Generate @param names |
| `GenerateParameterNamePlaceholder()` | Done | Generate @param placeholders |
| `StatementTerminator` property | Done | Return `;` |
| `BatchTerminator` property | Done | Return empty (no GO) |

#### 3.2 WitQuerySqlGenerator

Implemented in: `Query/WitQuerySqlGenerator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `VisitSqlBinary()` | Done | Generate binary expressions (string concatenation with `\|\|`) |
| `GenerateLimitOffset()` | Done | Generate LIMIT/OFFSET |
| `GenerateTop()` | Done | Empty (WitDB doesn't use TOP) |

#### 3.3 WitQuerySqlGeneratorFactory

Implemented in: `Query/WitQuerySqlGeneratorFactory.cs`

---

### Phase 4: Type Mapping (P0) - COMPLETED

#### 4.1 WitTypeMappingSource

Implemented in: `Storage/WitTypeMappingSource.cs`

| CLR Type | WitSQL Type | Status |
|----------|-------------|--------|
| `bool` | `BOOLEAN` | Done |
| `byte` | `UTINYINT` | Done |
| `sbyte` | `TINYINT` | Done |
| `short` | `SMALLINT` | Done |
| `ushort` | `USMALLINT` | Done |
| `int` | `INT` | Done |
| `uint` | `UINT` | Done |
| `long` | `BIGINT` | Done |
| `ulong` | `UBIGINT` | Done |
| `float` | `FLOAT` | Done |
| `double` | `DOUBLE` | Done |
| `decimal` | `DECIMAL` | Done |
| `string` | `TEXT` | Done |
| `byte[]` | `BLOB` | Done |
| `DateTime` | `DATETIME` | Done |
| `DateTimeOffset` | `DATETIMEOFFSET` | Done |
| `DateOnly` | `DATE` | Done |
| `TimeOnly` | `TIME` | Done |
| `TimeSpan` | `INTERVAL` | Done |
| `Guid` | `GUID` | Done |
| `Enum` | `INT` | Done |

---

### Phase 5: Model Building (P0) - COMPLETED (basic)

#### 5.1 WitModelValidator

Implemented in: `Metadata/WitModelValidator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `ValidateModel()` | Done | Validate model against WitDB constraints |
| `ValidateNoSchemas()` | Done | WitDB doesn't support schemas |

#### 5.2 WitAnnotationProvider

Implemented in: `Metadata/WitAnnotationProvider.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `For(IColumn)` | Done | Column annotations (autoincrement) |

---

### Phase 6: Update Pipeline (P0) - COMPLETED (basic)

#### 6.1 WitUpdateSqlGenerator

Implemented in: `Update/WitUpdateSqlGenerator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `AppendValues()` | Done | Handle DEFAULT VALUES |
| `GenerateNextSequenceValueOperation()` | Done | Generate INCREMENT() |

#### 6.2 WitModificationCommandBatchFactory

Implemented in: `Update/WitModificationCommandBatchFactory.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Create()` | Done | Create modification batch |

---

### Phase 7: Migrations (P1) - COMPLETED

#### 7.1 WitMigrationsSqlGenerator

Implemented in: `Migrations/WitMigrationsSqlGenerator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Generate(CreateTableOperation)` | Done | CREATE TABLE |
| `Generate(DropTableOperation)` | Done | DROP TABLE IF EXISTS |
| `Generate(RenameTableOperation)` | Done | ALTER TABLE RENAME TO |
| `Generate(AddColumnOperation)` | Done | ALTER TABLE ADD COLUMN |
| `Generate(DropColumnOperation)` | Done | ALTER TABLE DROP COLUMN |
| `Generate(AlterColumnOperation)` | Done | ALTER COLUMN SET/DROP |
| `Generate(RenameColumnOperation)` | Done | ALTER TABLE RENAME COLUMN |
| `Generate(CreateIndexOperation)` | Done | CREATE INDEX IF NOT EXISTS |
| `Generate(DropIndexOperation)` | Done | DROP INDEX IF EXISTS |
| `Generate(AddForeignKeyOperation)` | Done | Comment (limited support) |
| `Generate(DropForeignKeyOperation)` | Done | Comment (limited support) |
| `Generate(AddPrimaryKeyOperation)` | Done | Comment (limited support) |
| `Generate(DropPrimaryKeyOperation)` | Done | Comment (limited support) |
| `Generate(AddUniqueConstraintOperation)` | Done | Create unique index |
| `Generate(DropUniqueConstraintOperation)` | Done | Drop index |
| `Generate(AddCheckConstraintOperation)` | Done | Comment (future support) |
| `Generate(DropCheckConstraintOperation)` | Done | Comment (future support) |
| `Generate(CreateSequenceOperation)` | Done | CREATE SEQUENCE |
| `Generate(DropSequenceOperation)` | Done | DROP SEQUENCE |
| `Generate(AlterSequenceOperation)` | Done | ALTER SEQUENCE RESTART |
| `Generate(SqlOperation)` | Done | Raw SQL |
| `ColumnDefinition()` | Done | Column definition with types |

#### 7.2 WitHistoryRepository

Implemented in: `Migrations/WitHistoryRepository.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `ExistsSql` property | Done | SELECT from INFORMATION_SCHEMA |
| `GetCreateScript()` | Done | CREATE TABLE IF NOT EXISTS |
| `GetCreateIfNotExistsScript()` | Done | Same as GetCreateScript |
| `GetBeginIfNotExistsScript()` | Done | Empty (not supported) |
| `GetBeginIfExistsScript()` | Done | Empty (not supported) |
| `GetEndIfScript()` | Done | Empty (not supported) |
| `GetInsertScript()` | Done | INSERT INTO __EFMigrationsHistory |
| `GetDeleteScript()` | Done | DELETE FROM __EFMigrationsHistory |
| `AcquireDatabaseLock()` | Done | No-op lock (single-user) |
| `AcquireDatabaseLockAsync()` | Done | No-op lock (single-user) |
| `InterpretExistsResult()` | Done | Check for non-null result |
| `LockReleaseBehavior` property | Done | Explicit release |
| `ConfigureTable()` | Done | Configure column types |

---

### Phase 8: Database Creation (P1) - COMPLETED

#### 8.1 WitDatabaseCreator

Implemented in: `Storage/WitDatabaseCreator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Exists()` | Done | Check if database file exists |
| `ExistsAsync()` | Done | Async version |
| `HasTables()` | Done | Query INFORMATION_SCHEMA.TABLES |
| `HasTablesAsync()` | Done | Async version |
| `Create()` | Done | Open/close connection creates file |
| `CreateAsync()` | Done | Async version |
| `Delete()` | Done | Delete database file |
| `DeleteAsync()` | Done | Async version |

Note: `EnsureCreated()`, `EnsureDeleted()`, `CreateTables()`, `CanConnect()` are provided by base class `RelationalDatabaseCreator`.

---

### Phase 9: Function Translations (P1) - COMPLETED

#### 9.1 WitStringMethodTranslator

Implemented in: `Query/Translators/WitStringMethodTranslator.cs`

| C# Method | WitSQL Function | Status |
|-----------|-----------------|--------|
| `string.ToUpper()` | `UPPER()` | Done |
| `string.ToLower()` | `LOWER()` | Done |
| `string.Trim()` | `TRIM()` | Done |
| `string.TrimStart()` | `LTRIM()` | Done |
| `string.TrimEnd()` | `RTRIM()` | Done |
| `string.Substring()` | `SUBSTR()` | Done |
| `string.Replace()` | `REPLACE()` | Done |
| `string.Contains()` | `INSTR() > 0` | Done |
| `string.StartsWith()` | `LIKE 'x%'` | Done |
| `string.EndsWith()` | `LIKE '%x'` | Done |
| `string.IndexOf()` | `INSTR() - 1` | Done |
| `string.Concat()` | `\|\|` | Done |
| `string.IsNullOrEmpty()` | `IS NULL OR = ''` | Done |
| `string.IsNullOrWhiteSpace()` | `IS NULL OR TRIM() = ''` | Done |

#### 9.2 WitMathMethodTranslator

Implemented in: `Query/Translators/WitMathMethodTranslator.cs`

| C# Method | WitSQL Function | Status |
|-----------|-----------------|--------|
| `Math.Abs()` | `ABS()` | Done |
| `Math.Ceiling()` | `CEIL()` | Done |
| `Math.Floor()` | `FLOOR()` | Done |
| `Math.Round()` | `ROUND()` | Done |
| `Math.Truncate()` | `TRUNC()` | Done |
| `Math.Pow()` | `POWER()` | Done |
| `Math.Sqrt()` | `SQRT()` | Done |
| `Math.Log()` | `LN()` | Done |
| `Math.Log10()` | `LOG10()` | Done |
| `Math.Exp()` | `EXP()` | Done |
| `Math.Sin/Cos/Tan()` | `SIN/COS/TAN()` | Done |
| `Math.Asin/Acos/Atan()` | `ASIN/ACOS/ATAN()` | Done |
| `Math.Atan2()` | `ATAN2()` | Done |
| `Math.Max()` | `MAX()` | Done |
| `Math.Min()` | `MIN()` | Done |
| `Math.Sign()` | `SIGN()` | Done |

#### 9.3 WitDateTimeMethodTranslator

Implemented in: `Query/Translators/WitDateTimeMethodTranslator.cs`

| C# Method | WitSQL Function | Status |
|-----------|-----------------|--------|
| `DateTime.AddDays()` | `DATEADD('day', ...)` | Done |
| `DateTime.AddMonths()` | `DATEADD('month', ...)` | Done |
| `DateTime.AddYears()` | `DATEADD('year', ...)` | Done |
| `DateTime.AddHours()` | `DATEADD('hour', ...)` | Done |
| `DateTime.AddMinutes()` | `DATEADD('minute', ...)` | Done |
| `DateTime.AddSeconds()` | `DATEADD('second', ...)` | Done |
| `DateTime.AddMilliseconds()` | `DATEADD('millisecond', ...)` | Done |

#### 9.4 WitGuidMethodTranslator

Implemented in: `Query/Translators/WitGuidMethodTranslator.cs`

| C# Method | WitSQL Function | Status |
|-----------|-----------------|--------|
| `Guid.NewGuid()` | `NEWGUID()` | Done |

#### 9.5 WitMemberTranslator

Implemented in: `Query/Translators/WitMemberTranslator.cs`

| C# Member | WitSQL Translation | Status |
|-----------|-------------------|--------|
| `string.Length` | `LENGTH()` | Done |
| `DateTime.Year` | `YEAR()` | Done |
| `DateTime.Month` | `MONTH()` | Done |
| `DateTime.Day` | `DAY()` | Done |
| `DateTime.Hour` | `HOUR()` | Done |
| `DateTime.Minute` | `MINUTE()` | Done |
| `DateTime.Second` | `SECOND()` | Done |
| `DateTime.Millisecond` | `MILLISECOND()` | Done |
| `DateTime.DayOfWeek` | `DAYOFWEEK()` | Done |
| `DateTime.DayOfYear` | `DAYOFYEAR()` | Done |
| `DateTime.Date` | `DATE()` | Done |
| `DateTime.TimeOfDay` | `TIME()` | Done |
| `DateTime.Now` | `NOW()` | Done |
| `DateTime.UtcNow` | `NOW()` | Done |
| `DateTime.Today` | `DATE(NOW())` | Done |
| `DateOnly.*` | Extract functions | Done |
| `TimeOnly.*` | Extract functions | Done |
| `TimeSpan.*` | Extract/calculations | Done |

#### 9.6 Provider Registration

Implemented in: `Query/WitMethodCallTranslatorProvider.cs` and `Query/WitMemberTranslatorProvider.cs`

---

### Phase 10: Advanced Features (P2) - COMPLETED (basic)

#### 10.1 Computed Columns

Implemented in: `Extensions/WitPropertyBuilderExtensions.cs` and `Migrations/WitMigrationsSqlGenerator.cs`

| Feature | Status | Description |
|---------|--------|-------------|
| `HasWitComputedColumnSql()` | Done | Define computed column with SQL expression |
| `IsStored` | Done | STORED vs VIRTUAL computed columns |
| Migration support | Done | GENERATED ALWAYS AS syntax |

#### 10.2 Concurrency

Implemented in: `Extensions/WitPropertyBuilderExtensions.cs` and `Update/WitUpdateSqlGenerator.cs`

| Feature | Status | Description |
|---------|--------|-------------|
| `IsWitRowVersion()` | Done | Integer-based row version |
| `IsConcurrencyToken()` | Done | Standard EF Core concurrency |
| WHERE clause support | Done | Original value comparison in updates |

#### 10.3 Value Converters

| Feature | Status | Description |
|---------|--------|-------------|
| Enum to int | Done | Store enum as INT (default) |
| Enum to string | Pending | Store enum as TEXT |
| JSON serialization | Pending | Complex types as JSON |

#### 10.4 JSON Support (Future)

| Feature | Priority | Description |
|---------|----------|-------------|
| `ToJson()` | P2 | Map entity to JSON column |
| `FromJson()` | P2 | Map JSON column to entity |
| JSON path queries | P2 | `JSON_VALUE`, `JSON_QUERY` |
| JSON updates | P2 | `JSON_SET`, `JSON_REMOVE` |

---

## Service Registration

Current registered services in `WitDbServiceCollectionExtensions.cs`:

```csharp
builder
    // Core services
    .TryAdd<LoggingDefinitions, WitLoggingDefinitions>()
    .TryAdd<IDatabaseProvider, WitDatabaseProvider>()
    
    // Connection and type mapping
    .TryAdd<IRelationalTypeMappingSource, WitTypeMappingSource>()
    .TryAdd<ISqlGenerationHelper, WitSqlGenerationHelper>()
    .TryAdd<IRelationalConnection, WitRelationalConnection>()
    
    // Query generation
    .TryAdd<IQuerySqlGeneratorFactory, WitQuerySqlGeneratorFactory>()
    .TryAdd<IMethodCallTranslatorProvider, WitMethodCallTranslatorProvider>()
    .TryAdd<IMemberTranslatorProvider, WitMemberTranslatorProvider>()
    
    // Update pipeline
    .TryAdd<IUpdateSqlGenerator, WitUpdateSqlGenerator>()
    .TryAdd<IModificationCommandBatchFactory, WitModificationCommandBatchFactory>()
    
    // Model building
    .TryAdd<IRelationalAnnotationProvider, WitAnnotationProvider>()
    .TryAdd<IModelValidator, WitModelValidator>()
    
    // Migrations
    .TryAdd<IMigrationsSqlGenerator, WitMigrationsSqlGenerator>()
    .TryAdd<IHistoryRepository, WitHistoryRepository>()
    
    // Database creation
    .TryAdd<IRelationalDatabaseCreator, WitDatabaseCreator>()
```

---

## Current File Structure

```
OutWit.Database.EntityFramework/
+-- Diagnostics/
|   +-- WitLoggingDefinitions.cs          [Done]
+-- Extensions/
|   +-- WitDbContextOptionsBuilderExtensions.cs  [Done]
|   +-- WitDbServiceCollectionExtensions.cs      [Done]
|   +-- WitPropertyBuilderExtensions.cs          [Done]
+-- Infrastructure/
|   +-- WitDbContextOptionsExtension.cs   [Done]
|   +-- WitDbContextOptionsBuilder.cs     [Done]
|   +-- WitDatabaseProvider.cs            [Done]
+-- Metadata/
|   +-- WitAnnotationProvider.cs          [Done]
|   +-- WitModelValidator.cs              [Done]
+-- Migrations/
|   +-- WitHistoryRepository.cs           [Done]
|   +-- WitMigrationsSqlGenerator.cs      [Done]
+-- Query/
|   +-- WitQuerySqlGenerator.cs           [Done]
|   +-- WitQuerySqlGeneratorFactory.cs    [Done]
|   +-- WitMethodCallTranslatorProvider.cs [Done]
|   +-- WitMemberTranslatorProvider.cs    [Done]
|   +-- Translators/
|       +-- WitStringMethodTranslator.cs  [Done]
|       +-- WitMathMethodTranslator.cs    [Done]
|       +-- WitDateTimeMethodTranslator.cs [Done]
|       +-- WitGuidMethodTranslator.cs    [Done]
|       +-- WitMemberTranslator.cs        [Done]
+-- Storage/
|   +-- WitDatabaseCreator.cs             [Done]
|   +-- WitRelationalConnection.cs        [Done]
|   +-- WitSqlGenerationHelper.cs         [Done]
|   +-- WitTypeMappingSource.cs           [Done]
+-- Update/
|   +-- WitModificationCommandBatchFactory.cs  [Done]
|   +-- WitUpdateSqlGenerator.cs          [Done]
+-- TODO.md
+-- README.md
```

---

## Multi-targeting Configuration

The project targets both .NET 9 and .NET 10 with appropriate EF Core versions:

```xml
<PropertyGroup>
  <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
</PropertyGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
  <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.6" />
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
  <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0-preview.5.25277.114" />
</ItemGroup>
```

---

## Test Structure

```
OutWit.Database.EntityFramework.Tests/
+-- Extensions/
|   +-- WitDbContextOptionsBuilderExtensionsTests.cs  [Done] (14 tests)
|   +-- WitPropertyBuilderExtensionsTests.cs          [Done] (6 tests)
+-- Infrastructure/
|   +-- WitDbContextOptionsExtensionTests.cs          [Done] (16 tests)
|   +-- WitDatabaseProviderTests.cs                   [Done] (3 tests)
+-- Integration/
|   +-- BasicDbContextTests.cs                        [Done] (10 tests)
+-- Migrations/
|   +-- WitHistoryRepositoryTests.cs                  [Done] (18 tests)
|   +-- WitMigrationsSqlGeneratorTests.cs             [Done] (18 tests)
|   +-- WitMigrationsSqlGeneratorComputedColumnTests.cs [Done] (5 tests)
+-- Query/
|   +-- WitStringMethodTranslatorTests.cs             [Done] (17 tests)
|   +-- WitMathMethodTranslatorTests.cs               [Done] (30 tests)
|   +-- WitDateTimeMethodTranslatorTests.cs           [Done] (7 tests)
|   +-- WitMemberTranslatorTests.cs                   [Done] (39 tests)
|   +-- WitGuidMethodTranslatorTests.cs               [Done] (1 test)
+-- Storage/
|   +-- WitDatabaseCreatorTests.cs                    [Done] (12 tests)
|   +-- WitSqlGenerationHelperTests.cs                [Done] (17 tests)
|   +-- WitTypeMappingSourceTests.cs                  [Done] (37 tests)
+-- Update/
|   +-- WitUpdateSqlGeneratorTests.cs                 [Done] (3 tests)
```

---

## Next Steps

1. **JSON Support** (P2)
   - Add JSON column type mapping
   - Implement JSON method translators
   - Add JSON function support in migrations

---

## Dependencies

| Package | Version (net9.0) | Version (net10.0) | Purpose |
|---------|------------------|-------------------|---------|
| Microsoft.EntityFrameworkCore.Relational | 9.0.6 | 10.0.0-preview.5 | EF Core base |
| OutWit.Database.AdoNet | 1.0.0 | 1.0.0 | ADO.NET provider |

---

## See Also

- [EF Core Provider Documentation](https://docs.microsoft.com/en-us/ef/core/providers/)
- [Writing an EF Core Provider](https://docs.microsoft.com/en-us/ef/core/providers/writing-a-provider)
- [EF Core Source Code](https://github.com/dotnet/efcore)
- [SQLite Provider](https://github.com/dotnet/efcore/tree/main/src/EFCore.Sqlite.Core) - Reference implementation
