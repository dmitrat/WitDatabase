# Phase 12 - what a database remembers about how it was made

Phase 11 established the rule that every setting is either honoured or refused at `Open`, and proved it
for the connection string that *creates* a database. This phase asks the question a consumer meets far
more often: a database is created once, with a carefully written connection string, and opened a
thousand times afterwards - by another process, another deployment, another version of the application.
**How much of that connection string does the file remember?**

Until 12.2.0 the answer was none of it.

## 1. Instrument F - a database created by one configuration, opened by nothing

`ConfigurationRestoreTests`. Create with `Data Source=X;Setting=V`, run a workload, close. Then open the
same file twice: once with the same connection string - the **reference**, what the engine looks like
when the setting is spelled out - and once with `Data Source=X` and nothing else, the **bare** reopen,
which is what a consumer gets who trusted the file to remember. Compare the structural fingerprints,
and read the rows back on both sides.

The fingerprint is the census's, extracted into `EngineFingerprint` so that the two instruments share
one walk rather than two sets of blind spots. The census's own controls policed the extraction.

### 1.1 What it measured, before anything was changed

| verdict | count |
|---|---|
| **Restored** | **0** |
| Lost - opens, answers correctly, and is a different engine | 14 |
| Refused | 5 |

Nothing was recovered from the file. Every setting was either silently replaced by a default or
refused.

The five refusals are worth separating, because two of them were not refusals of the kind the phase-11
rule means:

- `MVCC=false` and `Transactions=false` - refused legibly, by the check 12.1.0 added. Correct.
- `Encryption=aes-gcm` - refused; the header is inside the encrypted page, so nothing can be read.
- `PageSize=16384` - refused with *"Page size mismatch: file has 16384 bytes, storage expects 4096"*.
  The file records its own page size at a fixed offset that does not depend on the page size. This was
  refusing something it could simply have read.
- `Store=lsm` - refused with **`Access to the path ... is denied`**. A raw operating-system error,
  because a bare connection string built a B+Tree store and handed it a path that is a directory. Not a
  refusal with an explanation; an accident that happened to stop.

### 1.2 The instrument was wrong before its subject, for the tenth time

Its first run reported `SyncWrites=false` as **RESTORED**. That is a truth-shaped non-fact: `SyncWrites`
**defaults to false**, so the case created the default, and the bare reopen agreed with it for free. A
setting that reaches nothing would report RESTORED the same way.

The fix is a control rather than a corrected case. Every case that comes out identical is now checked
against two more databases created with the case's baseline settings alone; if the reference cannot be
told from a default-configured database, the case is reported **VACUOUS** and a subject in that state
fails the run. And because a check nobody has watched fire is a claim rather than a control, two cases
are deliberately created with default values and asserted to be reported vacuous.

## 2. The serious one: an LSM database has no header at all

`WitDatabase.Open(path)` on an LSM database created with the default configuration **opened without
complaint and reported every table as missing**, with the rows intact underneath.

This is precisely the shape 12.0.0 fixed for the B+Tree store, and it survived because that fix was a
comparison against a header the LSM store does not have. `StorageDetector.DetectDirectory` fills in no
feature flags, so `HasTransactions` came back as the default of a field nobody had set - `false` - and
`Open` faithfully built what it was told: no transaction layer over a store whose every value sits under
a versioned MVCC key.

Attributed in the same test, which is what separates invisibility from loss:

| route | answer |
|---|---|
| `WitDatabase.Open(directory)` | `Table 'Restore' not found` |
| the same directory with `WithMvcc()` | all 8 rows |

A consumer reads "table not found" as "this database is empty", and the natural next step - create the
schema - writes over one that was perfectly intact.

## 3. What was decided

Dmitry, 2026-08-04: **restore structure and performance, never safety.**

| restored from the file | not restored |
|---|---|
| `Store`, `PageSize`, `Encryption` (flags), `Transactions`, `MVCC`, `Journal` | `Synchronous Commit` |
| `Cache`, `CacheSize` | `FileLocking` |
| every LSM setting a connection string can select | `Isolation Level` |

The three on the right are refused restoration for one reason each and they are not the same reason.
`Synchronous Commit` and `FileLocking` would let a **file** make a database quietly less durable, or
less exclusive, than the defaults promise, for a caller who said nothing about either. `Isolation Level`
is a property of a session rather than of the data.

**A connection string always wins over the file.** Restoration only fills in what the caller did not
name, and naming a conflicting transaction model is still refused.

## 4. What was built

### 4.1 The header grew, and the two fields that were declared and dropped are written

`ProviderMetadata` declared `CacheProviderKey` and `JournalProviderKey` and carried them with the
comment *"Not persisted - always uses default on reopen"*; 12 bytes were reserved for them, which is not
enough for two 16-byte keys and a cache size. The metadata region is 80 bytes now and the header 128,
from 100. Page 0 is cleared and rewritten in full on every header flush and holds nothing else, and the
smallest page a database can have is 512 bytes, so the room was already there.

Both directions stay readable, and that is asserted rather than assumed:

- a file written before 12.2.0 carries zeros from byte 88 on, which reads as **nothing recorded** and
  falls back to the defaults it always used;
- a build older than 12.2.0 reads the first 100 bytes of a new file and sees exactly what it saw before.

The format version's **minor** is bumped to 1.1. The major is what an older build refuses on, and it is
unchanged.

Provider keys are stored as text rather than as an enumeration, because a third party can register a
cache or journal provider under any key - `ThirdPartyProviderTests` drives a real database through one -
and an id would quietly close the registry.

### 4.2 The LSM directory gets a sidecar

`LsmDirectoryMetadata`, written as `provider.meta` beside the SSTables. It carries the same
`ProviderMetadata` block the database header carries - one encoding of those fields, not two that can
drift - followed by the LSM options, which have nowhere else to live.

Written under a temporary name and moved into place, which is atomic on NTFS and POSIX: the same
reasoning as the SSTables next to it, so a crash mid-write cannot leave a half-written configuration to
be read as a real one. Written only when the directory does not already have one, for the same reason
the database header is only filled in by `InitializeNewDatabase`: reopening under other settings must
not rewrite what the file says it was made with.

A directory without the sidecar reads as null and the caller falls back to what it did before, so a
database written by an earlier version still opens.

### 4.3 Restoration, and how the builder knows what the caller said

The hard half is not reading the file - it is distinguishing *"the caller chose MVCC"* from *"the caller
said nothing and MVCC is the default"*. The two produce an identical `Options`, and the first must be
refused where the second is restored.

`WitDatabaseBuilderOptions` records which settings were **named**, and `RestoreStoredConfiguration` is
**off by default**: a builder written by hand states its configuration in full, so letting a file
override any of it would change what existing code does. It is switched on by the two routes where the
caller is not spelling out a configuration - a connection string, and `WitDatabase.Open`.

The connection-string layer marks exactly the keywords that appear in the string, and it has to, because
the `Configure*` methods call the same builder methods either way: `ConfigureTransactions` calls
`WithMvcc` for the default as well as for `MVCC=true`.

Order matters twice. The **store** is restored first, because it decides which of the rest apply and
because it turns a path that is a directory into an LSM database rather than a file the operating system
refuses to open. The **transaction model** is restored before the journal, because a journal is only
legal without MVCC, and restoring one into an MVCC configuration would produce a combination the
validator refuses - a worse answer than the one being replaced.

The transaction model is reconciled at a second seam as well, in `ValidateStoredConfiguration`, and that
one is not redundant: it is the only place that works for an **encrypted** database, where nothing can
be read from the file before the store is built, and the transactional layer is built after it.

### 4.4 Two suppressed markers close, and they were the same defect

`CrossCuttingCoreTests` carried two `[Ignore]`d tests, confirmed 2026-07-27 and untouched since:

- *"an encrypted database created with MVCC comes back without it, silently"*;
- *"and it costs data: the row written before the reopen comes back NULL"*.

Their reasoning was right about the mechanism and wrong about what followed from it.
`WitDatabase.Open(path, password)` cannot read the header **from the file**, because it is inside the
encrypted page - and that was taken to mean the configuration was unknowable, so `WithTransactions()`
was called unconditionally and MVCC was never restored. But the store decrypts the header as soon as it
is built, and the transactional layer is built **after** the store. Reconciling the model there is late
enough to know and early enough to matter.

This is the first movement in the ledger for three sessions, and it was found by looking for what the
new seam already covered rather than by planning to close it.

### 4.5 `WitDatabase.Open` stops guessing

It now decides only the shape of the path - a directory is an LSM database, a file is a paged one - and
lets the builder read the rest. The code it replaced configured the transaction model from a detection
result that, for a directory, was a guess dressed as a fact.

## 5. What the instrument says now

| verdict | before | after |
|---|---|---|
| Restored | 0 | **17** |
| Lost | 14 | 3 |
| Refused | 5 | 1 |

The three still lost are `Synchronous Commit`, `FileLocking` and `Isolation Level`, which are the three
the decision excludes - and they are **asserted** to be lost rather than merely observed, so the rule is
measured on every run instead of stated once. The one refusal is an encrypted database opened without
its password, which is correct.

`WitDatabase.Open` returns the rows intact for all seven LSM cases, where it reported the table missing
for all seven before.

### 5.1 Two tests had to be re-pointed, and both were superseded rather than wrong

- **The must-be-lost control was `Cache=lru`**, on the grounds that it was documented as never
  persisted. It is persisted now, so the control moved to a setting that is deliberately not restored.
  A control whose premise has been fixed is not a control.
- **`ADifferentTransactionModelIsRefusedAtOpenTest` expected a refusal in all four directions.**
  Refusing was the best answer available in 12.0.0, because nothing could tell a caller who had chosen
  `MVCC=true` from one who had merely not mentioned it. Two of the four cases now open correctly, and
  the test is split in two: naming a conflicting model is still refused
  (`ANamedTransactionModelThatConflictsIsRefusedAtOpenTest`), and not naming one is restored
  (`AnUnnamedTransactionModelIsRestoredFromTheDatabaseTest`). Either alone would let the other regress
  unnoticed.

### 5.2 Restoring the store exposed a real defect, which is what instruments are for

`ConfigurationMismatchTests` went red on `lsm -> MVCC=false`: **OpensAndDataIsGone**. Before the change
that pair was refused - not by any check, but because opening an LSM directory as a file fails in the
operating system. Restoring the store removed the accident and left the real gap visible: the
transaction-model refusal reads the metadata the *built store* exposes, and the LSM store exposes none,
so it had never applied to LSM at all.

The same question is asked now where the answer exists for both stores. Four grid cells went the other
way at the same time - `lsm -> default`, `-> pagesize`, `-> cache-lru`, `-> cachesize` were accidental
refusals and are now **Correct**.

### 5.3 A fourth handle leak of the same shape

With the store restored, `lsm -> aes` reached the write-ahead log's header check, which refuses with
*"WAL is not encrypted but encryptor was provided"* - **from a constructor**, after the file was opened.
A constructor that throws leaves nothing to call `Dispose` on, so the creator's own configuration then
met *"the process cannot access the file"*.

This is the fourth construct-then-fail-then-leak in this codebase, after the in-memory store's storage,
the dropped journal, and the store whose build failed. The grid had been **printing** survival on every
cell and asserting it on none; it asserts it now, so the next one fails a run rather than a paragraph.

## 6. The tails, studied and decided

Carried in from phase 11 and settled here rather than re-copied.

### 6.1 Isolation Level - the record is confirmed, by execution

Phase 6 recorded *"reported and applied by nothing"* and it had been handed forward for six phases
without a measurement. It has one now, and the record is accurate.

| level | sees another connection's commit - scan | seek |
|---|---|---|
| `ReadCommitted` | True | True |
| `RepeatableRead` | True | True |
| `Serializable` | True | True |
| `Snapshot` | True | True |

`ReadCommitted` is the control - it **must** see the row, and it does, so the probe can distinguish the
two behaviours; there is only one behaviour to distinguish.

**"Reaches nothing" would be the wrong description**, and the seek column is why it is measured.
`MvccTransaction.Get` does switch on the level, and the three snapshot levels take a different branch
from `ReadCommitted`. The level reaches the transaction; the statement path is not what reads through
it. Pinned with its inversion instructions in `IsolationLevelIsAppliedTests`.

### 6.2 The double write - measured, and the blocker is deeper than recorded

**Measured first, by counting rather than timing.** `CommitWriteCountTests` puts a counting store under
the MVCC layer and counts what reaches it:

| 50 rows | store writes |
|---|---|
| autocommit (control) | **50** - exactly one per row |
| one transaction | **101** - 2.02x |

A count does not move with the machine, the page cache or the load, and the control is exact, so this is
the claim itself rather than an inference from a duration. Pinned, with the inversion written into the
assertion: it becomes `ROWS + 1` when the fix lands, the extra one being the max-timestamp record.

**The recorded blocker was that one parameter does two jobs**, and that much is right:
`MarkPreviousVersionDeleted(key, timestamp, transactionId)` uses `transactionId` for the **ownership
rule** - only touch my own uncommitted versions, or committed ones - while the same value stamps the new
record as uncommitted. Passing 0 to install a record already committed would move that rule as a side
effect.

**Implementing it found a second one, which is why it is not done here.** If versions are installed
already committed, a rollback can no longer recognise them by the transaction id they carry, so it has
to work from the per-transaction write set - **and that set is not only the versions the transaction
created.** `MarkPreviousVersionDeleted` adds the *earlier committed version's* key to it as well, so
that a transaction which overwrites its own row can find it again at commit without a scan. A rollback
that deleted everything in that set would delete the previous committed version outright: not a wrong
marker, **data loss**.

So the same conflation exists twice, one level apart - in the parameter and in the write set - and the
fix needs the write set split into "versions I created" and "versions I marked deleted" before the
marker can be moved. Today the previous version survives a rollback because the filter skips it and its
delete stamp carries a timestamp that was never published.

`CommitWriteCountTests` carries the two guards this needs, green today and red under a naive fix: a
rolled-back overwrite leaves the original value, and an uncommitted write is invisible to another
transaction.

**This is the "implement your hypothesis to refute it" rule paying for itself again.** The mechanism
survived a careful read of the code and did not survive being built.

### 6.3 Parked, with reasons

- **The histogram.** It fixes skew in an estimate nothing consumes: a scan costs `rows x 1.0` and an
  index range `estimated x 0.5`, and the estimate can never exceed the row count, so the index wins
  arithmetically. A histogram also costs writes to maintain. It becomes worth building after a cost
  model that could choose, not before.
- **The cost model** - its own phase; it changes query plans, which is a performance risk that needs its
  own measurement.
- **The asynchronous statement path** - its own phase. `WitSqlEngine` has only `Execute`/`Query`.
- **LSM autocommit ~5x**, undiagnosed; **one shared WAL across index key spaces**; **compaction's share
  of wall clock**; **`[BenchmarkCategory]` on 113 methods**; **mutation testing never proven to
  complete** (needs a manual dispatch); **`FileLocking=false` on Linux**, which is a decision rather
  than a defect.

## 6a. The version, and the one thing this breaks

**12.2.0 - a minor that carries a narrow break, taken deliberately.**

The project's test is *can an application that worked on the previous version fail on this one without
changing a line?* Here it answers **yes**, in exactly one place, and it is proved by execution rather
than argued: `WitDatabase.Open` used to read `FileLocking` out of the header and call
`WithoutFileLocking()` when the flag was clear, so a database created with `FileLocking=false` reopened
without the guard. It does not any more - safety settings are not restored - so the exclusive lock is
taken and **a second engine over that database is refused**. `FileLockingIsNotRestoredTests` asserts
both halves: the data still comes back, and the second `Open` throws.

Dmitry took the minor knowingly, and the reasons are on the record rather than implied: the break is
Linux-only in practice, it needs `FileLocking=false` written out explicitly, `WitSQL.md` § 15.0
documents that setting as *disabling the guard*, and the hole it closes is one this project had already
named - `FileLocking=false` admitting two engines on Linux was recorded as a gap in the intent, not an
accepted trade-off.

Everything else in the phase is a fix or an addition. The file format stays readable in both
directions, and no public API was removed.

## 7. Ledger

**45 suppressed entries (31 `[Ignore(…)]` + 14 `Ignore =`) plus 2 `[Explicit]`**, counted with the
commands on this branch. It was 47 + 2, and had not moved for three sessions. The two that closed are
the encrypted-MVCC pair in § 4.4, and neither was planned work - they were found by asking which
recorded defects the new seam already covered.

## 8. Verification

Six suites, the CI filter (`Category!=Performance&Category!=Conformance&Category!=Oracle`), on this
branch:

| suite | passed | failed |
|---|---|---|
| Core | 2270 | 0 |
| AdoNet | 1016 | 0 |
| Engine | 2247 | 0 |
| EntityFramework | 544 | 0 |
| Parser | 797 | 0 |
| Core.IndexedDb | 153 | 0 |

**One failure along the way was a load artifact and is worth recording as one.** `CommitCostProbeTests`
reported *"16x the data, 28.8x the commit"* against a bound of 4.0, while two other test suites and a
build were running on the same machine. Re-measured on a quiet machine: **0.9x**, and the probe's own
per-round output showed the outlier it had averaged in (`[3.32, 3.77, 8.33]`). The bound was not
touched. A timing test that fails under load has failed about the load.
