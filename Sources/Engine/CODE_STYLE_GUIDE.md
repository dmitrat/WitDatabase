# WitDatabase Code Style Guide

## 1. Class Structure

One file/class per file. Exceptions: nested classes and partial classes.

### Element Order in Classes

```
1. Constants
2. Events (if any)
3. Fields
4. Constructors
5. Initialization (if any)
   - Methods like InitSomething: InitDefault, InitEvents, InitCommands, etc.
6. Methods:
   - If few - single "Functions" region
   - If many - group by meaning into separate regions
   - Synchronous and async versions are placed in the same semantic region
   - Private methods at the end of their regions
7. Interface implementations (each in its own region)
   - Each interface type gets its own region named after the interface
   - Examples: IEnumerable, IDisposable, IAsyncDisposable
8. Properties (at the very end)
```

### Structure Example

```csharp
public sealed class MyService : IDisposable
{
    #region Constants
    
    private const int DEFAULT_TIMEOUT = 5000;
    
    #endregion

    #region Events
    
    public event EventHandler? DataChanged;
    
    #endregion

    #region Fields

    private readonly IStorage m_storage;
    private bool m_disposed;

    #endregion

    #region Constructors

    public MyService(IStorage storage)
    {
        m_storage = storage;
        InitDefault();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        // ...
    }

    #endregion

    #region Read

    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        // ...
    }

    public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default)
    {
        // ...
    }

    #endregion

    #region Write

    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        // ...
    }

    private void WriteInternal(ReadOnlySpan<byte> data)
    {
        // Private methods at the end of the region
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        m_storage.Dispose();
    }

    #endregion

    #region Properties

    public bool IsDisposed => m_disposed;

    #endregion
}
```

## 2. File Naming

### Extended Interface Suffix Principle

File names (containing interfaces, abstract base classes) are written in **reverse order** to:
1. Improve visual grouping in Solution Explorer
2. Simplify file search among hundreds of files in the project

### Examples

**Wrong:**
```
SimpleWalReplayVisitor.cs
TransactionalWalReplayVisitor.cs
```
- files far apart (S... and T...)

**Correct:**
```
WalReplayVisitorSimple.cs
WalReplayVisitorTransactional.cs
```
- files next to each other, right by `IWalReplayVisitor` interface

### Prefix Examples

| Interface/Base Class | Implementations |
|----------------------|-----------------|
| `IStorage` | `StorageFile.cs`, `StorageMemory.cs`, `StorageEncrypted.cs` |
| `IPageCache` | `PageCacheLru.cs`, `PageCacheClock.cs` |
| `ICryptoProvider` | `CryptoProviderAesGcm.cs`, `CryptoProviderBouncyCastle.cs` |

**Exception:** If too many implementations, use folders:
```
Storage/
  FileStorage.cs
  MemoryStorage.cs
  EncryptedStorage.cs
```

## 3. Regions

- Always use `#region` / `#endregion` for grouping
- Region names - brief and clear
- Empty line after `#region` and before `#endregion`

```csharp
#region Constants

private const int PAGE_SIZE = 4096;

#endregion
```

## 4. Access Modifiers

- Explicitly specify modifiers for all methods and fields
- Use `private` by default
- Use `internal` for classes not intended for external use
- `sealed` for classes not intended for inheritance

## 5. Fields and Constants

- Private fields with `m_` prefix: `m_storage`, `m_disposed`
- Constants in UPPER_CASE: `DEFAULT_PAGE_SIZE`, `MAX_KEY_LENGTH`
- `readonly` for immutable fields
- Static fields with `s_` prefix: `s_instance`

```csharp
#region Constants

private const int DEFAULT_PAGE_SIZE = 4096;
private const int MAX_RETRIES = 3;

#endregion

#region Fields

private static readonly object s_lock = new();

private readonly IStorage m_storage;
private readonly int m_pageSize;
private bool m_disposed;

#endregion
```

## 6. XML Documentation

- Required for all public methods
- `<summary>` - brief description
- `<param>` - for method parameters
- `<returns>` - for return values
- `<exception>` - for thrown exceptions

## 7. Nullable Reference Types

- Enable `#nullable enable` in all files (via csproj `<Nullable>enable</Nullable>`)
- Explicitly specify nullability for parameters and return values
- Use `?` for nullable types
- Use `!` only when certain (avoid where possible)

```csharp
public byte[]? Get(ReadOnlySpan<byte> key);  // may return null
public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);  // doesn't accept null
```

## 8. Extension Methods

For builder patterns and fluent APIs, extract extension methods to separate base from extensions:

```csharp
// In main project
public sealed class WitDatabaseBuilder
{
    public WitDatabaseBuilderOptions Options { get; } = new();
    
    public WitDatabase Build() { /* ... */ }
}

// Extension methods in same project
public static class WitDatabaseBuilderExtensions
{
    public static WitDatabaseBuilder WithMemoryStorage(this WitDatabaseBuilder builder)
    {
        builder.Options.UseMemoryStorage = true;
        return builder;
    }
}

// In another project (e.g., BouncyCastle)
public static class WitDatabaseBuilderBouncyCastleExtensions
{
    public static WitDatabaseBuilder WithBouncyCastleEncryption(
        this WitDatabaseBuilder builder, byte[] key)
    {
        builder.Options.CryptoProvider = new BouncyCastleCryptoProvider(key);
        return builder;
    }
}
```

## 9. Test Style (NUnit 4)

### Naming

- **Class name:** Ends with `Tests` (plural mandatory)
  - Examples: `StorageFileTests`, `BTreeTests`, `TransactionTests`
  
- **Method name:** Ends with `Test` (singular)
  - Example: `StorageFileHasCorrectProviderKeyTest`
  - No `_` symbols in names
  - PascalCase

### Bad Test Styles

**Wrong:**
```csharp
public void Storage_File_Has_Correct_Provider_Key_Test() { }
public void StorageFile_HasCorrectProviderKey() { }  // no Test
public void test_storage_file() { }  // snake_case
```

**Correct:**
```csharp
public void StorageFileHasCorrectProviderKeyTest() { }
public void PutAndGetReturnsValueTest() { }
public void DeleteNonExistentKeyReturnsFalseTest() { }
public void TransactionRollbackDiscardsChangesTest() { }
```

### Test File Structure

- **One test class per file**
- Name matches all its tests: `StorageFileTests.cs`
- Group test methods via `#region`

```csharp
[TestFixture]
public class StorageFileTests
{
    private string m_testDir = null!;

    [SetUp]
    public void Setup() { /* ... */ }

    [TearDown]
    public void TearDown() { /* ... */ }

    #region Read Tests

    [Test]
    public void ReadPageReturnsCorrectDataTest() { /* ... */ }

    [Test]
    public void ReadPageThrowsOnInvalidPageNumberTest() { /* ... */ }

    #endregion

    #region Write Tests

    [Test]
    public void WritePagePersistsDataTest() { /* ... */ }

    #endregion
}
```

### Test File Parallelism

Files are placed in identical hierarchy relative to main project:

```
OutWit.Database.Core/
  Storage/
    StorageFile.cs
    StorageMemory.cs
  Stores/
    StoreBTree.cs

OutWit.Database.Core.Tests/
  Storage/
    StorageFileTests.cs
    StorageMemoryTests.cs
  Stores/
    StoreBTreeTests.cs
