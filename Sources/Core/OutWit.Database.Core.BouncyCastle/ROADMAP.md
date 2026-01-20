# OutWit.Database.Core.BouncyCastle - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.Core.BouncyCastle.

---

## Version 2.0 - Planned Features

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| XChaCha20-Poly1305 | Extended nonce variant (192-bit nonce) |
| Argon2 Key Derivation | Memory-hard key derivation as alternative to PBKDF2 |
| Configurable Iterations | Allow custom PBKDF2 iteration count |

### Priority 2: Enhancements

| Feature | Description |
|---------|-------------|
| AES-GCM-SIV | Nonce-misuse resistant encryption |
| Encryption Benchmarks | Performance comparison utilities |
| Hardware Detection | Auto-select best algorithm for platform |

---

## Implementation Details

### XChaCha20-Poly1305 (Priority 1)

Extended nonce version for better security when using random nonces:

```csharp
public sealed class XChaCha20CryptoProvider : ICryptoProvider
{
    public const string PROVIDER_KEY = "xchacha20-poly1305";
    
    public int NonceSize => 24;  // 192-bit nonce
    public int TagSize => 16;
    
    // Uses HChaCha20 for subkey derivation
}
```

### Argon2 Key Derivation (Priority 1)

Memory-hard alternative to PBKDF2:

```csharp
public static class Argon2KeyDerivation
{
    public static byte[] DeriveKey(
        string password,
        byte[] salt,
        int memoryCost = 65536,    // 64 MB
        int timeCost = 3,
        int parallelism = 4,
        int keyLength = 32);
}
```

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
