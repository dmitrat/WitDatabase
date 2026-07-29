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
| 0 | This plan, plus the engine-suite baseline | none |
| 1 | Instrument A — crash runner, C1–C3, verdict classification | none |
| 2 | Instrument B — storage seam, modelled cut, fsync counter, C4–C6 | narrow internal factory in Core |
| 3 | WAL truncation on partial replay (+ the `RollbackJournal` relative path) | recovery |
| 4 | Savepoint replay | journal |
| 5 | Rowid counters | engine metadata |
| 6 | SSTable fsync, and `SyncWrites` documented for what it does | LSM |
| 7 | Compaction manifest | LSM |
| 8 | Statement atomicity / implicit per-statement transaction | engine write path |

Each PR waits for green CI before the next starts. A release is cut when enough has accumulated;
whether it is 3.1.0 or 4.0.0 is decided by the rule the previous releases used — **major if it changes
an answer a previous release gave**, and several of these change what survives a crash.

---

## 6. The four standing rules, applied to this phase

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

## 7. Acceptance

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
