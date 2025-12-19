# Database Core Roadmap

## ??????? ??????: Phase 2 ???????? ?

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

## Phase 2: WAL Unification ?

### 2.1 Shared Infrastructure

- [x] **Extract common CRC32 calculation to shared utility**
  - ?????? `OutWit.Database.Core/Utils/Crc32.cs`
  - ???????????????? ?????????? ? lookup table

- [x] **Create unified IWriteAheadLog interface**
  - ?????? `OutWit.Database.Core/Interfaces/IWriteAheadLog.cs`
  - ???????? `IWalReplayVisitor`, `SimpleWalReplayVisitor`, `TransactionalWalReplayVisitor`

- [x] **Create WriteAheadLogBase base class**
  - ?????? `OutWit.Database.Core/Wal/WriteAheadLogBase.cs`
  - ????? ??????: ????, ?????????, ??????????, ?????????????
  - ????????? ?????? ???????? ????????? (12 ??? 16 ????)

- [x] **Create unified WriteAheadLog implementation**
  - ?????? `OutWit.Database.Core/Wal/WriteAheadLog.cs`
  - ???????????? ??????????, CRC32, ??????????, ArrayPool
  - ??????????? ?? `WriteAheadLogBase`

- [x] **Create WalTransactionJournal adapter**
  - ?????? `OutWit.Database.Core/Wal/WalTransactionJournal.cs`
  - ??????? IWriteAheadLog ? ITransactionJournal

- [x] **Refactor LSM WAL to use base class**
  - ???????? `OutWit.Database.Core/LSM/WriteAheadLog.cs`
  - ??????????? ?? `WriteAheadLogBase`
  - ????????? ????????????? ? ????????????? ??????? (12-byte header)

- [x] **Delete old WalJournal**
  - ?????? `OutWit.Database.Core/Transactions/WalJournal.cs`
  - ??????? ?? `WalTransactionJournal`

---

## Phase 3: Tests for Concurrency & Transactions

### 3.1 Concurrency Tests

- [ ] **Create `LockManagerTests.cs`**
- [ ] **Create `DatabaseLockTests.cs`**
- [ ] **Create `FileLockTests.cs`**

### 3.2 Transaction Tests

- [ ] **Create `TransactionalStoreTests.cs`**
- [ ] **Create `TransactionTests.cs`**
- [ ] **Create `WriteAheadLogTests.cs`** (for unified WAL)
- [ ] **Create `RollbackJournalTests.cs`**

### 3.3 Integration Tests

- [ ] **Create `TransactionalStoreIntegrationTests.cs`**

---

## Phase 4: API Improvements

### 4.1 Options Pattern

- [ ] **Create `TransactionalStoreOptions.cs`**

### 4.2 Fluent Builder API

- [ ] **Create `TransactionalStoreBuilder.cs`**

### 4.3 Convenience Extensions

- [ ] **Create `KeyValueStoreExtensions.cs`**

---

## Phase 5: Documentation

- [ ] **Update README.md** with transaction examples
- [ ] **Create ARCHITECTURE.md** describing the overall design
- [ ] **Add XML documentation** to public APIs
- [ ] **Create sample project** demonstrating typical usage

---

## Phase 6: Performance Optimizations (Future)

- [ ] **Batch operations support**
- [ ] **Read-only transactions**
- [ ] **Optimistic concurrency**

---

## Progress Tracking

| Phase | Items | Completed | Progress |
|-------|-------|-----------|----------|
| Phase 1 | 4 | 4 | ? 100% |
| Phase 2 | 7 | 7 | ? 100% |
| Phase 3 | 8 | 0 | 0% |
| Phase 4 | 3 | 0 | 0% |
| Phase 5 | 4 | 0 | 0% |
| Phase 6 | 3 | 0 | 0% |
| **Total** | **29** | **11** | **38%** |

---

## WAL Architecture

### Class Hierarchy
```
WriteAheadLogBase (abstract)
??? OutWit.Database.Core.Wal.WriteAheadLog      # Transactional (16-byte header)
??? OutWit.Database.Core.LSM.WriteAheadLog      # LSM-specific (12-byte header)

IWriteAheadLog (interface)
??? Wal.WriteAheadLog
??? LSM.WriteAheadLog

ITransactionJournal (interface)
??? WalTransactionJournal (wraps IWriteAheadLog)
??? RollbackJournal
```

### File Structure
```
OutWit.Database.Core/
??? Utils/
?   ??? Crc32.cs                 # Shared CRC32 utility
??? Interfaces/
?   ??? IWriteAheadLog.cs        # WAL interface + visitors
?   ??? ITransactionJournal.cs   # Transaction journal interface
??? Wal/
?   ??? WriteAheadLogBase.cs     # Base class with common logic
?   ??? WriteAheadLog.cs         # Transactional WAL
?   ??? WalTransactionJournal.cs # ITransactionJournal adapter
??? LSM/
?   ??? WriteAheadLog.cs         # LSM-specific WAL
??? Transactions/
    ??? RollbackJournal.cs       # Rollback journal (keeps old values)
```

### Usage Examples

```csharp
// For LSM (non-transactional):
var lsmWal = new OutWit.Database.Core.LSM.WriteAheadLog("data.wal");
lsmWal.AppendPut(key, value);
lsmWal.Replay(new SimpleWalReplayVisitor(onPut, onDelete));

// For BTree with transactions:
var journal = new WalTransactionJournal("tx.wal", encryptor: null);
var store = new TransactionalStore(btree, journal, new LockManager("data.db"));

using var tx = store.BeginTransaction();
tx.Put("key"u8, "value"u8);
tx.Commit();
```

---

## WAL Feature Comparison

| Feature | LSM WAL | Transactional WAL |
|---------|---------|-------------------|
| Base class | WriteAheadLogBase | WriteAheadLogBase |
| Header size | 12 bytes | 16 bytes |
| Has version | No | Yes (v2) |
| Transactions | No | Yes |
| CRC32 | ? | ? |
| ArrayPool | ? | ? |
| Encryption | ? | ? |

---

## Changelog

### 2024-12-19
- ? Phase 1 completed
- ? Phase 2 completed
- ? Created `Utils/Crc32.cs`
- ? Created `Interfaces/IWriteAheadLog.cs` with visitors
- ? Created `Wal/WriteAheadLogBase.cs` - base class
- ? Created `Wal/WriteAheadLog.cs` (transactional)
- ? Created `Wal/WalTransactionJournal.cs`
- ? Refactored LSM WAL to inherit from base class
- ? Deleted old `WalJournal.cs`
