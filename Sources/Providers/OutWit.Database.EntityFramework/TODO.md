# OutWit.Database.EntityFramework - Implementation TODO

**Version:** 1.0  
**Last Updated:** 2025-02-05

---

## Overview

This package provides an Entity Framework Core provider for WitDatabase, enabling full ORM support including migrations, LINQ queries, change tracking, and all standard EF Core features.

**Target:** Full EF Core 9.0 compatibility with support for all standard features.

**Prerequisite:** `OutWit.Database.AdoNet` must be completed first.

---

## Implementation Plan

### Phase 1: Core Provider Infrastructure (P0)

#### 1.1 WitDbContextOptionsExtension

```csharp
public class WitDbContextOptionsExtension : IDbContextOptionsExtension
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Info` property | P0 | Extension info for logging |
| `ApplyServices()` | P0 | Register provider services |
| `Validate()` | P0 | Validate options |
| `ConnectionString` property | P0 | Database connection string |
| `Connection` property | P0 | Existing connection |
| `InMemory` property | P1 | In-memory mode |

#### 1.2 WitDbContextOptionsBuilder

Extension methods for `DbContextOptionsBuilder`:

```csharp
public static class WitDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseWitDb(
        this DbContextOptionsBuilder builder,
        string connectionString,
        Action<WitDbContextOptionsBuilder>? optionsAction = null);
        
    public static DbContextOptionsBuilder UseWitDb(
        this DbContextOptionsBuilder builder,
        WitDbConnection connection,
        Action<WitDbContextOptionsBuilder>? optionsAction = null);
}
```

| Method | Priority | Description |
|--------|----------|-------------|
| `UseWitDb(connectionString)` | P0 | Configure with connection string |
| `UseWitDb(connection)` | P0 | Configure with existing connection |
| `UseWitDb(action)` | P0 | Configure with options builder |
| `EnableSensitiveDataLogging()` | P1 | Log parameter values |
| `UseQuerySplittingBehavior()` | P1 | Split/single query mode |

---

### Phase 2: Database Provider (P0)

#### 2.1 WitDatabaseProvider

```csharp
public class WitDatabaseProvider : DatabaseProvider<WitDbContextOptionsExtension>
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Name` property | P0 | Provider name ("OutWit.Database.EntityFramework") |
| `IsConfigured()` | P0 | Check if provider is configured |

#### 2.2 WitRelationalConnection

```csharp
public class WitRelationalConnection : RelationalConnection
```

| Member | Priority | Description |
|--------|----------|-------------|
| `CreateDbConnection()` | P0 | Create WitDbConnection |
| `DbConnection` property | P0 | Get/set connection |
| `ConnectionString` property | P0 | Connection string |
| `Open()` / `OpenAsync()` | P0 | Open connection |
| `Close()` / `CloseAsync()` | P0 | Close connection |
| `BeginTransaction()` | P0 | Start transaction |
| `BeginTransactionAsync()` | P0 | Async version |
| `CurrentTransaction` property | P0 | Active transaction |

---

### Phase 3: SQL Generation (P0)

#### 3.1 WitSqlGenerationHelper

```csharp
public class WitSqlGenerationHelper : RelationalSqlGenerationHelper
```

| Member | Priority | Description |
|--------|----------|-------------|
| `DelimitIdentifier()` | P0 | Quote identifiers with `"` |
| `EscapeIdentifier()` | P0 | Escape special characters |
| `GenerateParameterName()` | P0 | Generate @param names |
| `GenerateParameterNamePlaceholder()` | P0 | Generate @param placeholders |
| `StatementTerminator` property | P0 | Return `;` |
| `BatchTerminator` property | P0 | Return empty (no GO) |

#### 3.2 WitQuerySqlGenerator

```csharp
public class WitQuerySqlGenerator : QuerySqlGenerator
```

| Member | Priority | Description |
|--------|----------|-------------|
| `VisitSelect()` | P0 | Generate SELECT |
| `VisitTable()` | P0 | Generate table reference |
| `VisitColumn()` | P0 | Generate column reference |
| `VisitSqlBinary()` | P0 | Generate binary expressions |
| `VisitSqlUnary()` | P0 | Generate unary expressions |
| `VisitSqlConstant()` | P0 | Generate literals |
| `VisitSqlParameter()` | P0 | Generate parameters |
| `VisitSqlFunction()` | P0 | Generate function calls |
| `VisitLike()` | P0 | Generate LIKE |
| `VisitIn()` | P0 | Generate IN |
| `VisitExists()` | P0 | Generate EXISTS |
| `VisitCase()` | P0 | Generate CASE |
| `VisitOrdering()` | P0 | Generate ORDER BY |
| `VisitLimit()` | P0 | Generate LIMIT/OFFSET |
| `VisitJoin()` | P0 | Generate JOINs |
| `VisitSetOperation()` | P1 | Generate UNION/INTERSECT/EXCEPT |

#### 3.3 WitQueryableMethodTranslatingExpressionVisitor

```csharp
public class WitQueryableMethodTranslatingExpressionVisitor 
    : RelationalQueryableMethodTranslatingExpressionVisitor
```

| Member | Priority | Description |
|--------|----------|-------------|
| `TranslateAggregate()` | P0 | COUNT, SUM, AVG, MIN, MAX |
| `TranslateContains()` | P0 | IN clause |
| `TranslateElementAtOrDefault()` | P0 | LIMIT 1 OFFSET n |
| `TranslateFirstOrDefault()` | P0 | LIMIT 1 |
| `TranslateLastOrDefault()` | P1 | ORDER BY DESC LIMIT 1 |
| `TranslateSingleOrDefault()` | P0 | LIMIT 2 (for validation) |
| `TranslateSkip()` | P0 | OFFSET |
| `TranslateTake()` | P0 | LIMIT |
| `TranslateDistinct()` | P0 | DISTINCT |
| `TranslateOrderBy()` | P0 | ORDER BY |
| `TranslateWhere()` | P0 | WHERE |
| `TranslateGroupBy()` | P0 | GROUP BY |
| `TranslateHaving()` | P0 | HAVING |
| `TranslateJoin()` | P0 | JOIN |
| `TranslateLeftJoin()` | P0 | LEFT JOIN |
| `TranslateSelectMany()` | P0 | CROSS JOIN |

---

### Phase 4: Type Mapping (P0)

#### 4.1 WitTypeMappingSource

```csharp
public class WitTypeMappingSource : RelationalTypeMappingSource
```

| Member | Priority | Description |
|--------|----------|-------------|
| `FindMapping(Type)` | P0 | Map CLR type to WitSQL |
| `FindMapping(string)` | P0 | Map store type to CLR |
| `GetMappingForValue()` | P0 | Map value to type |

#### 4.2 Type Mappings

| CLR Type | WitSQL Type | EF Core Type |
|----------|-------------|--------------|
| `bool` | `BOOLEAN` | `BoolTypeMapping` |
| `byte` | `UTINYINT` | `ByteTypeMapping` |
| `sbyte` | `TINYINT` | `SByteTypeMapping` |
| `short` | `SMALLINT` | `ShortTypeMapping` |
| `ushort` | `USMALLINT` | `UShortTypeMapping` |
| `int` | `INT` | `IntTypeMapping` |
| `uint` | `UINT` | `UIntTypeMapping` |
| `long` | `BIGINT` | `LongTypeMapping` |
| `ulong` | `UBIGINT` | `ULongTypeMapping` |
| `float` | `FLOAT` | `FloatTypeMapping` |
| `double` | `DOUBLE` | `DoubleTypeMapping` |
| `decimal` | `DECIMAL` | `DecimalTypeMapping` |
| `string` | `TEXT` | `StringTypeMapping` |
| `byte[]` | `BLOB` | `ByteArrayTypeMapping` |
| `DateTime` | `DATETIME` | `DateTimeTypeMapping` |
| `DateTimeOffset` | `DATETIMEOFFSET` | `DateTimeOffsetTypeMapping` |
| `DateOnly` | `DATE` | `DateOnlyTypeMapping` |
| `TimeOnly` | `TIME` | `TimeOnlyTypeMapping` |
| `TimeSpan` | `INTERVAL` | `TimeSpanTypeMapping` |
| `Guid` | `GUID` | `GuidTypeMapping` |
| `Enum` | `INT` | `EnumTypeMapping` |
| JSON types | `JSON` | `JsonTypeMapping` |

---

### Phase 5: Model Building (P0)

#### 5.1 WitModelValidator

```csharp
public class WitModelValidator : RelationalModelValidator
```

| Member | Priority | Description |
|--------|----------|-------------|
| `ValidateModel()` | P0 | Validate model against WitDB constraints |
| `ValidateTypeMappings()` | P0 | Validate all types are mappable |
| `ValidateRelationships()` | P0 | Validate FK relationships |

#### 5.2 WitAnnotationProvider

```csharp
public class WitAnnotationProvider : RelationalAnnotationProvider
```

| Member | Priority | Description |
|--------|----------|-------------|
| `For(ITable)` | P0 | Table annotations |
| `For(IColumn)` | P0 | Column annotations |
| `For(IIndex)` | P0 | Index annotations |
| `For(IForeignKey)` | P0 | FK annotations |

---

### Phase 6: Update Pipeline (P0)

#### 6.1 WitUpdateSqlGenerator

```csharp
public class WitUpdateSqlGenerator : UpdateSqlGenerator
```

| Member | Priority | Description |
|--------|----------|-------------|
| `AppendInsertOperation()` | P0 | Generate INSERT |
| `AppendUpdateOperation()` | P0 | Generate UPDATE |
| `AppendDeleteOperation()` | P0 | Generate DELETE |
| `AppendInsertOperationReturning()` | P0 | INSERT ... RETURNING |
| `AppendUpdateOperationReturning()` | P0 | UPDATE ... RETURNING |
| `AppendDeleteOperationReturning()` | P0 | DELETE ... RETURNING |

#### 6.2 WitModificationCommandBatchFactory

```csharp
public class WitModificationCommandBatchFactory : IModificationCommandBatchFactory
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Create()` | P0 | Create modification batch |
| `MaxBatchSize` | P1 | Maximum batch size |

---

### Phase 7: Migrations (P1)

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

### Phase 8: Database Creation (P1)

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

### Phase 9: Function Translations (P1)

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

### Phase 10: Advanced Features (P2)

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

## File Structure

```
OutWit.Database.EntityFramework/
??? Extensions/
?   ??? WitDbContextOptionsBuilderExtensions.cs
?   ??? WitDbServiceCollectionExtensions.cs
??? Infrastructure/
?   ??? WitDbContextOptionsExtension.cs
?   ??? WitDatabaseProvider.cs
?   ??? WitRelationalConnection.cs
??? Query/
?   ??? WitQuerySqlGenerator.cs
?   ??? WitQuerySqlGeneratorFactory.cs
?   ??? WitQueryableMethodTranslatingExpressionVisitor.cs
?   ??? WitQueryCompilationContextFactory.cs
??? Storage/
?   ??? WitTypeMappingSource.cs
?   ??? TypeMappings/
?   ?   ??? WitBoolTypeMapping.cs
?   ?   ??? WitStringTypeMapping.cs
?   ?   ??? WitGuidTypeMapping.cs
?   ?   ??? WitDateTimeTypeMapping.cs
?   ?   ??? ...
?   ??? WitSqlGenerationHelper.cs
??? Update/
?   ??? WitUpdateSqlGenerator.cs
?   ??? WitModificationCommandBatchFactory.cs
??? Migrations/
?   ??? WitMigrationsSqlGenerator.cs
?   ??? WitHistoryRepository.cs
?   ??? WitMigrationsAnnotationProvider.cs
?   ??? WitDatabaseCreator.cs
??? Metadata/
?   ??? WitModelValidator.cs
?   ??? WitAnnotationProvider.cs
??? Query/Internal/
?   ??? WitMethodCallTranslator.cs
?   ??? WitMemberTranslator.cs
?   ??? WitSqlExpressionFactory.cs
??? README.md
```

---

## Test Plan

### Unit Tests

| Category | Tests |
|----------|-------|
| Type mappings | 50+ |
| SQL generation | 60+ |
| Query translation | 80+ |
| Method translations | 50+ |
| Update generation | 30+ |
| Migrations | 40+ |
| Model validation | 20+ |
| **Total** | **330+** |

### Integration Tests

| Scenario | Description |
|----------|-------------|
| Basic CRUD | Add/Query/Update/Remove entities |
| Relationships | 1:1, 1:N, N:M |
| Inheritance | TPH, TPT |
| Complex queries | Joins, subqueries, aggregates |
| Transactions | Commit, rollback |
| Migrations | Up, Down, idempotent |
| Concurrency | Optimistic locking |
| Bulk operations | Large data sets |

### EF Core Test Suite

Run subset of EF Core functional tests to ensure compatibility.

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore.Relational | 9.0.0 | EF Core base |
| OutWit.Database.AdoNet | 1.0.0 | ADO.NET provider |
| OutWit.Database | 1.0.0 | SQL engine |

---

## Implementation Order

1. **Phase 1-2:** Core infrastructure (options, provider, connection)
2. **Phase 3-4:** SQL generation and type mapping
3. **Phase 5-6:** Model building and update pipeline
4. **Phase 7-8:** Migrations and database creation
5. **Phase 9:** Function translations
6. **Phase 10:** Advanced features

---

## See Also

- [EF Core Provider Documentation](https://docs.microsoft.com/en-us/ef/core/providers/)
- [Writing an EF Core Provider](https://docs.microsoft.com/en-us/ef/core/providers/writing-a-provider)
- [EF Core Source Code](https://github.com/dotnet/efcore)
- [SQLite Provider](https://github.com/dotnet/efcore/tree/main/src/EFCore.Sqlite.Core) - Reference implementation
