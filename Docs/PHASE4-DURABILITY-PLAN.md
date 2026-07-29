# Phase 4 — durability and crash recovery

Written 2026-07-29, before a line of code is changed. Starting point: `main` at `06febbe` (the 3.0.1
release merge), tree clean, phases 0–3 closed, 2.1.0–3.0.1 published.

**Verified rather than carried forward** — every row below was re-measured today, because a record of
a past state is a claim, not a closed case:

| Check | Measured |
|---|---|
| Working tree | clean (one untracked `WitDatabase.code-workspace`) |
| Release build, whole solution | **0 errors**, 714 warnings, 2 m 28 s |
| `OutWit.Database.Parser` in `OutWit.slnx` and in three `ProjectReference`s | present — the phase-3 IDE damage has not recurred |
| Versions in the seven packable `.csproj` | all **3.0.1** |
| Published, all seven at 3.0.1 | yes; `AdoNet 3.0.1` depends on `OutWit.Database 3.0.1` |
| `AdoNet 3.0.0` on nuget.org | **still listed**, and it depends on `OutWit.Database 2.4.0`. Unlisting is a manual step |
| `OutWit.Database` / `…EntityFramework` at 3.0.0 | never published — 3.0.0 shipped **five** of seven packages, not six |

**Ledger, counted rather than trusted.** `grep -rho "\[Ignore" --include=*.cs Sources/ | wc -l` → **77**,
matching what phase 3 recorded, but the decomposition is worth stating once so the number stops being
re-derived:

- **73** real `[Ignore(...)]` attributes;
- **4** prose mentions of the marker (three XML-doc paragraphs under `AuditVerification/`, one comment
  in `WitDbConnectionScenariosTests.cs:169`) — counted by the command, not markers;
- **13** `[TestCase(..., Ignore = …)]` properties the command does not see (7 reserved words in
  `ParserFindingsTests`, 4 table sources in `DropInGapsEngineTests`, 2 in `CrossCuttingAdoNetTests`);
- **3** `[Explicit]`.

**So the ledger is `73 + 13` suppressed test entries, plus 3 `[Explicit]`.** Phase 4 reports both
numbers; the single "77" is the raw command output and happens to land there only because the four
false positives and four of the thirteen misses are different sets.

**Measured baseline, this machine, before any change** (`Category!=Performance&Category!=Conformance&Category!=Oracle`):

| Suite | Result |
|---|---|
| `OutWit.Database.Core.Tests` (net9.0 and net10.0, identical) | **2213 passed, 26 skipped, 2239 total** |
| `OutWit.Database.Tests` (net9.0 and net10.0, identical) | **1941 passed, 32 skipped, 1973 total** |

Phase 3 recorded 1904/28/1932 for the engine suite after PR 2; the rise to 1941/32 is the tests its
later PRs added. Both frameworks agree exactly, which is worth noting because a durability regression
that appears on one framework only would otherwise look like noise.

---

## 1. What phase 4 is fixing, and what is missing to prove any of it

Five subjects carried in from workstream B, plus statement atomicity carried in from phase 1:

1. WAL truncation after a **partial** replay — committed transactions behind a bad record vanish, silently.
2. **Savepoint replay** — a write rolled back before commit is resurrected by recovery.
3. **Rowid counters** — reused after a crash, handing a new row the identity of a live one.
4. **SSTable fsync** — the WAL is truncated while the SSTable that replaced it is still in the OS cache.
5. **Compaction manifest** — a crash between publishing the output and deleting the inputs has no record to recover from.
6. **Statement atomicity** — a multi-row DML that fails part-way leaves the earlier rows written.

Two of them cannot be reached at all with the current test surface, which is why the instruments come
first rather than the fixes.

### 1.1 The mechanisms, re-measured today

Each of these was read out of the current `main`, and each is a fact about code, not yet a proof of
behaviour — the distinction that rule 2 exists to enforce.

- **Eager journalling.** `Transaction.Put` calls `m_journal?.LogPut(...)` at write time
  ([Transaction.cs:148](../Sources/Core/OutWit.Database.Core/Transactions/Transaction.cs#L148)), while
  `RollbackToSavepoint` only restores the in-memory change set
  ([Transaction.cs:310](../Sources/Core/OutWit.Database.Core/Transactions/Transaction.cs#L310)). The
  journal keeps a record of a write the transaction discarded. That is subject 2, stated as code.
- **Unconditional checkpoint after replay.**
  [TransactionalStore.Recover](../Sources/Core/OutWit.Database.Core/Transactions/TransactionalStore.cs#L403)
  is four lines: replay, flush if anything came back, then `m_journal?.Checkpoint()` — and
  `Checkpoint()` is `Truncate()`, i.e. `SetLength(0)`
  ([WriteAheadLogBase.cs:145](../Sources/Core/OutWit.Database.Core/Wal/WriteAheadLogBase.cs#L145)).
  There is no path between them that can say a replay stopped early. That is subject 1.
- **Rowid counters are written per allocation and flushed almost never.**
  `SchemaCatalog.GetNextRowId` calls `SaveTableRowId`
  ([SchemaCatalog.cs:158](../Sources/Engine/OutWit.Database/Schema/SchemaCatalog.cs#L158)), so the
  counter does reach the store. But `PersistRowIdsToStore` has exactly **two** callers, both inside
  `WitSqlEngine.Commit`
  ([WitSqlEngine.Transactions.cs:66](../Sources/Engine/OutWit.Database/Engine/WitSqlEngine.Transactions.cs#L66)),
  inside a `try { } catch { }` that swallows every failure by design; and the only `Flush` of the store
  is in `WitSqlEngine.Dispose`
  ([WitSqlEngine.cs:304](../Sources/Engine/OutWit.Database/Engine/WitSqlEngine.cs#L304)). **In
  autocommit `Commit()` returns immediately because there is no current transaction**, so that
  metadata path never runs at all. Subject 3, and note it is entangled with subject 6: an implicit
  per-statement transaction is what would make it run.
- **The SSTable is published, then the WAL is dropped.** `FlushMemTableInternal` builds the SSTable
  ([StoreLsm.cs:577](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L577)), opens a reader
  over it, and calls `m_wal?.Truncate()`
  ([StoreLsm.cs:602](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L602)). `SSTableBuilder.Finish`
  ends at `m_writer.Flush()`
  ([SSTableBuilder.cs:184](../Sources/Core/OutWit.Database.Core/LSM/SSTableBuilder.cs#L184)) — a
  `BinaryWriter` flush into the `FileStream`, nothing more. Real fsync exists in exactly three places
  in the whole repository, all outside `Core/LSM/`:
  [StorageFile.cs:194](../Sources/Core/OutWit.Database.Core/Storage/StorageFile.cs#L194),
  [TransactionJournalFile.cs:78](../Sources/Core/OutWit.Database.Core/Transactions/TransactionJournalFile.cs#L78),
  [WriteAheadLogBase.cs:138](../Sources/Core/OutWit.Database.Core/Wal/WriteAheadLogBase.cs#L138).
  Subject 4.
- **`SyncWrites` syncs the WAL only.** It is consulted at
  [StoreLsm.cs:155](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L155) and
  [:195](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L195), both times as `m_wal?.Sync()`.
  Its own doc comment sells it as the durable mode ("10K writes with SyncWrites=true: ~10 seconds").
  **The option makes writes durable up to the next memtable flush and no further** — after which the
  WAL is truncated and the only copy is an unsynced SSTable. This is not a new finding so much as the
  consumer-facing statement of subject 4, and it is the one a user would notice.
- **Compaction has no manifest.** The output is written, then the inputs are deleted with
  `try { File.Delete(file); } catch { }` under the SSTable write lock
  ([StoreLsm.cs:540](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L540)), and recovery is
  `Directory.GetFiles(m_directory, "sst_*.sst")` ordered by name
  ([StoreLsm.cs:621](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L621)). Subject 5 — and
  the audit recorded its *consequence* as not reproduced, which phase 4 will re-test rather than
  inherit.

---

## 2. The prerequisite: two instruments, each carrying its own control

`Process.Start` appears **nowhere** in the test tree — there is no out-of-process capability to
extend, so both instruments are new.

### 2.1 Instrument A — the out-of-process crash runner

**Why it is unavoidable.** A file-backed engine opens its storage with `FileShare.None`, so a second
engine cannot be opened over a live file; and disposing the first engine is exactly the operation that
flushes the counters the test is trying to catch unflushed. The "media outlives the wrapper" trick
that settled the MVCC and LSM batches works at the store layer and cannot reach the engine's schema.

**Shape.** `Tools/OutWit.Database.CrashRunner` — a small non-packable console app, next to Studio.
It takes a scenario name, a database path and a seed, performs a scripted workload against a real
file-backed engine, reports progress on stdout, and either exits cleanly or waits to be killed. The
test side lives with the suite it belongs to, tagged `[Category("Crash")]` — **not excluded from CI**.
The exclusions in `ci.yml` are for tests that measure the machine (`Performance`) or that report on
something other than this code (`Conformance`, `Oracle`); crash tests are deterministic assertions
about our own behaviour and belong on every PR. The category exists so they can be run alone.

**Controls, and they are the point.** Three scenarios whose answers are known before the run:

| Control | Scenario | Expected | If it fails |
|---|---|---|---|
| **C1** | write N rows, dispose cleanly, reopen | every row and every counter present | the harness is broken, not the engine — stop and fix the harness |
| **C2** | write N rows inside an explicit transaction, commit, flush, **then** kill the process | every row present | the kill itself is destroying data, so nothing measured after it means anything |
| **C3** | write N rows in autocommit, kill with no flush | *recorded, not asserted* — this is the baseline cost of a kill on this platform | it calibrates every other result: a defect must lose **more**, or lose **differently**, than C3 |

C2 and C3 together are what make a red test attributable. Without C3 every crash scenario looks like a
defect; without C2 no crash scenario proves anything at all.

**Verdicts, not counts.** Each scenario returns a classified verdict —
`AllSurvived` / `LostAndReported` / `LostSilently` / `ResurrectedDiscardedWrite` / `RowidReused` /
`HarnessFailed` — and the tests assert on the verdict. A count of missing rows would go green the
moment one defect masked another, which is the failure mode `GrammarRoundTripTests` was built to avoid
in phase 3.

### 2.2 Instrument B — a modelled power cut at the storage seam, plus an fsync counter

**Why a process kill is not enough.** A clean `Process.Kill` leaves the OS free to write its page cache
back, so data that was never fsynced survives anyway and the test goes green on a real defect. The
honest answer that still runs in CI is to **model** the page cache rather than to fight the real one.

**Shape.** A decorator over `IStorage` that plays the part of the operating system: a write lands in a
shadow page map, `Flush()` / `FlushAsync()` promotes the shadow pages onto the inner store and
increments `FsyncCount`, and `PowerCut()` discards everything unpromoted and hands back a fresh
storage over the inner media. Losing the unsynced writes is then deterministic, not lucky.

**The LSM path needs a seam, and one seam unlocks two findings.** `SSTableBuilder` opens its own
`new FileStream(filePath, …)`
([SSTableBuilder.cs:73](../Sources/Core/OutWit.Database.Core/LSM/SSTableBuilder.cs#L73)), so there is
nothing to decorate. The proposal is a narrow internal factory in `OutWit.Database.Core`, defaulting
to exactly the current behaviour, reached from tests through
`<InternalsVisibleTo Include="OutWit.Database.Core.Tests" />` — the pattern the Parser project already
uses. It is a production change and it is scoped deliberately: **it also supplies the injection point
for the one `core-lsm` finding recorded as "mechanism only"** — a failed flush leaving
`m_immutableMemTable` populated forever ([StoreLsm.cs:550](../Sources/Core/OutWit.Database.Core/Stores/StoreLsm.cs#L550))
— which the audit could not verify because "the current `StoreLsm` surface offers no way to arrange
one".

**Controls.**

| Control | Scenario | Expected |
|---|---|---|
| **C4** | write through a path that *does* fsync (`StorageFile.Flush`), then cut | data survives |
| **C5** | the same write with the fsync suppressed, then cut | data is lost |
| **C6** | `FsyncCount` on the WAL with `SyncWrites=true` | greater than zero |

C4 and C5 are the pair: if C4 loses data the model is too aggressive, if C5 keeps it the model is not
modelling anything. C6 pins that the counter can see a real fsync at all, which is what makes a
**zero** count on the SSTable path evidence rather than an artefact.

### 2.3 What instrument B proves, and what it does not

It proves the code **does not ask for durability** at the point it claims to have achieved it, and that
under the modelled semantics the data is gone. It does **not** prove behaviour on real hardware: a disk
with a write-back cache, a filesystem with different ordering guarantees, or a `fsync` that lies can all
change the outcome in either direction. Both halves go into the finding text — the mechanism is settled
and the deployment consequence is stated as modelled, not as observed on metal.

A real power-cut rig (a VM whose power is dropped, or a Windows filter driver) would settle the second
half. It is **out of scope and recorded as such**: it costs days, cannot run in CI, and would not
change any fix. If it is ever built, it belongs beside the benchmark suite, not in the test run.

---

## 3. Subjects, in the order they will be worked

| # | Subject | Instrument | Current marker |
|---|---|---|---|
| 1 | WAL truncation after a partial replay | none — already reproduced | `CorruptWalRecordDoesNotSilentlyDiscardLaterTransactionsTest` |
| 2 | Savepoint rollback resurrected by replay | none — already reproduced | `WalReplayDoesNotResurrectRolledBackWritesTest` |
| 3 | Rowid counter reuse after a crash | **A** | none — recorded as a comment in `CoreDurabilityFindingsTests` |
| 4 | SSTable fsync, and what `SyncWrites` actually promises | **B** | none — recorded as a comment in `CoreLsmFindingsTests:138` |
| 5 | Compaction manifest | **B** (for the failure point) | `CrashedCompactionDoesNotResurrectDeletedRowsTest` — **active**, pinning two properties, consequence not reproduced |
| 6 | Statement atomicity → implicit per-statement transaction | none | recorded in phase 1 |

Subjects 1 and 2 are already proven by `[Ignore]`d tests that fail on unfixed code, so they can be
fixed the moment the instruments are not blocking them — but they are sequenced **after** the
instruments anyway, because the fixes change recovery and the instruments are how a regression in
recovery will be noticed.

`RollbackJournal` with a bare relative path (`core-durability`, third marker) is a one-line fix with a
test already written; it rides along with subject 1's PR rather than getting its own.

---

## 4. Statement atomicity — why it is here and not in phase 1

Recorded in phase 1 and deliberately carried: a multi-row DML that fails part-way leaves the earlier
rows written. Pre-validating every row before writing any is the **wrong** fix — intra-statement
uniqueness depends on the earlier rows already being present, so pre-validation would let one statement
insert two rows with the same key. The correct shape is a statement-scoped savepoint, and
`CreateSavepoint` needs an active transaction while autocommit opens none.

So the fix is an **implicit per-statement transaction**, and that is the same mechanism subject 3 needs:
today autocommit never runs `PersistRowIdsToStore` at all, because that code lives inside
`WitSqlEngine.Commit` and autocommit never calls it. One change addresses both, and it changes the
write path — which is why phase 5 measures after it, not before.

Two consequences to keep in view while building it: `WitSqlEngine.Commit` swallows metadata-persist
failures by design, and an implicit transaction would run that swallowing path per statement; and
subject 2's fix changes what a savepoint means to the journal, which a statement-scoped savepoint then
depends on. **Subject 6 lands after subject 2, never before.**

---

## 5. PR sequence

| PR | Subject | Production change |
|---|---|---|
| 0 | This plan, plus the engine-suite baseline | none — **merged as #38** |
| 1 | Instrument A — crash runner, C1–C3, verdict classification | none — **§6** |
| 2 | Instrument B — modelled cut, fsync counter, C4–C6 | none — **§7**; `WithStorage` already existed |
| 3 | WAL truncation on partial replay (+ the `RollbackJournal` relative path) | recovery — **§8**, and the same fix was needed in the LSM WAL |
| 4 | Savepoint replay | journal — **§9**, compensating records, no format change |
| 5 | Rowid counters | engine metadata — an MVCC namespace collision fixed in **§10**; the subject itself closed in **§13** |
| 6 | SSTable fsync **and the LSM file seam** | LSM — **§11** |
| 7 | Compaction crash window | LSM — **§12**, atomic publish rather than a manifest |
| 8 | Statement atomicity / implicit per-statement transaction | engine write path |

Each PR waits for green CI before the next starts. A release is cut when enough has accumulated;
whether it is 3.1.0 or 4.0.0 is decided by the rule the previous releases used — **major if it changes
an answer a previous release gave**, and several of these change what survives a crash.

---

## 6. PR 1 results — the out-of-process runner, measured 2026-07-29

`Tools/OutWit.Database.CrashRunner` plus `Sources/Engine/OutWit.Database.Tests/Durability/`.
**Ten tests: eight green, two marked — and the two marked are both real defects, one of which the
audit had recorded as impossible to reproduce.**

### 6.1 The controls held, and one of them changed the reading of everything else

| Control | Result |
|---|---|
| **C1** clean shutdown | 20 of 20 rows, last row id 20 |
| **C2** commit + flush, then kill | 20 of 20 rows |
| **C3** autocommit, no flush, then kill | **the database could not be reopened at all — `Table 'T' not found`** |
| ADO.NET clean close | 20 rows, and `COUNT(*)` agrees |
| unknown scenario | fails as a harness error, not as a lost database |

**C3 is the calibration and it is more severe than expected.** A hard kill with nothing flushed does
not lose *some* rows — it loses the schema too, because nothing has reached the file yet. The
operating system never got the data, so its write-back cache never came into it. That is the baseline
every crash result here is read against.

### 6.2 The instrument caught a defect in its own measurement first

The first run reported **"a committed transaction does not survive a process kill — 0 of 20 rows"**,
through the ADO.NET provider *and* at the engine level with MVCC. That reading was **wrong**, and the
attribution probe is what broke it: opening the crashed file underneath the MVCC layer found **24 raw
records** against 27 in a cleanly closed database of the same shape. The data was on the media.

The verification was measuring `SELECT COUNT(*)`, which the engine answers from a cached per-table
counter rather than from the rows. Switched to counting what `SELECT` returns, the same crash gives
**20 of 20**. Durability holds; the counter does not.

**Every crash verification in the fixture now scans rather than counts, and says why.** Asserting on a
proxy is the same mistake phase 3's acceptance-only oracle made — "does it parse" instead of "what
does it answer" — and it produced a false report of the most serious defect class there is.

### 6.3 Two defects, both proven by execution

**Row ids come back at zero, so the next insert takes an identity already in use.** After a commit and
a kill, 20 rows survived with ids up to 20 — and the next insert was handed **id 1**. The audit
recorded this as *not reproducible with the current surface*; it is reproducible, and the surface it
needed is this harness. It is also worse than the finding stated: the counter does not skip ahead, it
restarts, so every insert after the crash collides.

**`SELECT` and `SELECT COUNT(*)` disagree after a crash — 20 rows returned, 0 counted.** Not in the
audit's 104. Same root cause: `PersistRowCountsToStore` and `PersistRowIdsToStore` run *after* the
commit, outside the flush the commit performed, inside a `try { } catch { }` that swallows failures —
and in autocommit they never run at all. The rows reach the media; the numbers describing them do not.

The count is the one an application believes when it checks whether its data arrived.

### 6.4 Counts after PR 1

Engine suite **1941 → 1949 passed, 32 → 34 skipped**, both frameworks identical, whole solution green
under the CI filter. Ledger **73 → 75** `[Ignore]` attributes plus 13 `[TestCase(… Ignore =)]` —
**88 suppressed entries**, up two, and the rise is the honest kind: both new markers are defects that
were already there.

---

## 7. PR 2 results — the modelled power cut, measured 2026-07-29

`Sources/Core/OutWit.Database.Core.Tests/Durability/`. **Six tests, all green — four controls and two
pins**, and the pins are results worth having rather than defects.

### 7.1 No production change was needed after all

The plan assumed a seam would have to be cut into the storage path. It does not:
`WitDatabaseBuilder.WithStorage(IStorage)` already exists, so the model is a decorator a test can
supply, and PR 2 touches no shipped code at all.

### 7.2 The controls, and the model was wrong first

| Control | Claim |
|---|---|
| **C4** | a flushed write survives the cut |
| **C5** | an unflushed write does not — and the flushed one beside it still does |
| **C5b** | before the cut, a cached page is indistinguishable from a durable one |
| **C6** | the flush counter can see a real flush, so a **zero** elsewhere is evidence |

**C5 went red on the first run**, and it was the model that was wrong: after the cut the media had
never been extended, so reading the lost page threw `ArgumentOutOfRangeException` instead of showing
the page missing. That is faithful media behaviour and a badly posed control. It now asserts *values*
in both directions in one shape — page 0 flushed and intact, page 1 unflushed and gone, storage one
page long — rather than an exception type. **Three instruments in this project have now been wrong
before their subject was, and all three were caught by a control.**

### 7.3 What it says about the commit path

With `WithBTree().WithTransactions()`:

- **A commit asks for durability** — flush count goes 0 → 1 across the commit, and no pages are left
  at risk. Counted, not inferred, which is why a zero would have been unambiguous.
- **Committed data survives a modelled power cut** — 20 of 20, with 0 unflushed pages discarded.

Both are **pins**, not fixes: they state a property that currently holds, so a later change to the
write path cannot quietly remove it. That matters because phase 4 is about to change that path.

This also sharpens the crash runner's result. Instrument A showed a committed transaction surviving a
process kill, which is the weaker claim - the operating system is still running afterwards. The model
says the write was made durable rather than merely handed to something that would have died with the
machine.

### 7.4 The LSM seam is deferred to the PR that needs it

The plan bundled a seam for `Core/LSM/` into this PR, because `SSTableBuilder` opens its own
`FileStream` and nothing can be injected into it. That is still true and still needed — but building
it here would mean shipping a production change with no test pointing at it. It moves to **PR 6**,
where the SSTable fsync finding gives it a purpose, and it still carries the second reason to exist:
it is the injection point for the `core-lsm` finding recorded as "mechanism only".

### 7.5 A finding instrument A has already proven, waiting for its PR

C3 in the crash fixture is currently recorded rather than asserted: **an autocommit statement that
returned successfully is lost entirely by a crash, schema included.** Autocommit durability is below
the minimum-set line — PostgreSQL, SQL Server and SQLite all provide it — so this is a defect and not
a trade-off, and it is the same missing per-statement transaction that statement atomicity needs. It
becomes a marked test in the PR that fixes it rather than a marker opened now against no fix.

---

## 8. PR 3 results — the WAL stops lying about what it lost, 2026-07-29

Two markers closed, and the fix turned out to be needed in **two places rather than one**.

### 8.1 Proven red first, as the rule requires

Both `[Ignore]`d tests were unmarked and run against unfixed code before anything was changed:

- `CorruptWalRecordDoesNotSilentlyDiscardLaterTransactionsTest` — *"after corrupting one mid-log
  record: 2/5 transactions recovered, error reported: **none**"*, exactly as the audit recorded in
  2026-07.
- `RollbackJournalAcceptsABareRelativePathTest` — `ArgumentException: The value cannot be an empty
  string. (Parameter 'path')`.

### 8.2 The discriminator was already in the file

Reporting every early stop would turn an ordinary crash into an unopenable database: a **torn tail** —
the half-written record left by a crash during an append — is normal and must replay cleanly.

The WAL header already carries an **entry counter**, written on sync and restored on open. That
settles it exactly: a record in flight when the power went was never counted, so a short replay that
matches the header is recovery working; a replay that comes up short *against the header* is data
loss. No new format, no heuristic.

`Replay` now distinguishes the two and throws `WalReplayException` carrying replayed, expected and the
byte offset. `TransactionalStore.Recover` flushes the prefix it did apply, **skips the checkpoint** —
truncating would destroy the records behind the damage along with any chance of recovering them by
other means — and rethrows.

### 8.3 The same silence existed on a second path

The finding named `TransactionalStore.cs:403`. `Core/LSM/WriteAheadLog.cs` had the identical shape —
stop at the first record that fails verification, return the count as though the log ended there, and
let `StoreLsm` truncate on the next memtable flush. Proven separately (**3 of 8 entries replayed, no
error**) and fixed with the same discriminator.

Fixing only the named path would have repeated this project's own history: the 2.0.0 `DropTable`
change fixed the schema half of a defect, left the storage half, and its comment was believed for
months.

### 8.4 The control was wrong twice, and both times it mattered

The torn-tail control is what keeps the fix from being merely loud, and its first two constructions
were both unfaithful:

1. It synced eight records and then truncated the file. That is not a torn tail — those records were
   acknowledged, so losing them quietly is the *defect*. As written it would have pinned the bug as
   correct behaviour.
2. Rewritten to append a ninth record through the WAL and abandon it — but `Dispose` calls
   `UpdateHeader`, so the header counted the torn record too and the control went red **after** the
   fix, for a reason a power failure never produces.

It now appends raw bytes to the end of the file, leaving the header behind them, which is what a crash
mid-append actually leaves.

### 8.5 A test in the suite was pinning the defect

`WriteAheadLogTests.CorruptedEntryStopsReplayTest` asserted only `replayedCount < 2` — that replay
stops. Stopping is right; **stopping quietly is the defect**, and the test pinned the quiet half as
correct. It is now `CorruptedEntryStopsReplayAndReportsItTest` and asserts the report, its counts, and
that the corruption offset is actually inside the file rather than past its end.

Worth stating plainly: **the existing suite contained an assertion that a confirmed data-loss
behaviour was correct.** That is the same class of hole phase 0's mutation testing exists to find.

### 8.6 Counts after PR 3

Core suite **2219 → 2223 passed, 26 → 24 skipped** (two markers closed, two LSM tests added), both
frameworks identical. Whole solution green under the CI filter. Ledger **75 → 73** attributes plus 13
`[TestCase(… Ignore =)]` — **86 suppressed entries**.

---

## 9. PR 4 results — a rolled-back write stays rolled back, 2026-07-29

One marker closed. Proven red first: with the `[Ignore]` removed, replay brought the discarded write
back — `Expected: null, But was: <50>`, the byte for `"2"`.

### 9.1 No change to the log format, because of how replay already works

`Put` and `Delete` write to the journal the moment they are called, while the store is not touched
until commit. Rolling back to a savepoint restored only the in-memory change set, so the journal kept
its account of writes the transaction had thrown away.

The obvious fix — a new `RollbackToSavepoint` entry type that replay has to interpret — would change
the log format and its version. It is unnecessary: `WalReplayVisitorTransactional` buffers a
transaction's operations **in order** and applies them on commit, so a **compensating record** logged
at rollback simply wins. `RollbackToSavepoint` now logs, for every key whose logged value no longer
matches where the transaction stands, what the rollback put back.

### 9.2 The dangerous case is not the obvious one

A key created after the savepoint compensates to a delete — easy, and the case the finding describes.
A key that **already existed in the store** and was only modified after the savepoint must compensate
to a *put of its original value*: compensating with a delete there would destroy a row the transaction
never owned, turning a fix for silent resurrection into a cause of silent deletion.

`OldValue` — captured when the transaction first touched the key — is what makes the distinction, and
`SavepointReplayTests` carries a case for each, plus a control that writes before the savepoint still
survive (without it, a compensation that simply discarded the whole transaction would pass everything
else) and a case where a rewrite after the rollback must beat the compensation.

### 9.3 An observation about the suite, not a finding

These fixtures cost seconds per test on this machine — the same file-backed journal test runs in 74 ms
alone and 16 s alongside others, and that includes the pre-existing test which has no compensating
records at all. It is the cost of `Flush(flushToDisk: true)` per journal record on this volume under
the suite's parallelism, not something this PR introduced. No test here asserts on elapsed time, so it
is a note for phase 5 rather than a risk — recorded because a number that surprising should not sit
unexplained.

### 9.4 Counts after PR 4

Core suite **2223 → 2228 passed, 24 → 23 skipped**, both frameworks identical; whole solution green.
Ledger **73 → 72** attributes plus 13 `[TestCase(… Ignore =)]` — **85 suppressed entries**.

---

## 10. PR 5 results — one defect fixed, and the intended fix refuted twice, 2026-07-29

Subject 3 (row-id counters) and its sibling, the row count that disagrees with the rows. **Neither
marker closes.** What this PR ships is a different defect, found while trying to close them — and the
two refutations, which are the more valuable half.

### 10.1 The intended fix, and why it does not work either way

Both defects have one cause: the row counts, row ids and row version are written **after** the commit
has returned, outside the flush the commit performed, inside a `try { } catch { }` that swallows every
failure. Making them commit atomically with the rows they describe should close both. There are
exactly two ways to do that, and **both were tried and both were refuted by measurement**:

- **Write straight to the store, before the commit.** Throws `LockRecursionException` on
  `TransactionalStore` — `BeginTransaction` holds the write lock for the transaction's whole life, and
  `Put` reaches for it again. The original code carried a comment saying it deferred for exactly this
  reason; that comment turns out to be right, which is worth recording because this project's habit is
  to distrust them.
- **Write through the transaction.** Puts the shared `$schema:` keys into the MVCC write set, and
  since every commit persists **every** table's counters, each commit then collides with the one
  before it. **3140 of 3142 conformance tests failed** with *"Transaction cannot commit due to
  write-write conflict"*. Narrowing it to only the touched tables would trade the wholesale failure
  for spurious serialization failures between honest concurrent writers — worse, not better.

So the fix needs one of two things this PR does not build: metadata that commits atomically **without
joining conflict detection**, or **reconstruction on open** when the persisted numbers are behind the
data. The second is what the code's own comment has been promising all along — *"metadata can be
recovered by scanning on next startup"* — and nothing implements it.

Both markers stay, with what was tried written into them.

### 10.2 What the attempt did find: a namespace the MVCC store was claiming

Routing the metadata through the transaction failed a second way first, and that one **is** fixed here.
`MvccKeyValueStore.CommitTransaction` skipped **every key beginning with `$`** when marking a
transaction's records committed. It has exactly one metadata key of its own,
`$mvcc:max_timestamp` — but the SQL engine keeps its entire schema catalog under `$schema:`.

**A key beginning with `$` written inside an MVCC transaction was committed and then never became
visible.** The transaction reported success; the value was gone. Proven with the same key written with
and without the prefix: `null` against the value.

The skip is now an exact-key comparison, which is how the rest of that class already filters its own
metadata in two other places — and the `MvccRecord.TryDeserialize` check at every call site was
already rejecting anything that is not a versioned record, so the prefix test was carrying no weight
beyond its collision.

`MvccMetadataKeyCollisionTests` holds it: the defect, a control with the same key minus the `$`, and a
control that the store's own timestamp key still survives a commit untouched — because narrowing the
skip must not start treating that one as a versioned record.

### 10.3 Counts after PR 5

Core suite **2228 → 2231 passed**, 23 skipped; engine suite back to **1949 / 34** with both markers
restored. Whole solution green on both frameworks. Ledger **unchanged at 72** attributes plus 13
`[TestCase(… Ignore =)]`.

---

## 11. PR 6 results — the SSTable reaches the media before the WAL is dropped, 2026-07-29

Subject 4. **It did not need the real power cut this plan reserved for it — it needed the count.**

### 11.1 Counting settled what a crash could not

The audit filed this as *mechanism confirmed, consequence not reproduced*, on the reasoning that
showing the loss needs a real power cut because a clean process kill lets the operating system write
its cache back. True, and beside the point: **a store that never asks for durability cannot have
achieved it**, and a count of zero is unambiguous in a way that a surviving-row count after a kill is
not. Same move as "every commit scans the whole database" in the MVCC batch — count, do not time.

Measured through the seam: **0 syncs per SSTable**. `SSTableBuilder.Finish` now syncs before
returning, so the WAL copy is destroyed only after the table is on the media. Unconditional rather
than tied to `SyncWrites`: a memtable flush is the moment the only other durable copy is dropped, and
it happens once per memtable rather than per write.

### 11.2 The consequence, stated the way a user would meet it

**`Flush()` reduced durability.** It replaced a WAL the caller may have synced with an SSTable that
was never synced, and truncated the WAL immediately afterwards — so asking for the data to be made
safe was precisely what put it at risk. That is now pinned by a test rather than described.

### 11.3 The seam, and the control that proves it is wired

`ISstableFile` / `ISstableFileFactory` in `Core/LSM/`, defaulted to an ordinary file, reachable
through `LsmOptions.SstableFileFactory`. `Sync()` is deliberately separate from a stream flush,
because flushing a `BinaryWriter` or a `FileStream` pushes bytes into the operating system and no
further — and the difference between the two is exactly what a power failure sees.

Three controls, and the third is the one that matters:

- the counter can see a sync that happened, so a zero elsewhere is evidence rather than a broken
  counter;
- an SSTable written through the seam is **byte-identical** to one written the ordinary way, so a
  measurement taken through it is not a statement about the seam;
- **a memtable flush driven through `StoreLsm` syncs** — everything else drives `SSTableBuilder` by
  hand and would stay green if the option never reached the store.

**A caveat recorded rather than half-done:** on POSIX this makes the file's *contents* durable, but
the directory entry naming a newly created file is separate and .NET exposes no portable way to fsync
a directory. Recovery already tolerates a missing SSTable — the WAL is the fallback — and closing it
properly needs a platform-specific call.

### 11.4 What the seam unlocks next

The `core-lsm` finding recorded as *mechanism only* — a failed flush leaving `m_immutableMemTable`
populated forever — said reproducing it needs an injected I/O failure the `StoreLsm` surface offers no
way to arrange. **That surface now exists.** The note in `CoreLsmFindingsTests` has been corrected to
say so.

### 11.5 Counts after PR 6

Core suite **2231 → 2235 passed**, 23 skipped, both frameworks identical; whole solution green.
Ledger unchanged at **72 + 13** — this finding was carried as a comment, not a marker.

---

## 12. PR 7 results — an unfinished table is not a table, 2026-07-29

Subject 5. The audit's compaction finding covered one half of the crash window; **the other half was
worse than the half that was recorded**, and nobody had looked at it.

### 12.1 The half nobody looked at

The recorded finding is a crash *after* the output is published with an input still on disk, and its
verdict — mechanism confirmed, consequence not reproduced — was re-checked and still holds: the
survivor is readmitted but loses, because the output sorts newer and keeps its tombstones.

The other half is a crash *while writing*. Both the memtable flush and the compactor wrote straight to
the final name, `sst_NNNNNN.sst`, so a crash part-way through left a truncated file already carrying
the name recovery looks for — with the highest id, which made it the **newest** table in the store.

Measured: **the next open failed outright** — `InvalidDataException: Invalid SSTable magic`. One crash
at the wrong moment and the database could not be opened at all.

### 12.2 Two questions, two different answers

- **A table that was never finished must never appear.** Fixed: it is written under a name the store
  ignores and renamed into place once complete and synced. A rename within one directory is atomic on
  NTFS and on POSIX, so there is no manifest to keep consistent.
- **A table that *is* damaged must be reported, not skipped.** Already true, and now pinned. This is
  the same principle the WAL fix settled: a database may lose data to corruption, but it must say so.
  Silently dropping an unreadable table would turn a hardware fault into missing rows nobody was told
  about.

The building name is a **prefix**, not an extra extension, and that detail is load-bearing: the store
lists its tables with `Directory.GetFiles(directory, "sst_*.sst")`, and on Windows a three-character
extension in a search pattern **also matches longer extensions beginning with it** — so
`sst_000009.sst.building` would have been listed as a live table on exactly the platform the guard is
meant to protect.

### 12.3 The first version of the test passed with the fix reverted

It used an injected write failure and a `using`. Disposing an unfinished builder deletes the fragment,
so the cleanup masked whether the rename did anything at all — the test was green either way.

**A crash runs no cleanup.** The test now abandons the builder without disposing it, releases the
handle the way a dead process would, and only then looks at the directory. Reverted, it fails with
*"Expected is String[1], actual is String[2]"* — the fragment listed as a table. The two mechanisms
are now separate tests, because they are separate claims: the rename is what survives a crash, the
cleanup is what keeps an ordinary failure from leaving litter.

That is the fourth instrument in this phase that was wrong before its subject was.

### 12.4 Counts after PR 7

Core suite **2235 → 2239 passed**, 23 skipped, both frameworks identical; whole solution green.
Ledger unchanged at **72 + 13**.

---

## 13. PR 8 results — the numbers commit with the rows they describe, 2026-07-29

Subject 3, and the row-count defect beside it. **Both markers close**, and the route that works is a
third one — neither of the two PR 5 refuted.

### 13.1 What PR 5 got wrong about its own refutation

PR 5 concluded that writing metadata through the transaction was impossible, because it made every
commit collide on the MVCC write set: 3140 of 3142 conformance tests failed. That was true of **what
it tried** — `PersistRow*ToStore` at commit time writes **every table's** counters, so each commit
contended with the previous one over tables it had never touched.

The distinction was not in the routing. It was in **which keys, and when**. Writing each counter
through the transaction *at the moment it is allocated from* puts only the touched tables in the write
set. Measured: **EF stays green at 552 passed**, and both crash markers turn.

The lesson is narrower than "the route is closed": a refutation is only as wide as the thing that was
actually run, and PR 5's write-up said "through the transaction" when what it had tested was "every
table's counters at commit". Recorded here rather than left standing.

### 13.2 What the fix is

`SaveTableRowId`, `SaveTableRowCount` and `SaveRowVersion` write through the transaction when there is
one, instead of updating only the in-memory cache and deferring to a post-commit pass. Repeated writes
to the same key inside a transaction collapse to one entry in its buffer, so the cost is one write-set
entry per table touched, not one per row.

Two things fall out of it:

- **Rollback gets simpler, not harder** than the old comment feared. A discarded transaction discards
  the metadata with it, instead of needing the cache reloaded from the store afterwards.
- **The post-commit persist is gone.** It was best-effort inside a `try { } catch { }` that swallowed
  every failure, and every commit rewrote every table's counters whether or not they had changed.

### 13.3 Measured, before and after

| | before | after |
|---|---|---|
| next insert after a crash, 20 rows with ids to 20 | **id 1** — an identity already in use | **id 21** |
| after a crash: `SELECT` / `COUNT(*)` | 20 rows / **0** | 20 rows / **20** |

### 13.4 Counts after PR 8

Engine suite **1949 → 1951 passed, 34 → 32 skipped**; Core unchanged at 2239; whole solution green on
both frameworks. Ledger **72 → 70** attributes plus 13 `[TestCase(… Ignore =)]` — **83 suppressed
entries**.

---

## 14. The four standing rules, applied to this phase

1. **The oracle settles attribution, never desirability.** Durability sits *below* the minimum-set
   line: PostgreSQL, SQL Server and SQLite all promise that a committed transaction survives a crash,
   so there is nothing to weigh here — "SQLite loses it too" would not be a defence even if it were
   true, and it is not. The oracle is still useful for **what a lost transaction should look like**:
   SQLite reports corruption rather than truncating past it, which is the shape subject 1 should copy.
2. **Prove by execution.** Every fix lands with a test that failed first, and the observed verdict
   string goes into this document before the fix, not after. Subjects 3, 4 and 5 currently have no
   proof at all — only a mechanism — so the first outcome of each instrument is a **verdict**, which
   may be "refuted".
3. **A record of a past fix is a claim.** Two verdicts in the durability batch are explicitly
   provisional — "not reproducible with the current surface" (rowid counters) and "mechanism confirmed,
   consequence not reproduced" (compaction, SSTable fsync). The instruments exist to re-decide them,
   and a re-decision that says "the audit was wrong" is a valid outcome to record.
4. **Build the control into the instrument.** C1–C6 above. A red control stops the phase until the
   instrument is fixed; it is never evidence about the engine.

---

## 15. Acceptance

- Baseline suite counts preserved or explained: Core.Tests **2213 passed / 26 skipped**, engine tests
  as recorded by PR 0.
- Every crash scenario reports a **classified verdict**, and the tests assert on the verdict.
- The controls C1–C6 are green on every run, and their failure is treated as a harness defect.
- Markers: subjects 1, 2 and 5 have tests today and close or convert them as they land. **Subjects 3
  and 4 have no test at all** — only a prose comment — so each *adds* a marker before it closes one,
  and the ledger is expected to rise before it falls, exactly as it did in phase 3. The ledger is
  reported as `73 + 13`, both numbers.
- CI stays green on both frameworks, and gains `Category=Crash` without gaining a timing-dependent gate
  — phase 3 §17 removed the last one, and phase 4 must not reintroduce it.
