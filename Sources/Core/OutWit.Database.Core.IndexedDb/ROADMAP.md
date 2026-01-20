# OutWit.Database.Core.IndexedDb - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.Core.IndexedDb.

---

## Version 2.0 - Planned Features

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| Multi-Tab Coordination | SharedWorker or BroadcastChannel for tab synchronization |
| Storage Quota API | Check and request storage quota |
| Offline Sync | Queue operations when offline, sync when online |

### Priority 2: Enhancements

| Feature | Description |
|---------|-------------|
| Web Worker Support | Run database operations in Web Worker |
| OPFS Integration | Origin Private File System for better performance |
| Compression | Compress pages before storing in IndexedDB |
| Background Sync | Service Worker integration for background operations |

---

## Implementation Details

### Multi-Tab Coordination (Priority 1)

Prevent data corruption when multiple tabs access the same database:

```csharp
public class IndexedDbCoordinator
{
    private readonly BroadcastChannel _channel;
    
    public async Task<IDisposable> AcquireLockAsync(string databaseName);
    public void NotifyChange(string databaseName, ChangeType type);
}
```

### Storage Quota API (Priority 1)

```csharp
public static class StorageQuota
{
    public static async Task<StorageEstimate> EstimateAsync(IJSRuntime jsRuntime);
    public static async Task<bool> RequestPersistentStorageAsync(IJSRuntime jsRuntime);
}

public struct StorageEstimate
{
    public long Usage { get; }
    public long Quota { get; }
    public double UsagePercentage => (double)Usage / Quota * 100;
}
```

### OPFS Integration (Priority 2)

Origin Private File System for file-like access in browser:

```csharp
public sealed class StorageOpfs : IStorage
{
    public const string PROVIDER_KEY = "opfs";
    
    // Uses FileSystemSyncAccessHandle for synchronous access
    // Much better performance than IndexedDB for large databases
}
```

---

## Browser Feature Detection

```javascript
// witdb-feature-detection.js
window.witDbFeatures = {
    hasIndexedDb: () => !!window.indexedDB,
    hasOpfs: () => 'getDirectory' in navigator.storage,
    hasBroadcastChannel: () => !!window.BroadcastChannel,
    hasSharedWorker: () => !!window.SharedWorker
};
```

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
