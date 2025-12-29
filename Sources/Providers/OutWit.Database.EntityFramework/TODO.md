# OutWit.Database.EntityFramework - Implementation TODO

**Version:** 1.0  
**Last Updated:** 2025-02-05

---

## Overview

This package provides an Entity Framework Core provider for WitDatabase, enabling full ORM support including migrations, LINQ queries, change tracking, and all standard EF Core features.

**Target:** Full EF Core 9.0 compatibility with support for all standard features.

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

### Pending Phases

- [ ] **Phase 7:** Migrations (P1)
- [ ] **Phase 8:** Database Creation (P1)
- [ ] **Phase 9:** Function Translations (P1)
- [ ] **Phase 10:** Advanced Features (P2)

### Current Test Status

- **88 tests passing**
- **2 integration tests skipped** (require full provider implementation)

---

## Implementation Plan

### Phase 1: Core Provider Infrastructure (P0) ? COMPLETED

#### 1.1 WitDbContextOptionsExtension ?

Implemented in: `Infrastructure/WitDbContextOptionsExtension.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Info` property | ? | Extension info for logging |
| `ApplyServices()` | ? | Register provider services |
| `Validate()` | ? | Validate options |
| `ConnectionString` property | ? | Database connection string |
| `Connection` property | ? | Existing connection |
| `InMemory` property | ? | In-memory mode |

#### 1.2 WitDbContextOptionsBuilder ?

Implemented in: `Extensions/WitDbContextOptionsBuilderExtensions.cs`

| Method | Status | Description |
|--------|--------|-------------|
| `UseWitDb(connectionString)` | ? | Configure with connection string |
| `UseWitDb(connection)` | ? | Configure with existing connection |
| `UseWitDbInMemory()` | ? | Configure for in-memory |
| `EnableSensitiveDataLogging()` | ? | Log parameter values |
| `UseQuerySplittingBehavior()` | ? | Split/single query mode |

---

### Phase 2: Database Provider (P0) ? COMPLETED

#### 2.1 WitDatabaseProvider ?

Implemented in: `Infrastructure/WitDatabaseProvider.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Name` property | ? | Provider name ("OutWit.Database.EntityFramework") |
| `IsConfigured()` | ? | Check if provider is configured |

#### 2.2 WitRelationalConnection ?

Implemented in: `Storage/WitRelationalConnection.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `CreateDbConnection()` | ? | Create WitDbConnection |
| `ConnectionString` property | ? | Connection string |

---

### Phase 3: SQL Generation (P0) ? COMPLETED (basic)

#### 3.1 WitSqlGenerationHelper ?

Implemented in: `Storage/WitSqlGenerationHelper.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `DelimitIdentifier()` | ? | Quote identifiers with `"` |
| `EscapeIdentifier()` | ? | Escape special characters |
| `GenerateParameterName()` | ? | Generate @param names |
| `GenerateParameterNamePlaceholder()` | ? | Generate @param placeholders |
| `StatementTerminator` property | ? | Return `;` |
| `BatchTerminator` property | ? | Return empty (no GO) |

#### 3.2 WitQuerySqlGenerator ?

Implemented in: `Query/WitQuerySqlGenerator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `VisitSqlBinary()` | ? | Generate binary expressions (string concatenation with `||`) |
| `GenerateLimitOffset()` | ? | Generate LIMIT/OFFSET |
| `GenerateTop()` | ? | Empty (WitDB doesn't use TOP) |

#### 3.3 WitQuerySqlGeneratorFactory ?

Implemented in: `Query/WitQuerySqlGeneratorFactory.cs`

---

### Phase 4: Type Mapping (P0) ? COMPLETED

#### 4.1 WitTypeMappingSource ?

Implemented in: `Storage/WitTypeMappingSource.cs`

| CLR Type | WitSQL Type | Status |
|----------|-------------|--------|
| `bool` | `BOOLEAN` | ? |
| `byte` | `UTINYINT` | ? |
| `sbyte` | `TINYINT` | ? |
| `short` | `SMALLINT` | ? |
| `ushort` | `USMALLINT` | ? |
| `int` | `INT` | ? |
| `uint` | `UINT` | ? |
| `long` | `BIGINT` | ? |
| `ulong` | `UBIGINT` | ? |
| `float` | `FLOAT` | ? |
| `double` | `DOUBLE` | ? |
| `decimal` | `DECIMAL` | ? |
| `string` | `TEXT` | ? |
| `byte[]` | `BLOB` | ? |
| `DateTime` | `DATETIME` | ? |
| `DateTimeOffset` | `DATETIMEOFFSET` | ? |
| `DateOnly` | `DATE` | ? |
| `TimeOnly` | `TIME` | ? |
| `TimeSpan` | `INTERVAL` | ? |
| `Guid` | `GUID` | ? |
| `Enum` | `INT` | ? |

---

### Phase 5: Model Building (P0) ? COMPLETED (basic)

#### 5.1 WitModelValidator ?

Implemented in: `Metadata/WitModelValidator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `ValidateModel()` | ? | Validate model against WitDB constraints |
| `ValidateNoSchemas()` | ? | WitDB doesn't support schemas |

#### 5.2 WitAnnotationProvider ?

Implemented in: `Metadata/WitAnnotationProvider.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `For(IColumn)` | ? | Column annotations (autoincrement) |

---

### Phase 6: Update Pipeline (P0) ? COMPLETED (basic)

#### 6.1 WitUpdateSqlGenerator ?

Implemented in: `Update/WitUpdateSqlGenerator.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `AppendValues()` | ? | Handle DEFAULT VALUES |
| `GenerateNextSequenceValueOperation()` | ? | Generate INCREMENT() |

#### 6.2 WitModificationCommandBatchFactory ?

Implemented in: `Update/WitModificationCommandBatchFactory.cs`

| Member | Status | Description |
|--------|--------|-------------|
| `Create()` | ? | Create modification batch |

---

### Phase 7: Migrations (P1) - NOT STARTED

#### 7.1 WitMigrationsSqlGenerator

```csharp
public class WitMigrationsSqlGenerator : MigrationsSqlGenerator
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Generate(CreateTableOperation)` | P1 | CREATE TABLE |
| `Generate(DropTableOperation)` | P1 | DROP TABLE |
| `Generate(AlterTableOperation)` | P1 | ALTER TABLE |
| `Generate(RenameTableOperation)` | P1 | RENAME TABLE |
| `Generate(AddColumnOperation)` | P1 | ADD COLUMN |
| `Generate(DropColumnOperation)` | P1 | DROP COLUMN |
| `Generate(AlterColumnOperation)` | P1 | ALTER COLUMN |
| `Generate(RenameColumnOperation)` | P1 | RENAME COLUMN |
| `Generate(CreateIndexOperation)` | P1 | CREATE INDEX |
| `Generate(DropIndexOperation)` | P1 | DROP INDEX |
| `Generate(AddForeignKeyOperation)` | P1 | ADD CONSTRAINT FK |
| `Generate(DropForeignKeyOperation)` | P1 | DROP CONSTRAINT |
| `Generate(AddPrimaryKeyOperation)` | P1 | ADD PRIMARY KEY |
| `Generate(DropPrimaryKeyOperation)` | P1 | DROP PRIMARY KEY |
| `Generate(AddUniqueConstraintOperation)` | P1 | ADD UNIQUE |
| `Generate(DropUniqueConstraintOperation)` | P1 | DROP UNIQUE |
| `Generate(AddCheckConstraintOperation)` | P1 | ADD CHECK |
| `Generate(DropCheckConstraintOperation)` | P1 | DROP CHECK |
| `Generate(CreateSequenceOperation)` | P1 | CREATE SEQUENCE |
| `Generate(DropSequenceOperation)` | P1 | DROP SEQUENCE |
| `Generate(SqlOperation)` | P1 | Raw SQL |

#### 7.2 WitHistoryRepository

```csharp
public class WitHistoryRepository : HistoryRepository
```

| Member | Priority | Description |
|--------|----------|-------------|
| `ExistsAsync()` | P1 | Check if __EFMigrationsHistory exists |
| `GetCreateScript()` | P1 | Script to create history table |
| `GetBeginIfExistsScript()` | P2 | Conditional begin |
| `GetEndIfScript()` | P2 | Conditional end |
| `GetAppliedMigrationsAsync()` | P1 | Get applied migrations |
| `GetInsertScript()` | P1 | Insert migration record |
| `GetDeleteScript()` | P1 | Delete migration record |

#### 7.3 WitMigrationsAnnotationProvider

```csharp
public class WitMigrationsAnnotationProvider : MigrationsAnnotationProvider
```

| Member | Priority | Description |
|--------|----------|-------------|
| `For(IModel)` | P1 | Model annotations |
| `For(IEntityType)` | P1 | Entity annotations |
| `For(IProperty)` | P1 | Property annotations |

---

### Phase 8: Database Creation (P1) - NOT STARTED

#### 8.1 WitDatabaseCreator

```csharp
public class WitDatabaseCreator : RelationalDatabaseCreator
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Exists()` | P1 | Check if database exists |
| `ExistsAsync()` | P1 | Async version |
| `HasTables()` | P1 | Check if has any tables |
| `HasTablesAsync()` | P1 | Async version |
| `Create()` | P1 | Create database file |
| `CreateAsync()` | P1 | Async version |
| `CreateTables()` | P1 | Create all tables |
| `CreateTablesAsync()` | P1 | Async version |
| `Delete()` | P1 | Delete database file |
| `DeleteAsync()` | P1 | Async version |
| `EnsureCreated()` | P1 | Create if not exists |
| `EnsureCreatedAsync()` | P1 | Async version |
| `EnsureDeleted()` | P1 | Delete if exists |
| `EnsureDeletedAsync()` | P1 | Async version |
| `CanConnect()` | P1 | Test connection |
| `CanConnectAsync()` | P1 | Async version |

---

### Phase 9: Function Translations (P1) - NOT STARTED

#### 9.1 WitMethodCallTranslator

```csharp
public class WitMethodCallTranslator : IMethodCallTranslator
```

**String Functions:**

| C# Method | WitSQL Function |
|-----------|-----------------|
| `string.Length` | `LENGTH()` |
| `string.ToUpper()` | `UPPER()` |
| `string.ToLower()` | `LOWER()` |
| `string.Trim()` | `TRIM()` |
| `string.TrimStart()` | `LTRIM()` |
| `string.TrimEnd()` | `RTRIM()` |
| `string.Substring()` | `SUBSTR()` |
| `string.Replace()` | `REPLACE()` |
| `string.Contains()` | `INSTR() > 0` |
| `string.StartsWith()` | `LIKE 'x%'` |
| `string.EndsWith()` | `LIKE '%x'` |
| `string.IndexOf()` | `INSTR()` |
| `string.Concat()` | `||` or `CONCAT()` |
| `string.IsNullOrEmpty()` | `IS NULL OR = ''` |
| `string.IsNullOrWhiteSpace()` | `IS NULL OR TRIM() = ''` |

**Math Functions:**

| C# Method | WitSQL Function |
|-----------|-----------------|
| `Math.Abs()` | `ABS()` |
| `Math.Ceiling()` | `CEIL()` |
| `Math.Floor()` | `FLOOR()` |
| `Math.Round()` | `ROUND()` |
| `Math.Truncate()` | `TRUNC()` |
| `Math.Pow()` | `POWER()` |
| `Math.Sqrt()` | `SQRT()` |
| `Math.Log()` | `LOG()` |
| `Math.Log10()` | `LOG10()` |
| `Math.Exp()` | `EXP()` |
| `Math.Sin/Cos/Tan()` | `SIN/COS/TAN()` |
| `Math.Max()` | `MAX()` |
| `Math.Min()` | `MIN()` |

**DateTime Functions:**

| C# Property/Method | WitSQL Function |
|--------------------|-----------------|
| `DateTime.Now` | `NOW()` |
| `DateTime.UtcNow` | `NOW()` |
| `DateTime.Today` | `DATE(NOW())` |
| `DateTime.Year` | `YEAR()` |
| `DateTime.Month` | `MONTH()` |
| `DateTime.Day` | `DAY()` |
| `DateTime.Hour` | `HOUR()` |
| `DateTime.Minute` | `MINUTE()` |
| `DateTime.Second` | `SECOND()` |
| `DateTime.Date` | `DATE()` |
| `DateTime.TimeOfDay` | `TIME()` |
| `DateTime.AddDays()` | `DATEADD('day', ...)` |
| `DateTime.AddMonths()` | `DATEADD('month', ...)` |
| `DateTime.AddYears()` | `DATEADD('year', ...)` |

**GUID Functions:**

| C# Method | WitSQL Function |
|-----------|-----------------|
| `Guid.NewGuid()` | `NEWGUID()` |

**Null Functions:**

| C# Expression | WitSQL Function |
|---------------|-----------------|
| `??` | `COALESCE()` |
| `EF.Functions.NullIf()` | `NULLIF()` |

#### 9.2 WitMemberTranslator

```csharp
public class WitMemberTranslator : IMemberTranslator
```

| C# Member | Translation |
|-----------|-------------|
| `string.Length` | `LENGTH(column)` |
| `DateTime.Year/Month/Day/etc.` | Extract functions |
| `TimeSpan.TotalDays/etc.` | Interval calculations |

---

### Phase 10: Advanced Features (P2) - NOT STARTED

#### 10.1 JSON Support

| Feature | Priority | Description |
|---------|----------|-------------|
| `ToJson()` | P2 | Map entity to JSON column |
| `FromJson()` | P2 | Map JSON column to entity |
| JSON path queries | P2 | `JSON_VALUE`, `JSON_QUERY` |
| JSON updates | P2 | `JSON_SET`, `JSON_REMOVE` |

#### 10.2 Computed Columns

| Feature | Priority | Description |
|---------|----------|-------------|
| `HasComputedColumnSql()` | P2 | Define computed column |
| `IsStored()` | P2 | STORED vs VIRTUAL |

#### 10.3 Value Converters

| Feature | Priority | Description |
|---------|----------|-------------|
| Enum to string | P2 | Store enum as TEXT |
| Enum to int | P0 | Store enum as INT |
| JSON serialization | P2 | Complex types as JSON |

#### 10.4 Concurrency

| Feature | Priority | Description |
|---------|----------|-------------|
| `IsRowVersion()` | P1 | ROWVERSION columns |
| `IsConcurrencyToken()` | P1 | Optimistic concurrency |

---

## Service Registration

```csharp
public class WitDbServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkWitDb(
        this IServiceCollection services)
    {
        var builder = new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IDatabaseProvider, WitDatabaseProvider>()
            .TryAdd<IRelationalConnection, WitRelationalConnection>()
            .TryAdd<ISqlGenerationHelper, WitSqlGenerationHelper>()
            .TryAdd<IQuerySqlGeneratorFactory, WitQuerySqlGeneratorFactory>()
            .TryAdd<ITypeMappingSource, WitTypeMappingSource>()
            .TryAdd<IRelationalTypeMappingSource, WitTypeMappingSource>()
            .TryAdd<IModelValidator, WitModelValidator>()
            .TryAdd<IUpdateSqlGenerator, WitUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, WitModificationCommandBatchFactory>()
            .TryAdd<IMigrationsSqlGenerator, WitMigrationsSqlGenerator>()
            .TryAdd<IHistoryRepository, WitHistoryRepository>()
            .TryAdd<IRelationalDatabaseCreator, WitDatabaseCreator>()
            .TryAdd<IMethodCallTranslator, WitMethodCallTranslator>()
            .TryAdd<IMemberTranslator, WitMemberTranslator>()
            // ... more services
            .TryAddCoreServices();
            
        return services;
    }
}
```

---

## Current File Structure

```
OutWit.Database.EntityFramework/
??? Diagnostics/
?   ??? WitLoggingDefinitions.cs          ?
??? Extensions/
?   ??? WitDbContextOptionsBuilderExtensions.cs  ?
?   ??? WitDbServiceCollectionExtensions.cs      ?
??? Infrastructure/
?   ??? WitDbContextOptionsExtension.cs   ?
?   ??? WitDbContextOptionsBuilder.cs     ?
?   ??? WitDatabaseProvider.cs            ?
??? Metadata/
?   ??? WitAnnotationProvider.cs          ?
?   ??? WitModelValidator.cs              ?
??? Query/
?   ??? WitQuerySqlGenerator.cs           ?
?   ??? WitQuerySqlGeneratorFactory.cs    ?
??? Storage/
?   ??? WitRelationalConnection.cs        ?
?   ??? WitSqlGenerationHelper.cs         ?
?   ??? WitTypeMappingSource.cs           ?
??? Update/
?   ??? WitModificationCommandBatchFactory.cs  ?
?   ??? WitUpdateSqlGenerator.cs          ?
??? TODO.md
```

---

## Test Structure

```
OutWit.Database.EntityFramework.Tests/
??? Extensions/
?   ??? WitDbContextOptionsBuilderExtensionsTests.cs  ? (14 tests)
??? Infrastructure/
?   ??? WitDbContextOptionsExtensionTests.cs          ? (17 tests)
?   ??? WitDatabaseProviderTests.cs                   ? (3 tests)
??? Integration/
?   ??? BasicDbContextTests.cs                        (2 tests skipped)
??? Storage/
    ??? WitSqlGenerationHelperTests.cs                ? (17 tests)
    ??? WitTypeMappingSourceTests.cs                  ? (37 tests)
```

---

## Next Steps

1. **Phase 7:** Implement migrations support
   - WitMigrationsSqlGenerator
   - WitHistoryRepository
   
2. **Phase 8:** Implement database creation
   - WitDatabaseCreator with EnsureCreated/EnsureDeleted

3. **Enable skipped integration tests**
   - Once database creation works, enable BasicDbContextTests

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore.Relational | 9.0.6 | EF Core base |
| OutWit.Database.AdoNet | 1.0.0 | ADO.NET provider |

---

## See Also

- [EF Core Provider Documentation](https://docs.microsoft.com/en-us/ef/core/providers/)
- [Writing an EF Core Provider](https://docs.microsoft.com/en-us/ef/core/providers/writing-a-provider)
- [EF Core Source Code](https://github.com/dotnet/efcore)
- [SQLite Provider](https://github.com/dotnet/efcore/tree/main/src/EFCore.Sqlite.Core) - Reference implementation
