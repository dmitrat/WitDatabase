# Database Core Roadmap

## ??????? ??????: Phase 1 ???????? ?

---

## Phase 1: Critical Fixes (Blocking Issues) ?

### 1.1 Transaction Subsystem Fixes

- [x] **Fix public field `_ownsStore`** ? `private m_ownsStore` ? `TransactionalStore.cs`
- [x] **Delete duplicate `TransactionLog.cs`** - ???????? `WalJournal` ??? encryption
- [x] **Fix blocking async dispose in `Transaction.Commit()`**
  - ????????: `m_asyncLockHandle?.DisposeAsync().AsTask().GetAwaiter().GetResult()` ????? ??????? deadlock
  - ???????: ???????? `ReleaseLocks()` ? `Task.Run().Wait(timeout)` ??? ??????????? ????????????
  - ????: `OutWit.Database.Core/Transactions/Transaction.cs`

### 1.2 Concurrency Subsystem Fixes

- [x] **Unify DatabaseLock sync/async mechanism**
  - ????????: ?????????????? ??? ?????? ????????? - `ReaderWriterLockSlim` ??? sync ? `SemaphoreSlim` ??? async
  - ???????: ????????????? ?? ???? `SemaphoreSlim` (???????????? ??? ??????)
  - ????: `OutWit.Database.Core/Concurrency/DatabaseLock.cs`

---

## Phase 2: WAL Unification

### 2.1 Add missing features to WalJournal

- [ ] **Add CRC32 validation in `WalJournal`**
  - LSM WAL ????? CRC32, Transaction WAL - ???
  - ???????? ????????? ??????????? ???????
  - ????: `OutWit.Database.Core/Transactions/WalJournal.cs`

- [ ] **Add ArrayPool in `WalJournal`**
  - LSM WAL ?????????? ArrayPool ??? ???????? ?????????
  - ????????? ??????? ? Transaction WAL
  - ????: `OutWit.Database.Core/Transactions/WalJournal.cs`

### 2.2 Create shared infrastructure (Future)

- [ ] **Extract common CRC32 calculation to shared utility**
  - ??????? `OutWit.Database.Core/Utils/Crc32.cs`
  - ???????????? ? LSM WAL ? Transaction WAL

- [ ] **Consider BaseWalWriter base class** (Optional)
  - ????? ??? ??? write/read entry
  - Encryption handling
  - ????? ???? ?????????, ??????? ????? ??????????

---

## Phase 3: Tests for Concurrency & Transactions

### 3.1 Concurrency Tests

- [ ] **Create `LockManagerTests.cs`**
  ```
  - ConcurrentReadersAllowedTest
  - WriterBlocksReadersTest  
  - TimeoutOnDeadlockTest
  - AsyncLockAcquisitionTest
  - LockReleaseOnDisposeTest
  ```

- [ ] **Create `DatabaseLockTests.cs`**
  ```
  - ReadLockAllowsMultipleReadersTest
  - WriteLockIsExclusiveTest
  - TimeoutThrowsExceptionTest
  - SyncAndAsyncCanMixTest
  ```

- [ ] **Create `FileLockTests.cs`**
  ```
  - SharedLockAllowsMultipleReadersTest
  - ExclusiveLockBlocksOthersTest
  - ExponentialBackoffWorksTest
  - LockFileCleanupOnDisposeTest
  ```

### 3.2 Transaction Tests

- [ ] **Create `TransactionalStoreTests.cs`**
  ```
  - CommitPersistsChangesTest
  - RollbackDiscardsChangesTest
  - AutoRollbackOnDisposeTest
  - ConcurrentTransactionsBlockedTest
  - NonTransactionalWriteAutoCommitsTest
  - CheckpointTruncatesJournalTest
  ```

- [ ] **Create `TransactionTests.cs`**
  ```
  - GetReturnsUncommittedChangesTest
  - PutBuffersUntilCommitTest
  - DeleteTracksOldValueTest
  - MultipleOperationsAtomicTest
  - StateTransitionsCorrectlyTest
  ```

- [ ] **Create `WalJournalTests.cs`**
  ```
  - RecoveryReplaysCommittedTest
  - RecoveryIgnoresUncommittedTest
  - EncryptionRoundTripTest
  - CheckpointTruncatesTest
  - AutoCheckpointOnThresholdTest
  - ConcurrentWritesSerializedTest
  ```

- [ ] **Create `RollbackJournalTests.cs`**
  ```
  - RecoveryRestoresOriginalValuesTest
  - CommitDeletesJournalFileTest
  - EncryptionSupportTest
  - MultipleTransactionFilesTest
  ```

### 3.3 Integration Tests

- [ ] **Create `TransactionalStoreIntegrationTests.cs`**
  ```
  - CrashRecoveryWithWalTest
  - CrashRecoveryWithRollbackJournalTest
  - TransactionalBTreeStoreTest
  - TransactionalLsmStoreTest
  - EncryptedTransactionalStoreTest
  ```

---

## Phase 4: API Improvements

### 4.1 Options Pattern

- [ ] **Create `TransactionalStoreOptions.cs`**
  ```csharp
  public class TransactionalStoreOptions
  {
      public TransactionMode Mode { get; set; } = TransactionMode.Wal;
      public IBlockEncryptor? Encryptor { get; set; }
      public bool EnableFileLocking { get; set; } = true;
      public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);
      public long WalCheckpointThreshold { get; set; } = 1024 * 1024; // 1MB
  }
  ```

### 4.2 Fluent Builder API

- [ ] **Create `TransactionalStoreBuilder.cs`**
  ```csharp
  public class TransactionalStoreBuilder
  {
      public TransactionalStoreBuilder WithStore(IKeyValueStore store);
      public TransactionalStoreBuilder WithWalJournal(string path);
      public TransactionalStoreBuilder WithRollbackJournal(string path);
      public TransactionalStoreBuilder WithEncryption(IBlockEncryptor encryptor);
      public TransactionalStoreBuilder WithFileLocking(string dbPath);
      public TransactionalStoreBuilder WithTimeout(TimeSpan timeout);
      public ITransactionalStore Build();
  }
  
  // Usage:
  var store = new TransactionalStoreBuilder()
      .WithStore(new BTreeStore("data.db"))
      .WithWalJournal("data.wal")
      .WithFileLocking("data.db")
      .WithTimeout(TimeSpan.FromSeconds(10))
      .Build();
  ```

### 4.3 Convenience Extensions

- [ ] **Create `KeyValueStoreExtensions.cs`**
  ```csharp
  public static class KeyValueStoreExtensions
  {
      // String helpers
      public static void Put(this IKeyValueStore store, string key, string value);
      public static string? GetString(this IKeyValueStore store, string key);
      
      // JSON helpers (optional)
      public static void PutJson<T>(this IKeyValueStore store, string key, T value);
      public static T? GetJson<T>(this IKeyValueStore store, string key);
      
      // Transactional wrapper
      public static ITransactionalStore AsTransactional(
          this IKeyValueStore store, 
          TransactionalStoreOptions? options = null);
  }
  ```

---

## Phase 5: Documentation

- [ ] **Update README.md** with transaction examples
- [ ] **Create ARCHITECTURE.md** describing the overall design
- [ ] **Add XML documentation** to public APIs
- [ ] **Create sample project** demonstrating typical usage

---

## Phase 6: Performance Optimizations (Future)

- [ ] **Batch operations support**
  ```csharp
  using var batch = store.CreateBatch();
  batch.Put(key1, value1);
  batch.Put(key2, value2);
  batch.Commit(); // Single disk write
  ```

- [ ] **Read-only transactions** (no write lock needed)
  ```csharp
  using var tx = store.BeginReadOnlyTransaction();
  var snapshot = tx.Scan(null, null).ToList(); // Consistent snapshot
  ```

- [ ] **Optimistic concurrency** with version stamps

---

## Progress Tracking

| Phase | Items | Completed | Progress |
|-------|-------|-----------|----------|
| Phase 1 | 4 | 4 | ? 100% |
| Phase 2 | 4 | 0 | 0% |
| Phase 3 | 8 | 0 | 0% |
| Phase 4 | 3 | 0 | 0% |
| Phase 5 | 4 | 0 | 0% |
| Phase 6 | 3 | 0 | 0% |
| **Total** | **26** | **4** | **15%** |

---

## Notes

### ????????????? ????????

1. **Interface-first**: ??? ????????? ?????????? ????? ??????????
2. **Composition over inheritance**: Builder/Options ?????? ????????
3. **Explicit configuration**: ??? ?????, ??? ????????????? ????
4. **Testability**: ??? ??????????? ?????????????

### ?????????????

- LSM Store ????? ???? WAL ??? MemTable durability
- Transaction WAL ????? ??? ????????? multi-key ????????
- ??? WAL ????? ?????????????? (?????? ????)

### ??????????

1. ? **Completed**: Phase 1 - ????????? ???????????
2. ?? **In Progress**: Phase 2-3 - ?????????? ? ?????
3. ?? **Planned**: Phase 4-6 - ????????? API ? ????????????

---

## Changelog

### 2024-12-19
- ? Fixed `_ownsStore` public field
- ? Deleted duplicate `TransactionLog.cs`
- ? Fixed blocking async dispose in `Transaction`
- ? Unified `DatabaseLock` sync/async mechanism
