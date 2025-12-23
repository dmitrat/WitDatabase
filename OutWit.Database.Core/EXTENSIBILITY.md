# OutWit.Database.Core - Extensibility Guide

This guide explains how to extend WitDatabase.Core with custom implementations for storage, encryption, caching, and other components.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Provider System](#provider-system)
3. [Creating Custom Crypto Provider](#creating-custom-crypto-provider)
4. [Creating Custom Storage Backend](#creating-custom-storage-backend)
5. [Creating Custom Key-Value Store](#creating-custom-key-value-store)
6. [Creating Custom Page Cache](#creating-custom-page-cache)
7. [Creating Custom Transaction Journal](#creating-custom-transaction-journal)
8. [Creating Custom Secondary Index Factory](#creating-custom-secondary-index-factory)
9. [Packaging Your Extension](#packaging-your-extension)
10. [Best Practices](#best-practices)

---

## Architecture Overview

WitDatabase.Core uses a modular architecture where major components are defined by interfaces and can be replaced:

```
+------------------------------------------------------------------+
|                          WitDatabase                              |
|                               |                                   |
|      +------------------------+------------------------+          |
|      |                        |                        |          |
|      v                        v                        v          |
| +-----------+          +--------------+         +-----------+     |
| |IKeyValue  |          |ITransactional|         |IIndex     |     |
| |Store      |          |Store         |         |Manager    |     |
| +-----------+          +--------------+         +-----------+     |
|      |                        |                                   |
|      v                        v                                   |
| +-----------+          +--------------+                           |
| |StoreBTree |          |MvccTransact  |                           |
| |StoreLsm   |          |ionalStore    |                           |
| |YourStore  |<---------|              |                           |
| +-----------+          +--------------+                           |
|      |                                                            |
|      v                                                            |
| +---------------------------------------------------------------+ |
| |                         IStorage                               | |
| |  +-----------+  +-------------+  +-------------------------+  | |
| |  |StorageFile|  |StorageMemory|  |   StorageEncrypted      |  | |
| |  |YourStorage|  +-------------+  |          |              |  | |
| |  +-----------+                   |  +---------------+      |  | |
| |                                  |  |ICryptoProvider|      |  | |
| |                                  |  |  AES-GCM      |      |  | |
| |                                  |  |  ChaCha20     |      |  | |
| |                                  |  |  YourCrypto   |      |  | |
| |                                  |  +---------------+      |  | |
| |                                  +-------------------------+  | |
| +---------------------------------------------------------------+ |
+-------------------------------------------------------------------+
```

### Pluggable Components

| Interface | Purpose | Built-in Implementations |
|-----------|---------|-------------------------|
| `ICryptoProvider` | Encryption algorithm | `EncryptorProviderAesGcm`, `BouncyCastleCryptoProvider` |
| `IStorage` | Page-level storage backend | `StorageFile`, `StorageMemory`, `StorageEncrypted` |
| `IKeyValueStore` | Key-value storage engine | `StoreBTree`, `StoreLsm`, `StoreInMemory` |
| `IPageCache` | Page caching strategy | `PageCacheLru`, `PageCacheShardedClock` |
| `ITransactionJournal` | Transaction durability | `TransactionJournalFile`, `WalTransactionJournal` |
| `ISecondaryIndexFactory` | Secondary index creation | `SecondaryIndexFactoryKeyValueStore` |

---

## Provider System

All pluggable components implement `IProvider` and register with `ProviderRegistry`.

### IProvider Interface

```csharp
public interface IProvider
{
    /// <summary>
    /// Unique key identifying this provider type.
    /// Examples: "aes-gcm", "chacha20-poly1305", "file", "memory"
    /// </summary>
    string ProviderKey { get; }
}
```

### ProviderRegistry

The global registry manages provider factories:

```csharp
// Register a provider factory
ProviderRegistry.Instance.Register<ICryptoProvider>("my-crypto", 
    parameters => new MyCryptoProvider(parameters.GetRequired<byte[]>("key")));

// Create provider instance
var provider = ProviderRegistry.Instance.Create<ICryptoProvider>("my-crypto", 
    new ProviderParameters { ["key"] = myKey });

// Check if registered
bool exists = ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("my-crypto");

// Get all registered keys
var keys = ProviderRegistry.Instance.GetRegisteredKeys<ICryptoProvider>();
```

### Auto-Registration with ModuleInitializer

Use `[ModuleInitializer]` for automatic registration when assembly loads:

```csharp
public static class MyProviderRegistration
{
    private static bool _initialized;
    
    [ModuleInitializer]
    public static void Initialize()
    {
        if (_initialized) return;
        
        ProviderRegistry.Instance.Register<ICryptoProvider>("my-crypto", 
            p => new MyCryptoProvider(p.GetRequired<byte[]>("key")));
        
        _initialized = true;
    }
}
```

---

## Creating Custom Crypto Provider

### Step 1: Implement ICryptoProvider

```csharp
using OutWit.Database.Core.Interfaces;
using System.Security.Cryptography;

namespace MyCompany.WitDatabase.MyCrypto;

/// <summary>
/// Example custom crypto provider using XChaCha20-Poly1305.
/// </summary>
public sealed class XChaCha20CryptoProvider : ICryptoProvider
{
    public const string PROVIDER_KEY = "xchacha20-poly1305";
    
    private readonly byte[] _key;
    private bool _disposed;
    
    public XChaCha20CryptoProvider(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 256 bits (32 bytes)", nameof(key));
        _key = (byte[])key.Clone();
    }
    
    public string ProviderKey => PROVIDER_KEY;
    public int NonceSize => 24;  // XChaCha20 uses 24-byte nonce
    public int TagSize => 16;
    public int Overhead => NonceSize + TagSize;
    
    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, 
        Span<byte> ciphertext, Span<byte> tag)
    {
        ThrowIfDisposed();
        // Your encryption implementation here
        // ...
    }
    
    public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, 
        ReadOnlySpan<byte> tag, Span<byte> plaintext)
    {
        ThrowIfDisposed();
        try
        {
            // Your decryption implementation here
            // ...
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public ICryptoProvider Clone()
    {
        ThrowIfDisposed();
        return new XChaCha20CryptoProvider(_key);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }
    
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
```

### Step 2: Create Registration Class

```csharp
using System.Runtime.CompilerServices;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

namespace MyCompany.WitDatabase.MyCrypto;

public static class XChaCha20ProviderRegistration
{
    private static bool _initialized;
    
    [ModuleInitializer]
    public static void Initialize()
    {
        if (_initialized) return;
        
        ProviderRegistry.Instance.RegisterOrReplace<ICryptoProvider>(
            XChaCha20CryptoProvider.PROVIDER_KEY, 
            p => new XChaCha20CryptoProvider(p.GetRequired<byte[]>("key")));
        
        _initialized = true;
    }
    
    /// <summary>
    /// Explicit registration for scenarios where ModuleInitializer doesn't work.
    /// </summary>
    public static void EnsureRegistered() => Initialize();
}
```

### Step 3: Create Builder Extension

```csharp
using OutWit.Database.Core.Builder;

namespace MyCompany.WitDatabase.MyCrypto;

public static class WitDatabaseBuilderXChaCha20Extensions
{
    /// <summary>
    /// Enable XChaCha20-Poly1305 encryption with password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithXChaCha20Encryption(
        this WitDatabaseBuilder builder, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));
        
        var salt = DerivePasswordSalt(password);
        var key = DeriveKey(password, salt);
        
        builder.Options.CryptoProvider = new XChaCha20CryptoProvider(key);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }
    
    /// <summary>
    /// Enable XChaCha20-Poly1305 encryption with raw 256-bit key.
    /// </summary>
    public static WitDatabaseBuilder WithXChaCha20Encryption(
        this WitDatabaseBuilder builder, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("XChaCha20 requires a 32-byte key", nameof(key));
        
        builder.Options.CryptoProvider = new XChaCha20CryptoProvider(key);
        return builder;
    }
    
    private static byte[] DerivePasswordSalt(string password)
    {
        // Use SHA-256 to derive salt from password
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var input = System.Text.Encoding.UTF8.GetBytes(password + "_WitDB_XChaCha_Salt");
        var hash = sha256.ComputeHash(input);
        return hash[..16];
    }
    
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, 
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
    }
}
```

### Step 4: Usage

```csharp
using MyCompany.WitDatabase.MyCrypto;

// Using extension method
var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithXChaCha20Encryption("password")
    .WithBTree()
    .Build();

// Using raw key
byte[] key = new byte[32];
RandomNumberGenerator.Fill(key);

var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithXChaCha20Encryption(key)
    .WithBTree()
    .Build();
```

---

## Creating Custom Storage Backend

### Step 1: Implement IStorage

```csharp
using OutWit.Database.Core.Interfaces;

namespace MyCompany.WitDatabase.CloudStorage;

/// <summary>
/// Example cloud-based storage backend (e.g., for Azure Blob, S3).
/// </summary>
public sealed class StorageCloud : IStorage
{
    public const string PROVIDER_KEY = "cloud";
    
    private readonly ICloudBlobClient _client;
    private readonly string _containerName;
    private readonly int _pageSize;
    private long _pageCount;
    
    public StorageCloud(ICloudBlobClient client, string containerName, int pageSize = 4096)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _containerName = containerName;
        _pageSize = pageSize;
        
        // Initialize from existing container or create new
        _pageCount = GetExistingPageCount();
    }
    
    public string ProviderKey => PROVIDER_KEY;
    public int PageSize => _pageSize;
    public long PageCount => _pageCount;
    public bool IsReadOnly => false;
    
    public void ReadPage(long pageNumber, Span<byte> buffer)
    {
        if (pageNumber >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        
        var blobName = GetBlobName(pageNumber);
        var data = _client.DownloadBlob(_containerName, blobName);
        data.AsSpan().CopyTo(buffer);
    }
    
    public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, 
        CancellationToken cancellationToken = default)
    {
        if (pageNumber >= _pageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        
        var blobName = GetBlobName(pageNumber);
        var data = await _client.DownloadBlobAsync(_containerName, blobName, cancellationToken);
        data.AsMemory().CopyTo(buffer);
    }
    
    public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
    {
        var blobName = GetBlobName(pageNumber);
        _client.UploadBlob(_containerName, blobName, buffer.ToArray());
        
        if (pageNumber >= _pageCount)
            _pageCount = pageNumber + 1;
    }
    
    public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, 
        CancellationToken cancellationToken = default)
    {
        var blobName = GetBlobName(pageNumber);
        await _client.UploadBlobAsync(_containerName, blobName, buffer, cancellationToken);
        
        if (pageNumber >= _pageCount)
            _pageCount = pageNumber + 1;
    }
    
    public void SetSize(long pageCount)
    {
        _pageCount = pageCount;
        // Optionally pre-allocate or trim blobs
    }
    
    public void Flush()
    {
        // Cloud storage typically auto-flushes
        // Could implement local write cache here
    }
    
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Flush();
        return ValueTask.CompletedTask;
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
    
    private string GetBlobName(long pageNumber) => $"page_{pageNumber:D10}.bin";
    
    private long GetExistingPageCount()
    {
        // Query container for existing pages
        return _client.ListBlobs(_containerName)
            .Where(b => b.StartsWith("page_"))
            .Count();
    }
}
```

### Step 2: Create Builder Extension

```csharp
using OutWit.Database.Core.Builder;

namespace MyCompany.WitDatabase.CloudStorage;

public static class WitDatabaseBuilderCloudExtensions
{
    public static WitDatabaseBuilder WithCloudStorage(
        this WitDatabaseBuilder builder, 
        ICloudBlobClient client, 
        string containerName)
    {
        builder.Options.Storage = new StorageCloud(client, containerName, builder.Options.PageSize);
        return builder;
    }
}
```

### Step 3: Usage

```csharp
var cloudClient = new AzureBlobClient(connectionString);
var db = new WitDatabaseBuilder()
    .WithCloudStorage(cloudClient, "my-database")
    .WithBTree()
    .WithEncryption("password")  // Encrypt before sending to cloud
    .Build();
```

---

## Creating Custom Key-Value Store

For specialized storage engines:

```csharp
using OutWit.Database.Core.Interfaces;

namespace MyCompany.WitDatabase.SpecializedStore;

/// <summary>
/// Example: Time-series optimized key-value store.
/// </summary>
public sealed class StoreTimeSeries : IKeyValueStore, IKeyValueStoreStatistics
{
    public const string PROVIDER_KEY = "timeseries";
    
    // Implementation details...
    
    public string ProviderKey => PROVIDER_KEY;
    
    public byte[]? Get(ReadOnlySpan<byte> key) { /* ... */ }
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) { /* ... */ }
    public bool Delete(ReadOnlySpan<byte> key) { /* ... */ }
    public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey) { /* ... */ }
    public void Flush() { /* ... */ }
    
    // Async variants...
    
    public void Dispose() { /* ... */ }
}
```

Use with builder:

```csharp
var db = new WitDatabaseBuilder()
    .WithStore(new StoreTimeSeries(options))
    .WithTransactions()
    .Build();
```

---

## Creating Custom Page Cache

```csharp
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Cache;

namespace MyCompany.WitDatabase.AdvancedCache;

/// <summary>
/// Example: Adaptive Replacement Cache (ARC) implementation.
/// </summary>
public sealed class PageCacheArc : IPageCache
{
    public const string PROVIDER_KEY = "arc";
    
    private readonly IStorage _storage;
    private readonly int _capacity;
    
    // T1, T2, B1, B2 lists for ARC algorithm
    // ...
    
    public PageCacheArc(IStorage storage, int capacity)
    {
        _storage = storage;
        _capacity = capacity;
    }
    
    public string ProviderKey => PROVIDER_KEY;
    public int Count => /* ... */;
    public int DirtyCount => /* ... */;
    
    public CachedPage GetPage(long pageNumber) { /* ARC lookup logic */ }
    public CachedPage CreatePage(long pageNumber) { /* ... */ }
    public void MarkDirty(long pageNumber) { /* ... */ }
    public void ReleasePage(long pageNumber) { /* ... */ }
    public void Evict(long pageNumber) { /* ... */ }
    public void FlushAll() { /* ... */ }
    public ValueTask FlushAllAsync(CancellationToken ct = default) { /* ... */ }
    public void Clear() { /* ... */ }
    
    public void Dispose() { /* ... */ }
}
```

---

## Creating Custom Transaction Journal

```csharp
using OutWit.Database.Core.Interfaces;

namespace MyCompany.WitDatabase.DistributedJournal;

/// <summary>
/// Example: Distributed transaction journal using consensus protocol.
/// </summary>
public sealed class DistributedTransactionJournal : ITransactionJournal
{
    public const string PROVIDER_KEY = "distributed";
    
    private readonly IConsensusClient _consensusClient;
    
    public DistributedTransactionJournal(IConsensusClient consensusClient)
    {
        _consensusClient = consensusClient;
    }
    
    public string ProviderKey => PROVIDER_KEY;
    
    public void BeginTransaction(long transactionId)
    {
        _consensusClient.Propose(new BeginTxEntry(transactionId));
    }
    
    public void LogPut(long transactionId, ReadOnlySpan<byte> key, 
        ReadOnlySpan<byte> value, ReadOnlySpan<byte> oldValue)
    {
        _consensusClient.Propose(new PutEntry(transactionId, key.ToArray(), 
            value.ToArray(), oldValue.ToArray()));
    }
    
    public void LogDelete(long transactionId, ReadOnlySpan<byte> key, 
        ReadOnlySpan<byte> oldValue)
    {
        _consensusClient.Propose(new DeleteEntry(transactionId, key.ToArray(), 
            oldValue.ToArray()));
    }
    
    public void CommitTransaction(long transactionId)
    {
        _consensusClient.Propose(new CommitTxEntry(transactionId));
    }
    
    public void RollbackTransaction(long transactionId)
    {
        _consensusClient.Propose(new RollbackTxEntry(transactionId));
    }
    
    public void Sync()
    {
        _consensusClient.WaitForCommit();
    }
    
    public int Recover(IKeyValueStore store)
    {
        // Replay committed transactions from consensus log
        return _consensusClient.Replay(store);
    }
    
    public void Checkpoint()
    {
        _consensusClient.CreateSnapshot();
    }
    
    public void Dispose()
    {
        _consensusClient.Dispose();
    }
}
```

---

## Creating Custom Secondary Index Factory

```csharp
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Indexes;

namespace MyCompany.WitDatabase.FullTextIndex;

/// <summary>
/// Example: Full-text search index factory.
/// </summary>
public sealed class FullTextIndexFactory : ISecondaryIndexFactory
{
    private readonly string _indexDirectory;
    
    public FullTextIndexFactory(string indexDirectory)
    {
        _indexDirectory = indexDirectory;
    }
    
    public string ProviderKey => "fulltext";
    
    public ISecondaryIndex CreateIndex(string name, bool isUnique)
    {
        var indexPath = Path.Combine(_indexDirectory, name);
        return new FullTextIndex(indexPath, isUnique);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

public sealed class FullTextIndex : ISecondaryIndex
{
    // Lucene.NET or similar implementation
    // ...
}
```

Use with builder:

```csharp
var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithBTree()
    .WithSecondaryIndexFactory(new FullTextIndexFactory("./indexes"))
    .Build();
```

---

## Packaging Your Extension

### Project Structure

```
MyCompany.WitDatabase.MyCrypto/
|-- MyCompany.WitDatabase.MyCrypto.csproj
|-- MyCryptoProvider.cs
|-- MyCryptoProviderRegistration.cs
|-- WitDatabaseBuilderExtensions.cs
+-- README.md
```

### Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    
    <!-- NuGet Package Info -->
    <PackageId>MyCompany.WitDatabase.MyCrypto</PackageId>
    <Version>1.0.0</Version>
    <Description>Custom crypto provider for WitDatabase</Description>
    <Authors>Your Name</Authors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OutWit.Database.Core" Version="1.0.0" />
    <!-- Your crypto library if needed -->
  </ItemGroup>
</Project>
```

### Documentation

Include a README.md with:
- Installation instructions
- Usage examples
- Configuration options
- Performance considerations
- Known limitations

---

## Best Practices

### 1. Thread Safety

All providers should be thread-safe:

```csharp
public sealed class MyCryptoProvider : ICryptoProvider
{
    private readonly Lock _lock = new();
    
    public void Encrypt(...)
    {
        lock (_lock)
        {
            // Thread-safe encryption
        }
    }
}
```

### 2. Proper Disposal

Always implement `IDisposable` correctly:

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    
    // Clear sensitive data
    CryptographicOperations.ZeroMemory(_key);
    
    // Dispose managed resources
    _underlyingResource?.Dispose();
}
```

### 3. Provider Key Naming

Use lowercase, hyphen-separated names:
- Good: `"aes-gcm"`, `"chacha20-poly1305"`, `"azure-blob"`
- Bad: `"AES_GCM"`, `"ChaCha20Poly1305"`, `"AzureBlob"`

### 4. Error Handling

Throw meaningful exceptions:

```csharp
public bool Decrypt(...)
{
    try
    {
        // Decryption logic
        return true;
    }
    catch (AuthenticationTagMismatchException)
    {
        // Don't throw - return false for auth failures
        return false;
    }
    catch (Exception ex)
    {
        // Wrap unexpected errors
        throw new CryptoProviderException("Decryption failed", ex);
    }
}
```

### 5. Clone Support

Crypto providers must support cloning for index encryption:

```csharp
public ICryptoProvider Clone()
{
    ThrowIfDisposed();
    // Create new instance with same key (copy, don't share)
    return new MyCryptoProvider((byte[])_key.Clone());
}
```

### 6. Validation

Validate inputs early:

```csharp
public MyCryptoProvider(byte[] key)
{
    if (key == null)
        throw new ArgumentNullException(nameof(key));
    if (key.Length != 32)
        throw new ArgumentException("Key must be 32 bytes", nameof(key));
    
    _key = (byte[])key.Clone();
}
```

### 7. Testing

Include comprehensive tests:

```csharp
[Fact]
public void Encrypt_Decrypt_RoundTrip()
{
    var key = new byte[32];
    RandomNumberGenerator.Fill(key);
    
    using var provider = new MyCryptoProvider(key);
    
    var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
    var nonce = new byte[provider.NonceSize];
    RandomNumberGenerator.Fill(nonce);
    
    var ciphertext = new byte[plaintext.Length];
    var tag = new byte[provider.TagSize];
    
    provider.Encrypt(nonce, plaintext, ciphertext, tag);
    
    var decrypted = new byte[plaintext.Length];
    var success = provider.Decrypt(nonce, ciphertext, tag, decrypted);
    
    Assert.True(success);
    Assert.Equal(plaintext, decrypted);
}
```

---

## See Also

- [README.md](README.md) - Project documentation
- [STATUS.md](STATUS.md) - Implementation status
- [OutWit.Database.Core.BouncyCastle](../OutWit.Database.Core.BouncyCastle/) - Reference implementation
