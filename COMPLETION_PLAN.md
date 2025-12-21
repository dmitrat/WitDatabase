# WitDatabase - Development Plan v1.7

## PROJECT STATUS: COMPLETED

**Last Updated**: 2024-12-21

---

## New in v1.7: Provider System (Completed)

### 1. IProvider - Base Interface
All components implement `IProvider`:
- `IStorage` - storage providers (file, memory, encrypted)
- `IKeyValueStore` - store providers (btree, lsm, inmemory)
- `ICryptoProvider` - encryption providers (aes-gcm, chacha20-poly1305)
- `IPageCache` - cache providers (lru, clock)
- `ITransactionJournal` - journal providers (rollback, wal)

### 2. ProviderRegistry - Centralized Registry
```csharp
// Create provider by key
var store = ProviderRegistry.Instance.Create<IKeyValueStore>("btree",
    new ProviderParameters()
        .Set("storage", storage)
        .Set("cacheSize", 1000));

// Check availability
bool available = ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("aes-gcm");

// List all registered keys
var keys = ProviderRegistry.Instance.GetRegisteredKeys<IStorage>();
```

### 3. Auto-registration of Built-in Providers

**Core Project (`OutWit.Database.Core`):**
| Type | Keys |
|------|------|
| `IStorage` | file, memory, encrypted |
| `IKeyValueStore` | btree, lsm, inmemory |
| `ICryptoProvider` | aes-gcm |
| `IPageCache` | clock, lru |
| `ITransactionJournal` | rollback, wal |

**BouncyCastle Project (`OutWit.Database.Core.BouncyCastle`):**
| Type | Keys |
|------|------|
| `ICryptoProvider` | chacha20-poly1305 |

### 4. ProviderMetadata in Header
Header (bytes 48-99) stores:
- `Features` - flags (Encryption, Transactions, FileLocking)
- `StoreProviderKey` - store key (16 bytes)
- `EncryptionProviderKey` - encryption key (16 bytes)

### 5. Auto-detection of Settings on Open()
```csharp
// Automatically restores settings from header
using var db = WitDatabase.Open(path);

// For encrypted DB - password required
// InvalidDataException: "Database is encrypted with 'aes-gcm' provider..."

// Get info without opening
var info = WitDatabase.GetDatabaseInfo(path);
// info.StoreProvider, info.RequiresEncryption, info.HasTransactions...
```

### 6. BouncyCastle Extension
```csharp
// Explicit registration (if ModuleInitializer didn't work)
BouncyCastleProviderRegistration.EnsureRegistered();

// Usage via Builder
using var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithBouncyCastleEncryption("password")
    .Build();
```

---

## TESTS

| File | Tests | Description |
|------|-------|-------------|
| `BuiltInProviderTests.cs` | 18 | Core provider registration |
| `BouncyCastleProviderRegistrationTests.cs` | 5 | BouncyCastle registration |
| `ProviderRegistryTests.cs` | 22 | Provider registry |
| `ProviderMetadataTests.cs` | 10 | Metadata storage |
| `DatabaseConfigurationPersistenceTests.cs` | 21 | Auto-detection and persistence |
| `BouncyCastle*Tests.cs` | 30 | BouncyCastle encryption tests |

**Total Provider-related tests: 93+**

---

## USAGE

### Simple Case - Auto-detection
```csharp
using var db = WitDatabase.Open("data.db");
```

### Encrypted DB (AES-GCM)
```csharp
using var db = WitDatabase.Open("data.db", "password");
```

### ChaCha20-Poly1305 (BouncyCastle)
```csharp
using var db = new WitDatabaseBuilder()
    .WithFilePath("data.db")
    .WithBouncyCastleEncryption("password")
    .Build();
```

### Register Custom Providers
```csharp
ProviderRegistry.Instance.Register<ICryptoProvider>("my-crypto", 
    p => new MyCryptoProvider(p.GetRequired<byte[]>("key")));
```

---

## CHANGELOG

### 2024-12-21 (v1.7)
- Added `BuiltInProviderRegistration` with `[ModuleInitializer]`
- Added `BouncyCastleProviderRegistration` for ChaCha20-Poly1305
- Added `ConfigurationValidator` for settings validation
- Added `WitDatabase.Open()` auto-detection of settings
- Added `WitDatabase.GetDatabaseInfo()` for inspection without opening
- Added 93+ tests for Provider system

### 2024-12-21 (v1.6)
- Added `IProvider` base interface
- Added `ProviderRegistry` for provider management
- Added `ProviderMetadata` in header

### 2024-12-21 (v1.5)
- Added new automatic registration system
- Added configuration persistence
