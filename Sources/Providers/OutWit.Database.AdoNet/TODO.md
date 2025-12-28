# OutWit.Database.AdoNet - Implementation TODO

**Version:** 1.0  
**Last Updated:** 2025-02-05

---

## Overview

This package provides a standard ADO.NET provider for WitDatabase, allowing it to be used with any .NET application that uses `System.Data.Common` abstractions.

**Target:** Full `System.Data.Common` compatibility for seamless integration with existing .NET data access patterns.

---

## Implementation Plan

### Phase 1: Core Classes (P0)

#### 1.1 WitDbConnection

```csharp
public sealed class WitDbConnection : DbConnection
```

| Member | Priority | Description |
|--------|----------|-------------|
| `ConnectionString` property | P0 | Parse and store connection string |
| `Database` property | P0 | Return database name/path |
| `DataSource` property | P0 | Return data source identifier |
| `ServerVersion` property | P0 | Return WitDatabase version |
| `State` property | P0 | Track connection state |
| `Open()` | P0 | Open database connection |
| `OpenAsync()` | P0 | Async version |
| `Close()` | P0 | Close connection |
| `CloseAsync()` | P0 | Async version |
| `ChangeDatabase()` | P1 | Switch database (if supported) |
| `BeginTransaction()` | P0 | Start transaction |
| `BeginTransactionAsync()` | P0 | Async version |
| `CreateCommand()` | P0 | Create WitDbCommand |
| `Dispose()` / `DisposeAsync()` | P0 | Cleanup resources |
| `GetSchema()` | P1 | Return schema information |

**Internal:**
- Hold reference to `WitSqlEngine`
- Manage connection lifecycle
- Track active transactions

#### 1.2 WitDbCommand

```csharp
public sealed class WitDbCommand : DbCommand
```

| Member | Priority | Description |
|--------|----------|-------------|
| `CommandText` property | P0 | SQL statement text |
| `CommandType` property | P0 | Text/StoredProcedure/TableDirect |
| `CommandTimeout` property | P0 | Execution timeout |
| `Connection` property | P0 | Associated connection |
| `Transaction` property | P0 | Associated transaction |
| `Parameters` property | P0 | Parameter collection |
| `ExecuteNonQuery()` | P0 | Execute DDL/DML, return affected rows |
| `ExecuteNonQueryAsync()` | P0 | Async version |
| `ExecuteScalar()` | P0 | Execute and return first column of first row |
| `ExecuteScalarAsync()` | P0 | Async version |
| `ExecuteReader()` | P0 | Execute and return data reader |
| `ExecuteReaderAsync()` | P0 | Async version |
| `Prepare()` | P1 | Prepare statement for execution |
| `PrepareAsync()` | P1 | Async version |
| `Cancel()` | P1 | Cancel executing command |
| `CreateParameter()` | P0 | Create WitDbParameter |
| `Dispose()` / `DisposeAsync()` | P0 | Cleanup resources |

**Internal:**
- Parse SQL and bind parameters
- Execute via `WitSqlEngine`
- Handle timeouts and cancellation

#### 1.3 WitDbDataReader

```csharp
public sealed class WitDbDataReader : DbDataReader
```

| Member | Priority | Description |
|--------|----------|-------------|
| `FieldCount` property | P0 | Number of columns |
| `HasRows` property | P0 | Whether result has rows |
| `IsClosed` property | P0 | Whether reader is closed |
| `RecordsAffected` property | P0 | Affected row count |
| `Depth` property | P0 | Nesting depth (always 0) |
| `Read()` | P0 | Advance to next row |
| `ReadAsync()` | P0 | Async version |
| `NextResult()` | P1 | Advance to next result set |
| `NextResultAsync()` | P1 | Async version |
| `Close()` | P0 | Close reader |
| `CloseAsync()` | P0 | Async version |
| `GetName(int)` | P0 | Get column name |
| `GetOrdinal(string)` | P0 | Get column ordinal |
| `GetDataTypeName(int)` | P0 | Get column type name |
| `GetFieldType(int)` | P0 | Get column CLR type |
| `GetValue(int)` | P0 | Get value as object |
| `GetValues(object[])` | P0 | Get all values |
| `IsDBNull(int)` | P0 | Check if value is null |
| `GetBoolean(int)` | P0 | Get as bool |
| `GetByte(int)` | P0 | Get as byte |
| `GetChar(int)` | P0 | Get as char |
| `GetDateTime(int)` | P0 | Get as DateTime |
| `GetDecimal(int)` | P0 | Get as decimal |
| `GetDouble(int)` | P0 | Get as double |
| `GetFloat(int)` | P0 | Get as float |
| `GetGuid(int)` | P0 | Get as Guid |
| `GetInt16(int)` | P0 | Get as short |
| `GetInt32(int)` | P0 | Get as int |
| `GetInt64(int)` | P0 | Get as long |
| `GetString(int)` | P0 | Get as string |
| `GetBytes(...)` | P1 | Read bytes into buffer |
| `GetChars(...)` | P1 | Read chars into buffer |
| `GetEnumerator()` | P1 | Enumerate rows |
| `GetSchemaTable()` | P1 | Return schema as DataTable |
| `this[int]` indexer | P0 | Get value by ordinal |
| `this[string]` indexer | P0 | Get value by name |

**Internal:**
- Wrap `WitSqlResult` and iterate rows
- Convert `WitSqlValue` to CLR types
- Handle type conversions

#### 1.4 WitDbParameter

```csharp
public sealed class WitDbParameter : DbParameter
```

| Member | Priority | Description |
|--------|----------|-------------|
| `ParameterName` property | P0 | Parameter name (@name or :name) |
| `Value` property | P0 | Parameter value |
| `DbType` property | P0 | Database type |
| `Direction` property | P0 | Input/Output/InputOutput/ReturnValue |
| `IsNullable` property | P0 | Whether nullable |
| `Size` property | P1 | Size for variable-length types |
| `Precision` property | P1 | Precision for numeric types |
| `Scale` property | P1 | Scale for numeric types |
| `SourceColumn` property | P1 | Source column for updates |
| `SourceColumnNullMapping` property | P1 | Null mapping |
| `SourceVersion` property | P1 | DataRowVersion |
| `ResetDbType()` | P1 | Reset to default type |

#### 1.5 WitDbParameterCollection

```csharp
public sealed class WitDbParameterCollection : DbParameterCollection
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Count` property | P0 | Number of parameters |
| `Add(object)` | P0 | Add parameter |
| `AddRange(Array)` | P0 | Add multiple parameters |
| `Clear()` | P0 | Remove all parameters |
| `Contains(object)` | P0 | Check if contains |
| `Contains(string)` | P0 | Check by name |
| `CopyTo(Array, int)` | P0 | Copy to array |
| `GetEnumerator()` | P0 | Enumerate parameters |
| `IndexOf(object)` | P0 | Get index |
| `IndexOf(string)` | P0 | Get index by name |
| `Insert(int, object)` | P0 | Insert at index |
| `Remove(object)` | P0 | Remove parameter |
| `RemoveAt(int)` | P0 | Remove at index |
| `RemoveAt(string)` | P0 | Remove by name |
| `this[int]` indexer | P0 | Get/set by index |
| `this[string]` indexer | P0 | Get/set by name |

#### 1.6 WitDbTransaction

```csharp
public sealed class WitDbTransaction : DbTransaction
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Connection` property | P0 | Associated connection |
| `IsolationLevel` property | P0 | Transaction isolation level |
| `Commit()` | P0 | Commit transaction |
| `CommitAsync()` | P0 | Async version |
| `Rollback()` | P0 | Rollback transaction |
| `RollbackAsync()` | P0 | Async version |
| `Save(string)` | P1 | Create savepoint |
| `SaveAsync(string)` | P1 | Async version |
| `Rollback(string)` | P1 | Rollback to savepoint |
| `RollbackAsync(string)` | P1 | Async version |
| `Release(string)` | P1 | Release savepoint |
| `ReleaseAsync(string)` | P1 | Async version |
| `Dispose()` / `DisposeAsync()` | P0 | Cleanup (rollback if not committed) |

---

### Phase 2: Infrastructure (P0)

#### 2.1 WitDbConnectionStringBuilder

```csharp
public sealed class WitDbConnectionStringBuilder : DbConnectionStringBuilder
```

| Property | Priority | Description | Example |
|----------|----------|-------------|---------|
| `DataSource` | P0 | Database file path | `Data Source=mydb.witdb` |
| `Mode` | P0 | Memory/File | `Mode=File` |
| `Password` | P1 | Encryption password | `Password=secret` |
| `ReadOnly` | P1 | Open read-only | `ReadOnly=true` |
| `Pooling` | P1 | Enable connection pooling | `Pooling=true` |
| `MinPoolSize` | P2 | Minimum pool size | `MinPoolSize=1` |
| `MaxPoolSize` | P2 | Maximum pool size | `MaxPoolSize=100` |
| `DefaultTimeout` | P1 | Default command timeout | `DefaultTimeout=30` |
| `CacheSize` | P1 | Page cache size | `CacheSize=2000` |
| `PageSize` | P1 | Page size in bytes | `PageSize=4096` |
| `IsolationLevel` | P1 | Default isolation level | `IsolationLevel=Snapshot` |

**Connection String Examples:**
```
Data Source=mydb.witdb
Data Source=mydb.witdb;Password=secret
Data Source=:memory:
Data Source=mydb.witdb;Mode=ReadOnly;Pooling=true
```

#### 2.2 WitDbProviderFactory

```csharp
public sealed class WitDbProviderFactory : DbProviderFactory
```

| Member | Priority | Description |
|--------|----------|-------------|
| `Instance` static field | P0 | Singleton instance |
| `CanCreateDataAdapter` property | P1 | Returns true |
| `CanCreateCommandBuilder` property | P1 | Returns true |
| `CreateConnection()` | P0 | Create WitDbConnection |
| `CreateCommand()` | P0 | Create WitDbCommand |
| `CreateParameter()` | P0 | Create WitDbParameter |
| `CreateDataAdapter()` | P1 | Create WitDbDataAdapter |
| `CreateCommandBuilder()` | P1 | Create WitDbCommandBuilder |
| `CreateConnectionStringBuilder()` | P0 | Create WitDbConnectionStringBuilder |

**Registration:**
```csharp
DbProviderFactories.RegisterFactory("OutWit.Database.AdoNet", WitDbProviderFactory.Instance);
```

---

### Phase 3: Data Adapters (P1)

#### 3.1 WitDbDataAdapter

```csharp
public sealed class WitDbDataAdapter : DbDataAdapter
```

| Member | Priority | Description |
|--------|----------|-------------|
| `SelectCommand` property | P1 | SELECT command |
| `InsertCommand` property | P1 | INSERT command |
| `UpdateCommand` property | P1 | UPDATE command |
| `DeleteCommand` property | P1 | DELETE command |
| `Fill(DataSet)` | P1 | Fill DataSet from SELECT |
| `Fill(DataTable)` | P1 | Fill DataTable from SELECT |
| `FillAsync(...)` | P1 | Async versions |
| `Update(DataSet)` | P1 | Update database from DataSet |
| `Update(DataTable)` | P1 | Update database from DataTable |

#### 3.2 WitDbCommandBuilder

```csharp
public sealed class WitDbCommandBuilder : DbCommandBuilder
```

| Member | Priority | Description |
|--------|----------|-------------|
| `DataAdapter` property | P1 | Associated data adapter |
| `GetInsertCommand()` | P1 | Generate INSERT command |
| `GetUpdateCommand()` | P1 | Generate UPDATE command |
| `GetDeleteCommand()` | P1 | Generate DELETE command |
| `RefreshSchema()` | P1 | Refresh schema from database |

---

### Phase 4: Connection Pooling (P1)

#### 4.1 Connection Pool

| Feature | Priority | Description |
|---------|----------|-------------|
| `ConnectionPool` class | P1 | Pool manager |
| Min/Max pool size | P1 | Configurable limits |
| Connection lifetime | P1 | Max connection age |
| Idle timeout | P1 | Release idle connections |
| Validation on borrow | P2 | Validate before reuse |
| Thread-safe | P1 | Concurrent access |

---

### Phase 5: Advanced Features (P2)

#### 5.1 Schema Discovery

| Feature | Priority | Description |
|---------|----------|-------------|
| `GetSchema()` | P1 | Standard schema collections |
| `MetaDataCollections` | P1 | List available collections |
| `DataSourceInformation` | P1 | Provider information |
| `DataTypes` | P1 | Supported data types |
| `Restrictions` | P1 | Schema restrictions |
| `Tables` | P1 | Table metadata |
| `Columns` | P1 | Column metadata |
| `Indexes` | P1 | Index metadata |
| `IndexColumns` | P1 | Index column metadata |
| `ForeignKeys` | P1 | Foreign key metadata |
| `Views` | P1 | View metadata |

#### 5.2 Batch Execution

| Feature | Priority | Description |
|---------|----------|-------------|
| `WitDbBatch` | P2 | DbBatch implementation |
| `WitDbBatchCommand` | P2 | DbBatchCommand implementation |
| Batch execute | P2 | Execute multiple commands |

---

## Type Mappings

### WitSQL to DbType

| WitSQL Type | DbType | CLR Type |
|-------------|--------|----------|
| `TINYINT` | `SByte` | `sbyte` |
| `UTINYINT` | `Byte` | `byte` |
| `SMALLINT` | `Int16` | `short` |
| `USMALLINT` | `UInt16` | `ushort` |
| `INT` | `Int32` | `int` |
| `UINT` | `UInt32` | `uint` |
| `BIGINT` | `Int64` | `long` |
| `UBIGINT` | `UInt64` | `ulong` |
| `FLOAT16` | `Single` | `Half` |
| `FLOAT` | `Single` | `float` |
| `DOUBLE` | `Double` | `double` |
| `DECIMAL` | `Decimal` | `decimal` |
| `BOOLEAN` | `Boolean` | `bool` |
| `DATE` | `Date` | `DateOnly` |
| `TIME` | `Time` | `TimeOnly` |
| `DATETIME` | `DateTime` | `DateTime` |
| `DATETIMEOFFSET` | `DateTimeOffset` | `DateTimeOffset` |
| `INTERVAL` | `Object` | `TimeSpan` |
| `GUID` | `Guid` | `Guid` |
| `CHAR(n)` | `StringFixedLength` | `string` |
| `VARCHAR(n)` | `String` | `string` |
| `TEXT` | `String` | `string` |
| `BINARY(n)` | `Binary` | `byte[]` |
| `VARBINARY(n)` | `Binary` | `byte[]` |
| `BLOB` | `Binary` | `byte[]` |
| `JSON` | `String` | `string` |
| `ROWVERSION` | `Binary` | `byte[]` |

---

## File Structure

```
OutWit.Database.AdoNet/
??? WitDbConnection.cs
??? WitDbCommand.cs
??? WitDbDataReader.cs
??? WitDbParameter.cs
??? WitDbParameterCollection.cs
??? WitDbTransaction.cs
??? WitDbConnectionStringBuilder.cs
??? WitDbProviderFactory.cs
??? WitDbDataAdapter.cs
??? WitDbCommandBuilder.cs
??? WitDbException.cs
??? WitDbType.cs                    # DbType extensions
??? Pool/
?   ??? ConnectionPool.cs
?   ??? PooledConnection.cs
?   ??? PoolOptions.cs
??? Schema/
?   ??? SchemaProvider.cs
?   ??? SchemaCollections.cs
??? Internal/
?   ??? TypeConverter.cs            # WitSqlValue <-> CLR conversion
?   ??? ParameterBinder.cs          # Bind parameters to SQL
?   ??? ResultSetWrapper.cs         # Wrap WitSqlResult
??? README.md
```

---

## Test Plan

### Unit Tests

| Category | Tests |
|----------|-------|
| Connection lifecycle | 20+ |
| Command execution | 30+ |
| Data reader | 40+ |
| Parameters | 25+ |
| Transactions | 25+ |
| Connection string | 15+ |
| Type conversions | 50+ |
| Connection pooling | 20+ |
| Schema discovery | 20+ |
| **Total** | **245+** |

### Integration Tests

| Scenario | Description |
|----------|-------------|
| CRUD operations | Full create/read/update/delete |
| Transactions | Commit, rollback, savepoints |
| Concurrent access | Multiple connections |
| Large result sets | Memory efficiency |
| Parameterized queries | All parameter types |
| NULL handling | Nullable columns |
| Binary data | BLOB read/write |
| Unicode | International characters |

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| OutWit.Database | 1.0.0 | SQL engine |
| OutWit.Database.Core | 1.0.0 | Storage engine |

---

## Implementation Order

1. **Phase 1A:** `WitDbConnection`, `WitDbCommand` (basic)
2. **Phase 1B:** `WitDbDataReader`, `WitDbParameter`
3. **Phase 1C:** `WitDbTransaction`, `WitDbParameterCollection`
4. **Phase 2A:** `WitDbConnectionStringBuilder`
5. **Phase 2B:** `WitDbProviderFactory`
6. **Phase 3:** `WitDbDataAdapter`, `WitDbCommandBuilder`
7. **Phase 4:** Connection pooling
8. **Phase 5:** Schema discovery, batch execution

---

## See Also

- [System.Data.Common](https://docs.microsoft.com/en-us/dotnet/api/system.data.common)
- [DbProviderFactory](https://docs.microsoft.com/en-us/dotnet/api/system.data.common.dbproviderfactory)
- [ADO.NET Provider Model](https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/ado-net-provider-model)
