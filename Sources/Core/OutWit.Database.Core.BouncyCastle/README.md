# OutWit.Database.Core.BouncyCastle

**ChaCha20-Poly1305 encryption provider** for WitDatabase using BouncyCastle.

This package provides an alternative encryption algorithm when AES-NI hardware acceleration is not available.

---

## Installation

```xml
<PackageReference Include="OutWit.Database.Core.BouncyCastle" Version="1.0.0" />
```

---

## Quick Start

### Password-Based Encryption

```csharp
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.BouncyCastle;

var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithBouncyCastleEncryption("my-secure-password")
    .WithBTree()
    .Build();
```

### User + Password Encryption

```csharp
var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithBouncyCastleEncryption("username", "password")
    .WithBTree()
    .Build();
```

### Raw Key Encryption

```csharp
byte[] key = new byte[32]; // 256-bit key
RandomNumberGenerator.Fill(key);

var db = new WitDatabaseBuilder()
    .WithFilePath("encrypted.db")
    .WithBouncyCastleEncryption(key)
    .WithBTree()
    .Build();
```

---

## Why ChaCha20-Poly1305?

| Feature | AES-GCM | ChaCha20-Poly1305 |
|---------|---------|-------------------|
| Hardware acceleration | Requires AES-NI | Software-only |
| Performance (with AES-NI) | Faster | Slower |
| Performance (without AES-NI) | Slower | Faster |
| Security | Excellent | Excellent |
| Key size | 128/192/256-bit | 256-bit |

**Use ChaCha20-Poly1305 when:**
- Running on hardware without AES-NI (older CPUs, some ARM devices)
- Running in Blazor WebAssembly (no hardware acceleration)
- Consistent performance across all platforms is important

**Use AES-GCM (default) when:**
- Running on modern x86/x64 CPUs with AES-NI
- Maximum performance is required

---

## API Reference

### WitDatabaseBuilder Extensions

```csharp
// Password-based encryption (PBKDF2 key derivation)
builder.WithBouncyCastleEncryption(string password)

// User + password encryption
builder.WithBouncyCastleEncryption(string user, string password)

// Raw 256-bit key
builder.WithBouncyCastleEncryption(byte[] key)

// Raw key with custom salt
builder.WithBouncyCastleEncryption(byte[] key, byte[] salt)
```

### BouncyCastleCryptoProvider

```csharp
// Create provider with raw key
var provider = new BouncyCastleCryptoProvider(key);

// Create provider from password
var provider = BouncyCastleCryptoProvider.FromPassword(
    password: "secret",
    salt: saltBytes,
    iterations: 100_000
);

// Properties
provider.NonceSize   // 12 bytes
provider.TagSize     // 16 bytes
provider.ProviderKey // "chacha20-poly1305"
```

---

## Security Details

### Key Derivation

- **Algorithm**: PBKDF2-SHA256
- **Iterations**: 100,000 (default)
- **Key size**: 256 bits
- **Salt size**: 16 bytes (derived from password or user)

### Encryption

- **Algorithm**: ChaCha20-Poly1305
- **Key size**: 256 bits
- **Nonce size**: 96 bits (12 bytes)
- **Authentication tag**: 128 bits (16 bytes)

### Memory Safety

- Uses `ArrayPool<T>` for temporary buffers (reduced GC pressure)
- Sensitive data is zeroed after use via `CryptographicOperations.ZeroMemory`
- Key material is securely cleared on `Dispose()`

---

## Blazor WebAssembly Support

ChaCha20-Poly1305 works well in Blazor WebAssembly where hardware AES acceleration is not available:

```csharp
var db = new WitDatabaseBuilder()
    .WithIndexedDbStorage("MyDatabase", JSRuntime)
    .WithBouncyCastleEncryption("password")
    .WithBTree()
    .Build();
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| BouncyCastle.Cryptography | 2.6.2 | ChaCha20-Poly1305 implementation |
| OutWit.Database.Core | 1.0.0 | Core database library |

---

## Related Projects

| Project | Description |
|---------|-------------|
| OutWit.Database.Core | Core storage engine |
| OutWit.Database.Core.IndexedDb | IndexedDB storage for Blazor WASM |
| OutWit.Database | SQL execution engine |

---

## License

MIT License - see LICENSE file for details.

---

## See Also

- [OutWit.Database.Core](../OutWit.Database.Core/) - Core database library
- [OutWit.Database.Core.IndexedDb](../OutWit.Database.Core.IndexedDb/) - Blazor WASM support
- [BouncyCastle](https://www.bouncycastle.org/) - Cryptography library
- [ROADMAP.md](ROADMAP.md) - Version 2.0 planned features
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
