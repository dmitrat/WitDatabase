# TODO: Full Async BTree Refactoring for WASM Support

## Problem Statement

Current BTree implementation uses synchronous I/O operations internally, which causes `PlatformNotSupportedException: Cannot wait on monitors on this runtime` in Blazor WebAssembly when using IndexedDB storage.

### Root Cause
- `StorageIndexedDb` sync methods use `.GetAwaiter().GetResult()` which deadlocks in WASM single-threaded environment
- BTree operations (Insert, Delete, Split, Merge) call `PageManager.AllocatePage()` synchronously
- Page cache (`PageCacheShardedClock`) loads pages synchronously via `IStorage.ReadPage()`
- Index metadata restoration during database creation uses sync I/O
- BTree entry count loading/saving uses sync page access
- **PageManager.AllocatePageAsync** was calling sync cache methods inside

### Current State ? COMPLETE
All sync I/O removed from async initialization and operation paths.

---

## All Phases Complete ?

### Final Fix: PageManager Async Methods

**Problem:** `PageManager.AllocatePageAsync()` called sync `m_cache.GetPage()` and `m_cache.Evict()` inside lock.
Also `FlushAsync()` called sync `SaveHeaderImmediate()`.

**Solution:**
- Refactored `AllocatePageAsync()` to use `m_cache.GetPageAsync()` and `m_cache.EvictAsync()`
- Refactored `FlushAsync()` to use `SaveHeaderImmediateAsync()`
- All cache and storage operations now use async versions

---

## Progress Summary

**Total Tests:** 1925 passing (all platforms)

---

## ? REFACTORING COMPLETE

### WASM-Safe Chain (NO sync I/O):
```
WitDatabaseBuilder.BuildAsync()
  ??? BuildStoreInternalAsync()
        ??? StorageIndexedDb.InitializeAsync()      [async]
        ??? StoreBTree.CreateAsync()
              ??? PageManager.CreateAsync()
                    ??? IsNewDatabaseAsync()        [async]
                    ??? LoadHeaderAsync()           [async]
                    ??? InitializeNewDatabaseAsync() [async]
              ??? BTree.CreateAsync()
                    ??? CreateLeafNodeAsync()
                          ??? AllocatePageAsync()   [async cache + storage]
                    ??? LoadEntryCountStaticAsync() [async]
                    ??? SaveEntryCountIfDirtyAsync() [async]
  ??? WitDatabase.CreateAsync()
        ??? RestoreIndexesFromMetadataAsync()
              ??? IndexMetadataStore.LoadAllIndexesAsync() [async]
```

### All Async Methods Now Use:
- `m_cache.GetPageAsync()` instead of `m_cache.GetPage()`
- `m_cache.CreatePageAsync()` instead of `m_cache.CreatePage()`
- `m_cache.EvictAsync()` instead of `m_cache.Evict()`
- `m_storage.ReadPageAsync()` instead of `m_storage.ReadPage()`
- `m_storage.WritePageAsync()` instead of `m_storage.WritePage()`
- `m_storage.SetSizeAsync()` instead of `m_storage.SetSize()`

---

## API Summary for WASM Usage

```csharp
@inject IJSRuntime JSRuntime

var db = await new WitDatabaseBuilder()
    .WithIndexedDbStorage("MyDatabase", JSRuntime)
    .WithBTree()
    .WithTransactions()
    .BuildAsync();  // Fully async - no sync I/O anywhere

await db.PutAsync(key, value);
var value = await db.GetAsync(key);
await db.DeleteAsync(key);

await foreach (var (k, v) in db.ScanAsync(null, null))
{
    // process
}

await db.FlushAsync();
await db.DisposeAsync();
