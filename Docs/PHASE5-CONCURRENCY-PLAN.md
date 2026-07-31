# Phase 5 — Concurrency and concurrent access

> **Status: the FIRST HALF IS CLOSED, released as 5.0.0 on 2026-07-30.** All four questions the audit had
> to answer are answered; five defects are fixed, four of which were in no audit. **The remaining
> concurrency markers are the second half** — see § 8a for the PR record, what the phase changed about the
> method, and what is left.
>
> The shape held: PR 1 established the model by execution and changed **no** production line, deliberately.
> Phase 4 had shown what happens when a phase fixes a list instead of a subject — six of its thirteen
> defects were in no audit — and here four of five were.
>
> **The target model, decided 2026-07-30 (§ 8): one process, one engine per database, many
> connections, one writer at a time.** Cross-process access is out of scope by design; concurrent
> connections *within* one host process are the supported shape, because the goal is drop-in for
> ASP.NET Core services where the `DbContext`s are many and the host is one.
>
> **The heaviest finding so far is § 3a, and only CI could have produced it: an LSM database has no
> exclusivity on Linux.** A second connection opens and reads the first's data, because .NET maps
> `FileShare.Read` to a *shared* advisory lock on Unix. That is the platform the target deployment runs
> on, and the store chosen for write-heavy work. Measured cost: the two engines **diverge** — one sees
> one row, the other two. Data loss is the plausible next step and is **not** claimed, because the
> experiment that would prove it has not been run.
>
> Entry state: `main` at `d7e3e6e`, tag `v4.0.0`, seven packages published. Predecessors:
> `Docs/PHASE3-GRAMMAR-PLAN.md`, `Docs/PHASE4-DURABILITY-PLAN.md`,
> `Docs/NEXT-SESSION-PLAN.md` § "Phases 5–10".

---

## 1. The ledger, recounted

The phase-5 plan and the project memory both carried **68 + 13 = 81** suppressed entries. Recounted
2026-07-30, the honest figure is **66 + 13 = 79**, and the two grep errors do *not* cancel:

| Measure | Command | Result |
|---|---|---|
| Lines matching `\[Ignore` | `grep -rn "\[Ignore" --include=*.cs Sources/` | 72 |
| …of which `///` prose | | −4 |
| …of which `[Ignore]` inside a string literal | | −2 |
| **Real `[Ignore(…)]` attributes** | `grep -rho "\[Ignore(" --include=*.cs Sources/` | **66** |
| Lines matching `Ignore *=` | | 20 |
| …of which `private const string …Ignore =` | | −7 |
| **`[TestCase(… Ignore = …)]` entries** | | **13** |
| **Suppressed entries** | | **79** |
| *After PR 2 closed the parallel-mode marker* | | **65 + 13 = 78** |
| *After PR 4 added three (instrument B, § 7a)* | | **67 + 14 = 81** |

**The `TestCase` filter is wrong too — third correction to this count, 2026-07-30.** PR 4's marker went
on a **continuation line**:

```csharp
[TestCase(false, TestName = "…",
    Ignore = "CONFIRMED …")]
```

so `grep "Ignore *=" | grep -c "TestCase"` does not see it, and reported 13 where the truth was 14. **Use
this instead**, which needs no assumption about line breaks:

```
grep -rho "\[Ignore(" --include=*.cs Sources/ | wc -l                    # attributes
grep -rn  "Ignore *=" --include=*.cs Sources/ | grep -vc "const string"  # TestCase properties
```

Three wrong answers in one phase, all from pattern-matching a C# attribute with a line-oriented tool.
The lesson is not the number; it is that **a count nobody has cross-checked against a second method is
a guess**, and this ledger is quoted in every release note.
| `[Explicit]` attributes | | **2** (not 3 — one hit is prose inside the other's reason) |

The recorded count of 68 came from filtering `\[Ignore` for non-comment lines, which still admits the
two prose mentions inside test reason strings. Recorded here because the *method* of counting is what
was wrong, and the same filter will be wrong again next phase.

**The concurrency area holds 17 markers, not 15:**

| File | Markers |
|---|---|
| `Core.Tests/AuditVerification/CoreConcurrencyFindingsTests.cs` | 10 |
| `AdoNet.Tests/Parallel/WitDbConnectionParallelAccessTests.cs` | 5 |
| `Core.Tests/Concurrency/LockManagerTests.cs` | 1 — "Flaky due to file lock timing issues" |
| `AdoNet.Tests/AuditVerification/ConnectionPoolFindingTests.cs` | 1 — the pool permit leak |

---

## 2. Instrument A — the concurrency-model probe

Nothing in the repository states what the concurrency model is, so the first instrument does not look
for defects at all: it **measures the model**. Two fixtures, 27 tests, all green, each pinning an
observation rather than a wish.

- `Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/ConcurrencyModelProbeTests.cs`
  — 19 tests, asking every question through the ADO.NET surface, which is also what answers
  "reachable from the provider".
- `Sources/Core/OutWit.Database.Core.Tests/AuditVerification/ConcurrencyModelWiringProbeTests.cs`
  — 8 tests, asking what the builder actually wires.

**Controls, built in from the start rather than added after a false report:**

| Control | What it protects against |
|---|---|
| Two connections to two *different* files both open | The probes measuring the harness instead of the sharing model |
| One connection closed and reopened succeeds | "Refused because shared exclusively" vs "the first opener leaked its handle" |
| A refused `Open` leaves nothing behind, and the file reopens after dispose | A failed open leaking a handle and poisoning later probes |
| `FileLock` used directly creates its `.lock` sidecar | "No sidecar exists" being read as evidence when the probe simply looks in the wrong place |
| The parked store admits exactly one writer and blocks it there | The serialisation probes measuring the decorator rather than the lock manager |
| The marker's `CREATE TABLE` run with **no** parallel mode | Attributing a failure to the setting that happened to be in the connection string |

Every control was green on the first run. Two further disciplines carried over from earlier phases
and both mattered:

- **Rows are counted by reading them, never by `COUNT(*)`.** This engine answers `COUNT(*)` from a
  cached per-table counter, and phase 4 published a false catastrophe by trusting it.
- **The write-serialisation probe checks the hand-off crossed a thread boundary** — it reports
  distinct thread ids seen inside the store, because `ReaderWriterLockSlim.IsWriteLockHeld` is
  thread-affine and a same-thread test once made `PageLatchManager` look correct.

The parked-collaborator probe **discriminated in both directions** on its first run — one writer
inside with locking on, two with it off — so it can detect the absence of serialisation and not
merely report a number. That is the property a stress loop never has.

---

## 3. Question 1 — what the concurrency model is

**Answer, measured: one engine per database, one process, one writer at a time, and no readers
beside it.** Every route to a second concurrent holder of a database is closed, and the storage layer
closes it at the OS level rather than by policy.

| Configuration | Second opener, Windows | Second opener, Linux | Enforced by |
|---|---|---|---|
| btree over a file (default) | `IOException` | `IOException` | `StorageFile` → `FileShare.None` |
| LSM over a directory | `IOException` on `<dir>/wal.log` | **opens, and reads the first's data** | the WAL — and only on Windows |
| `Read Only=true` | `IOException` — setting dropped, § 4 | `IOException` | `FileShare.None` again |
| `Data Source=:memory:` | opens, **a different database** | opens, **a different database** | nothing keyed by connection string |

**Exactly one row differs by platform, and it is the dangerous one — see § 3a.** Every other verdict in
this document was identical on both. The model above is therefore the *Windows* model; on Linux an LSM
database has no exclusivity at all.

Three consequences worth stating separately, because each was a guess before it was a measurement:

1. **LSM exclusivity comes from a different file than btree's.** The LSM store's own files are
   shareable — `SSTableReader` opens `FileShare.Read` — and it is `WriteAheadLogBase`, opening
   `ReadWrite`/`FileShare.Read`, that refuses the second opener. So the model is uniform, but the
   *mechanism* is not, and the LSM failure lands part-way through building the engine.
2. **A refused open is clean.** The first connection keeps working, the refused connection reports
   `Closed`, and the file opens normally once the first is disposed. Pinned by a control, because a
   leaked handle here would have quietly invalidated every later probe.
3. **The refusal is a raw `IOException` carrying an OS message.** Through ADO.NET a consumer catching
   `DbException` does not catch it, and the message names a Windows sharing violation rather than the
   engine's own limit. That is a phase-6 shaped defect discovered in phase 5; recorded, not fixed
   here.

### 3a. An LSM database has no exclusivity on Linux — **a defect, in no audit, found by CI**

The instrument's first version asserted that a second LSM connection is refused, full stop, having
measured exactly that on Windows. **The Linux runner disagreed**, and the probe's own output says what
happened:

```
PROBE  Q1 lsm, second connection in the same process [unix]        ->  OK, value <null>
PROBE  Q1 lsm, the second connection reads a row the first wrote [unix]  ->  OK, value String:a
```

The second connection **opened, and read the row the first connection had written.** Two independent
engines are then live over one LSM directory, each with its own memtable and its own handle on the same
write-ahead log, and nothing coordinates them — `FileLock` would have been the mechanism, and § 3 has
just established that it is unreachable.

**The mechanism, and it explains why btree is unaffected.** .NET emulates `FileShare` on Unix with
advisory `flock`: `FileShare.None` becomes an exclusive lock, and every other value becomes a *shared*
one. So:

- `StorageFile` passes `FileShare.None` → exclusive → btree is refused on both platforms.
- `WriteAheadLogBase` opens `FileAccess.ReadWrite, FileShare.Read` → **shared** on Unix. Windows reads
  that share mode as "no second writer"; Unix reads it as "come in". `SSTableReader` is
  `FileShare.Read` too, so nothing else in the LSM store stands in the way.

**Why this matters more than the marker count.** Linux is where an ASP.NET Core service is most likely
to be deployed, and § 8 makes concurrent connections the supported shape. So the platform where the
target deployment lives is the platform with no protection, on the store chosen for write-heavy
workloads. It is also invisible: nothing fails, nothing warns, and the second engine returns correct
answers right up until the two memtables disagree.

### What it costs — measured, and less than "corruption"

`ProbeTwoLsmConnectionsBothWriteTest` asked the next question and the Linux runner answered it. The
verdict is **divergent views, not a lost write** — stated that way deliberately, because the severity of
a claim is a reason to check it harder, not to publish it faster:

| Observation, Linux | Value |
|---|---|
| second `Open` | succeeds |
| second engine's `INSERT` | succeeds |
| rows visible to the **first** engine | **1** |
| rows visible to the **second** engine | **2** |
| rows on disk after both engines closed | **2** |

Both writes survived. What did not survive is agreement: the second engine sees both rows because it
**replayed `wal.log` at open**, which already held the first engine's row; the first engine cannot see
the second's row at all, because it lives in a memtable belonging to another engine and nothing
invalidates or notifies. Two engines, one database, both answering confidently, neither wrong by its own
lights.

**So the honest statement of the defect is:** on Linux an LSM database admits a second engine, and the
two then diverge. **Not proven, and therefore not claimed:** that this loses data under contention. The
mechanism for loss is visible — both engines flush memtables into the same directory and both rewrite
the same log, and phase 4's scan/compaction race is exactly this hazard *within* one engine — but the
experiment that would settle it has not been run. **The next experiment, named so it is not quietly
dropped:** two engines interleaving flushes and a compaction over overlapping key ranges, with rows
counted by reading them. It belongs with the fix, not with the audit, because it is a test of whatever
mechanism closes § 3a.

> ### 3a-bis. The premise, re-checked 2026-07-31 — and it has changed
>
> The experiment above was named as *"a test of whatever mechanism closes § 3a"*, and that mechanism —
> the exclusive `.lock` sidecar — shipped in 5.0.0. **So the premise was re-checked before the experiment
> was run**, by execution and on both platforms
> (`LsmTwoEngineReachabilityProbeTests`). It has changed:
>
> | Configuration | Windows | **Linux** |
> |---|---|---|
> | **default** | refused, `DatabaseAlreadyOpenException` | **refused, `DatabaseAlreadyOpenException`** |
> | `FileLocking=false` | refused, `IOException` (the log's share mode) | **BOTH ENGINES OPEN** |
>
> **Two engines over one LSM database are no longer reachable in any supported default.** The one route
> left is `FileLocking=false` on Unix — the switch `WitSQL.md` § 15.0 documents as *disabling the guard*,
> for filesystems where advisory locking is unreliable.
>
> And in that configuration § 3a's symptom reproduces **exactly**, so the CI run that answered the
> reachability question also took the measurement: *first engine sees the second's row = **False**,
> second engine sees the first's row = **True***. The asymmetry is the original mechanism — the second
> engine replays `wal.log` at open, which already holds the first engine's row; the first cannot see the
> second's row because it lives in a memtable belonging to another engine and nothing invalidates or
> notifies. It is now **pinned as an assertion**, so the sharp edge is executed rather than prose.
>
> **What this does to the promised experiment.** Its subject is no longer a defect in a supported
> configuration; it is the cost of an escape hatch that documents itself as removing the protection. That
> is a real question for anyone who sets the switch on a network share, and a much smaller one than the
> phase recorded. It is also **Unix-only, so every iteration costs a CI cycle** — which is the argument
> for deciding whether it is worth running rather than assuming it is.
>
> **Windows is not evidence here, in either direction.** It refuses the second engine with the guard off
> too — but through the write-ahead log's `FileShare.Read`, which is precisely the mechanism § 3a proved
> does not hold on Unix. A local green says nothing about the platform the deployment target runs on.

Both numbers above are pinned in the probe, so a change to what the two engines see fails the build
rather than passing quietly.

### 3b. Fixed in PR 3 — the limit is now enforced rather than inherited

**The guard.** An exclusive `.lock` sidecar, taken in `WitDatabaseBuilder.Build`/`BuildAsync` **before any
database file is opened** and released by `WitDatabase.Dispose` **after the store closes its files**. A
second engine is refused with `DatabaseAlreadyOpenException` naming the database and explaining the
limit, instead of whichever raw `IOException` a share-mode collision produced first.

Why a sidecar rather than tightening the write-ahead log's `FileShare`:

- `FileShare.None` is an **exclusive `flock`** on Unix, so the sidecar behaves identically on both
  platforms — which is exactly what the log's `FileShare.Read` did not.
- It does not depend on **which files a configuration happens to create**, which the `EnableWal=false`
  hole showed is the real requirement. Tightening the log would have closed every case the ADO.NET
  provider can produce and still left the Core API able to build an unprotected database.
- The OS releases the handle when the owning process exits, so a crash does not leave a database
  permanently locked. Phase 4 established that a crash runs no cleanup, so a guard that needed cleanup
  would have been the wrong shape.

**What the fix reversed about this document.** § 8 said `FileLock` was dead weight to remove. It is now
the mechanism, unchanged apart from one addition — and the reasoning that reversed it is § 8's own
caveat: ruling cross-process access out of *support* is not permission for the limit to go unenforced.

**Three things caught during the fix, and none by reading:**

1. **The guard nearly refused the first engine.** `AcquireExclusiveLock(TimeSpan.Zero)` computes
   `deadline = UtcNow + timeout` and loops `while (UtcNow < deadline)`, so a zero timeout **skips the
   body and reports a timeout without ever trying**. Hence the new `TryAcquireExclusiveLock`, and a test
   that pins the trap so nobody expresses "try once" that way again.
2. **`EnsureDeleted` reported success with the sidecar still on disk** — caught by an existing EF test
   within minutes. `DatabaseFiles` exists precisely so "whoever creates those paths and whoever deletes
   them cannot drift apart", and the sidecar drifted from it on the day it was introduced. The lock path
   now comes from `DatabaseFiles.GetLockPath` and `Delete` removes it. **The suite caught this, which is
   worth recording in a project whose usual finding is the opposite.**
3. **A half-failed `Build` would have leaked the lock.** The guard is taken before the store, so anything
   throwing afterwards has to release it — otherwise nothing ever would, and the database would be
   permanently unopenable. The `try`/`catch` around the rest of `Build` is that release, and
   `ProbeRefusedOpenLeavesNothingBehindTest` is the test for the shape.

**The pins inverted, which is how the fix proved itself.** Two probes from PR 1 asserted the *defects*
and were labelled to be inverted when fixed; both went red on the first run after the guard landed, and
both now assert the fixed behaviour:

| Probe | Before | After |
|---|---|---|
| `ProbeFileBackedDatabaseCreatesTheLockSidecarTest` | no sidecar — `FileLock` unreachable | sidecar present for the engine's lifetime |
| `ProbeSecondWriterIsSerialisedWithFileLockingOffTest` | **2** writers inside the store | **1** |
| `ProbeLsmWithoutWalIsStillExclusiveTest(false)` | `[Ignore]`d — a second engine opened | closed; refused with the typed exception |

The LSM probes also **lost their platform branches**, which is the clearest statement that § 3a is
closed: the expectation no longer depends on the platform or on the configuration, and all four
`EnableWal`×`Transactions` combinations are asserted identically.

**Breaking, deliberately, and it ships as 5.0.0.** On Linux, two connections to one LSM database used to
work; they now throw. That configuration was unsafe — § 3a measured the two engines diverging — so
refusing it is the fix rather than a regression. **It does not yet make the ASP.NET Core shape work**:
each `WitDbConnection` still builds its own engine, so two connections in one process are still refused.
That is the shared-engine subject in § 7, and the guard is what makes it safe to build.

### The cross-process mechanism existed and was unreachable — *fixed in PR 3, § 3b*

*As measured in PR 1. Kept as written because it is the finding the fix answers; `FileLock` is now the
exclusivity guard, and `LockManager`'s file-locking constructor is still unused — the guard calls
`FileLock` directly, since pairing it with an in-process handle was never what the exclusivity job
needed.*

`FileLock` is documented as the multi-process mechanism; `LockManager` has a constructor taking a
database path in order to use it; `EnableFileLocking` defaults to true. None of that is reachable.
`WitDatabaseBuilder.BuildTransactionalStoreInternal` calls `new LockManager(Options.LockTimeout)` —
the **other** constructor, whose own summary reads *"Creates a lock manager for in-memory databases
(no file locking)"* — so `UseFileLocking` is false for a file-backed database and `FileLock` is never
constructed.

Proved by execution rather than by that reading: **no reachable configuration creates a `.lock`
sidecar** — not btree with transactions, not with MVCC, not with file locking explicitly off — while
the control shows `FileLock` does create one when used directly.

It could hardly matter, given that `FileShare.None` already prevents the second process the FileLock
would have coordinated. But it does matter, in the opposite direction to the option's name:

### `FileLocking=false` removes the only write serialisation there is — **a defect, in no audit** *(fixed, § 3b)*

`EnableFileLocking` does not select *how* locking works; it decides **whether a `LockManager` exists
at all**, and both transactional stores document `null` as "no locking". So a consumer who writes
`FileLocking=false` — reading it as "do not coordinate across processes", which is what the name and
the class comments promise — instead switches off in-process write serialisation.

Measured with the parked collaborator, on two distinct threads:

| Configuration | Writers inside the store at once |
|---|---|
| default (file locking on) | **1** |
| `FileLocking=false` | **2** |

There is no cross-process locking to turn off, and what the flag actually removes is the mutual
exclusion between two threads writing the same store. Pinned by
`ProbeSecondWriterIsSerialisedWithFileLockingOffTest`, whose assertion is labelled as pinning a
defect so that a future fix inverts it rather than silently agreeing with it.

---

## 4. Read-only is parsed and dropped — **a defect, in no audit**

Both spellings are accepted by `WitDbConnectionStringBuilder` and neither reaches the storage layer:
`WitDbConnection.ConfigureStorage` only asks whether the mode is `Memory`, and `options.ReadOnly` is
never read anywhere in the provider.

| Probe | Observed |
|---|---|
| `Read Only=true`, then `INSERT` | **succeeds**; the row reads back |
| `Mode=ReadOnly`, then `INSERT` | **succeeds** |
| Two `Read Only=true` connections to one file | `IOException` on the second |

This one compounds: `StorageFile` *does* grant `FileShare.Read` when opened read-only, so **many
readers over one file is a shape the storage layer already supports**, and it is unreachable only
because the provider drops the setting. The cheapest route to "one writer, many readers" runs through
this defect.

### 4a. Fixed in PR 6 — and the fix is not where § 4 assumed

**§ 4 above reasoned toward the wrong mechanism.** It treated read-only as a *storage* property, whose
value was that `StorageFile` grants `FileShare.Read` and so many readers could share a file. PR 5 removed
the premise: connections already share one engine, so many readers over one file needs nothing from the
storage layer. What read-only is actually *for* is a connection that must not write.

So it is enforced **per session**, and that choice is load-bearing rather than cosmetic. As a storage
property, a read-only connection and a writing connection would ask for different databases and one of
them would be refused as an options mismatch — which forbids the pairing read-only exists to allow. `Read
Only` and `Mode` are therefore excluded from the shared-database signature, and
`ReadOnlyAndWritingConnectionsCoexistTest` is the test that says so.

**Fail-closed.** A read-only session permits a named list of statement kinds — `SELECT`, `EXPLAIN`, and
transaction control — and refuses everything else, so a statement kind added to WitSQL later is refused
until somebody judges it safe. The other way round, a read-only guarantee would weaken silently every
time the language grew.

**The bulk API was a hole, and guarding `Execute` alone would have left it open.** `BulkInsert`,
`BulkUpdate` and `BulkDelete` write without parsing anything, so they never pass the statement check —
five public ways straight through a read-only connection. Each now calls `EnsureNotReadOnly`, and it is a
named method precisely so the next write path that bypasses statement execution has something obvious to
call.

**The revert test: 16 of 19 red** with the flag no longer threaded through. The three that stay green are
the read-allowing cases, which is correct — reads work either way.

**Found on the way, recorded not fixed:** `Mode=ReadWrite` means "open an existing database, fail if it is
not there", and it **silently creates one instead**, leaving the file behind. Same defect family — the
other three `Mode` values are all dropped by `ConfigureStorage`, which only asks whether the mode is
`Memory` — but a *database-level* fix (`FileMode.Open` against `OpenOrCreate`) that changes behaviour for
anyone currently relying on a database being created for them. SQLite refuses the shape. Marker added, so
the ledger goes 65 → 66 while § 4 closes.

---

## 5. Question 3 — parallel mode: **supported, and the marker was misattributed**

The marker read *"Parallel Mode=Buffered causes SQL parsing issues - requires investigation"* and had
been carried for months. It is wrong about its own cause.

All four modes — `Auto`, `Buffered`, `Latched`, `Optimistic` — open a file-backed database, take DDL,
take ten `INSERT`s, and return **all ten rows to a scan**. What fails is the ignored fixture's own
statement:

```
CREATE TABLE Data (Key TEXT PRIMARY KEY, Value TEXT)
→ WitSqlParsingException: Line 1:19 - mismatched input 'Key' expecting {…}
```

`KEY` is a lexer token (`Grammars/WitSqlLexer.g4:52`) and cannot be a bare column name. The
attribution control runs the identical statement **with no parallel mode set at all** and it fails
the same way; renaming the column off `Key` makes the same shape pass. So this is a grammar defect —
reserved words unusable as identifiers — that spent months labelled as a concurrency one.

Note this is *not* the existing `parser` marker, which is about `WitSqlExpressionSerializer` emitting
unquoted reserved identifiers on a round trip. This is a statement a **user** writes by hand.

**Verdict: parallel mode is a supported configuration**, and the marker is **closed, not reworded** —
PR 2 fixed the grammar. Ledger 66 → 65.

### 5a. Fixed, and then measured properly — 118 keywords, not one

The fix is one line: `KEY` added to `nonReservedKeyword`. It is unambiguous because `KEY` appears in
this grammar only after `PRIMARY` or `FOREIGN`, both reserved; the build produces no new ANTLR
ambiguity, `PRIMARY KEY`/`FOREIGN KEY` still parse, and so does the hard case —
`CREATE TABLE T (Key TEXT, Value TEXT, PRIMARY KEY (Key))`, where the identifier and the keyword sit
two tokens apart.

**But this class had escaped a 104-finding audit, so fixing the one name it tripped over is not
enough.** `KeywordAsIdentifierCorpusTests` now asks the question of the **whole lexer vocabulary**,
taken from the generated lexer's own `ruleNames` so a keyword added tomorrow is covered without anyone
remembering. **172 keywords cannot be used as a bare column name.** Then the oracle was asked the same
172, and it split them:

| | Count | Meaning |
|---|---|---|
| SQLite **accepts**, WitSQL refuses | **118** | a real drop-in gap |
| SQLite **also refuses** | **54** | correct — PostgreSQL and SQL Server refuse them too |

So the finding is **118**, and the correction runs against my own earlier wording: refusing a bare
reserved word is standard, not a defect, and the sentence "`Key` is an ordinary column name in every
dialect the project targets" was too strong — `KEY` is non-reserved in PostgreSQL and the SQL standard
but **is** reserved in SQL Server. The oracle is what kept the claim honest, which is the rule working
as intended: it settles attribution, not desirability.

The 118 include names an ordinary entity would use: `Text`, `Int`, `Decimal`, `Double`, `Char`, `Money`,
`Json`, `Guid`, `Row`, `Start`, `End`, `Current`, `Timestamp`, `View`, `Column`, `Interval`,
`Sequence`. Most are **type** names, so `CREATE TABLE T (Text TEXT)` asks the parser to take a type
keyword as a column name immediately before a type keyword — real ambiguity risk, and its own piece of
work.

**Recorded, not fixed here.** Phase 5's remit is concurrency; `KEY` was fixed because the phase tripped
over it and the owner asked for it. The 118 are pinned by name in
`Grammar/keywords-unusable-as-column-name.txt`, split into the two sections above, and handed to
phase 7 where the DDL round-trip corpus lives.

**Two things the corpus does that a count would not.** It pins names rather than a number, so a
grammar change that fixes one keyword and breaks another cannot go green — the trap
`GrammarRoundTripTests` was rebuilt to avoid. And it fails in **both** directions: verified by removing
`TEXT` from the pinned list (red: "stopped working") and by adding `KEY` to it (red: "now work"). A
pinned list nobody has proved can fail is just a comment.

**A dead grammar alternative found on the way.** `VALUE` was listed in `nonReservedKeyword` but is not
a lexer token at all, so ANTLR defined it implicitly and the alternative could never match — it emitted
`warning(125): implicit definition of token VALUE` on every build. Removed; `Value` as a column name
works and always did, by matching `IDENTIFIER`. Checked the other 134 entries the same way: `VALUE` was
the only one.

---

## 6. The access question — the pool cannot serve the shape it exists for

`ConnectionPool` exists, is `sealed`, is keyed by connection string, has **15** tests in
`ConnectionPoolTests` (plus 9 in `PoolOptionsTests`, which cover the options record), and
**nothing in the provider references it**: `Pooling`, `Min Pool Size` and `Max Pool Size` are parsed
by `WitDbConnectionStringBuilder` and read by nothing outside `Pool/`. Measured, the mechanism does
not work over a real database either:

| Probe | Observed |
|---|---|
| Pool over a file, `Min Pool Size=2` | `IOException` **in `GetPool`** — the pool cannot be constructed |
| Pool over a file, two simultaneous borrows | first succeeds, **second throws `IOException`** |
| Pool over `:memory:`, two borrows, write in one and read in the other | `Table 'T' not found` |

The third row is the one that explains the first two surviving a green suite. **Every test in
`ConnectionPoolTests` uses `Data Source=:memory:`**, and two `:memory:` connections are two separate
databases — so the suite exercises borrowing, returning, lifetime and idle eviction, and never once
the property the pool exists for: that the connections it hands out address the same data. Fifteen
green tests, **zero of them file-backed**, and the load-bearing property is untested.

*(Counted rather than estimated: this section first said "roughly thirty tests", which was wrong —
`--filter FullyQualifiedName~ConnectionPoolTests` reports 15, and the `~Pool` filter's 50 sweeps in
`PoolOptionsTests`, `ConnectionPoolFindingTests` and the connection-string builder's pool properties.
Recorded because the section's whole argument is that a count can flatter a suite, and estimating my
own would have been the same mistake one level up.)*

This is the third instance of the same shape in three phases — phase 3's acceptance-only oracle,
phase 4's `COUNT(*)` verification, and now a pool suite in a storage mode where sharing is
meaningless. **A suite can be green, large, and about nothing.**

---

## 6a. What the model probe says about the ASP.NET Core shape

Recorded after § 8 was decided, because it changes which of the § 3 measurements is the important one.
The supported deployment is one host process holding several `DbContext`s, so the load-bearing question
is not "can a second process open the database" but **"can a second connection open it"** — and that is
the row of § 3 that reads `IOException`.

Two consequences for the work, both measured rather than assumed:

- **The refusal is at the OS level, not in a policy layer.** `StorageFile` passes `FileShare.None` to
  `FileStream`, so no amount of coordination above it makes a second `WitDbConnection` work. Sharing has
  to be achieved by **not opening the file twice** — one engine, many handles — rather than by opening
  it twice more politely.
- **The engine already serialises writers in-process**, measured at 1 writer inside the store under the
  default configuration (§ 3). So the shared-engine shape does not need a new lock hierarchy, which is
  the risk the phase-5 plan called its highest. It needs the sharing, plus the serialisation it already
  has, plus `FileLocking=false` no longer being able to switch that serialisation off.

---

## 7. What this phase must still establish

### Question 2 — answered: which markers a consumer can actually reach

Established by finding every production construction site of each subject and every production caller of
the specific member the marker names. This is analysis of the call graph rather than a running test, and
it is labelled as such — where it changes a priority, the sharper claim is the one about *callers*, not
about types.

| Marker subject | Reachable from the provider? |
|---|---|
| `MvccTransaction` — deadlock detector never fed | **Yes.** MVCC is the provider default |
| `RowLockHandle` / `RowLockManager` — dispose releases nothing; continuation runs inline | **Yes.** `MvccTransactionalStore` constructs `RowLockManager` unconditionally |
| `PageCacheShardedClock.Clear` — recycles a buffer mid-write | **Yes, but only through `Dispose`** — see below |
| `LsmParallelStore` / `LsmParallelWriter` — does not read its own write; discards buffered writes | **Yes**, with `Store=lsm` and a parallel mode |
| `PageLatchManager` — double-grant, then `SynchronizationLockException` | **No. Dead code** |
| `ConnectionPool` permit leak *(already known)* | **No**, and now doubly so |

**`PageLatchManager` is referenced by nothing but itself.** No production code constructs it or calls it —
the only references in the whole of `Sources/` outside tests are its own declaration and its own nested
`LatchHandle`. So its marker is a real defect in code no consumer can enter, which is the second such
case in this area. Worth saying plainly: that is an argument about *priority*, not a dismissal, and the
cheapest honest resolution is to delete the class rather than fix it.

**The data-corruption marker is narrower than it reads, and this is the most useful thing question 2
produced.** The marker says `Clear()` recycles a pooled buffer while its write is in flight, which is
true. But **no production code calls `IPageCache.Clear()` from outside the cache** — the only caller is
`Dispose`. So the window is not an ordinary write path; it is *closing the cache while a write is still in
flight*, i.e. a disorderly shutdown. That makes it a durability-adjacent defect rather than a
write-path one, and it changes what the fix has to guarantee: `Clear` must refuse a pinned page exactly as
`Evict` already does, and `Dispose` must not be able to discard an unfinished write.

**What this does not change.** Every one of these is still on the books. Reachability decides the order
they are worked in, not whether they are worked — and the phase has already shown twice that a marker's
own text can be wrong about its cause.
### Question 4 — answered in PR 7

The claim was in prose, in two places — `WitSQL.md` § 15.0 and `DatabaseAlreadyOpenException`'s own
message: *the operating system releases the handle when the owning process exits, so a process that dies
without shutting down cleanly does not leave the database permanently unopenable.* Nothing executed it.

**Neither half is provable in one process.** "A second process is refused" would have the guard arguing
with itself; "the lock is released when a process dies" would be measuring `Dispose`, because **a crash
runs no cleanup** — so nothing in the dying process's own code can be what releases the lock. Phase 4
built the out-of-process runner for exactly this class of claim.

New scenario `lock-held-kill`: open through the ADO.NET provider, write a row, and park with the lock
held and **no** `Close` or `Dispose`. The test then, in one method because each half is the other's
control:

| | Observed |
|---|---|
| `.lock` sidecar exists while the other process is parked | ✔ |
| Opening here, with that process holding it | `DatabaseAlreadyOpenException`, naming the database |
| Opening here after that process is **killed** | ✔ succeeds |
| The row the killed process committed | ✔ still readable |

Split into two tests these would both pass while meaning nothing — a "refused" test passes if opening
never works at all, and a "reopens" test passes if the lock was never taken. Asserting the exception
**type** also shows the guard fires *before* `StorageFile`'s share mode, which is what makes this a test
of the guard rather than of `FileShare.None`.

**Not covered separately:** the same crossing for an LSM database. The mechanism is the same sidecar and
the same code path, and the LSM cases are covered in-process, but a cross-process LSM scenario would be
the more complete statement.

- **Question 4, original scoping.** Every verdict above is from this machine, and
  a second machine has overturned a local verdict twice in this project. The multi-process harness is
  **narrower than the plan expected**, now that § 8 rules cross-process access out of scope: "is a
  second process refused" is answered by `FileShare.None` at the OS level, so the harness has one
  question left worth the build — **can a process open the database after another crashed holding it**,
  which is where phase 4's recovery work meets this phase's access model. `Tools/OutWit.Database.CrashRunner`
  already kills a process mid-write; the missing half is the reopen.
- **The shared-engine mechanism itself**, which § 8 turns from an open question into the phase's main
  subject. **Instrument B is now built and it has changed the shape of the job — see § 7a.**

### 7a. Instrument B — what two engines over one database see, and why the obvious design fails

`Sources/Engine/OutWit.Database.Tests/Concurrency/SharedDatabaseTwoEnginesProbeTests.cs`, six tests,
two controls. The obvious way to make many connections work is *share the `WitDatabase`, give each
connection its own `WitSqlEngine`* — the engine holds `m_currentTransaction`, so it is a session rather
than a database object. This instrument asked whether that split is actually correct. It is not, yet.

| Probe | Observed |
|---|---|
| Control: one engine sees its own work | ✔ |
| Control: second engine sees a table that **predates** it | ✔ — `SchemaCatalog` loads in its constructor |
| Second engine sees a table created **after** it | ✘ `InvalidOperationException: Table 'Later' not found` |
| Second engine scans rows the first inserted | ✔ returns **1** |
| …and `COUNT(*)` for the same rows | ✘ returns **0** |
| Two sessions begin transactions, **MVCC off** | ✘ `LockRecursionException` |
| Two sessions begin transactions, **MVCC on** | ✔ |

**The blocker is the schema catalog, not the store.** `SchemaCatalog` loads the schema **once, in its
constructor**, into plain dictionaries of tables, indexes, views, triggers, sequences, row ids and row
counts — and `WitSqlEngine` constructs its own. So two sessions over one database each hold a private,
immediately stale idea of the schema. `ReloadMetadataFromStore` exists but refreshes only the counters.
**The catalog is database-level state that is currently session-level**, and that is the thing to move.

**The insidious half is the count.** A scan through the second engine returns the row; `COUNT(*)`
through the *same* engine returns zero, because the count comes from that engine's own catalog counter
while the rows come off the shared store. A query and its own count disagree **across sessions**, with
no crash involved — the same split phase 4 met after a process kill. It was caught only because the
probe measured both: a `COUNT(*)`-only test would have reported no rows, and a rows-only test would
have reported success. That is the fourth time this project's own `COUNT(*)` has nearly told a lie.

**MVCC is what makes the shape possible at all.** With MVCC off, a transaction holds the database-wide
write lock for its whole duration, so a second session's `BEGIN` throws `LockRecursionException` on the
same thread — one transaction per database, and a poor diagnosis of it. One writer at a time *is* the
documented model for `MVCC=false`, so the defect there is the error message rather than the exclusion.
**MVCC is the provider default** (`WitDbConnectionStringBuilder.Mvcc` → `true`), and its case passes, so
this does not block the work — but it does mean the supported shape is MVCC-only, which needs saying in
`WitSQL.md` once it lands.

**Where the shared catalog has to live.** Not on `WitDatabase`: that lives in Core, `SchemaCatalog` lives
in the engine assembly, and Core must not reference upward. So the registry in the ADO.NET layer holds
the pair — it already references both — and `WitSqlEngine` needs a constructor that accepts a catalog
instead of always building one.

**Ledger: 65 → 67, plus one `TestCase` marker (13 → 14), so 81 suppressed entries.** Three new markers,
all defects that were in no audit, and the phase predicted the ledger would rise before it falls. The
third of them also exposed the counting error in § 1.

**Why PR 4 stopped at the measurement.** The fix is a real architecture change — moving the schema
catalog from session scope to database scope and adding a `WitSqlEngine` constructor that accepts one —
and the shape of it was only knowable *after* these results. Landing the instrument first is the same
order phase 4 used, where the instrument PR preceded every fix and each fix then had a verdict to aim at.

### 7b. Fixed in PR 5 — the ASP.NET Core shape works

**What was built.** `SharedDatabase`, a reference-counted process-wide registry keyed by the resolved
full path of the data source, holding one `WitDatabase` **and one `SchemaCatalog`** per database. Each
connection takes a lease, builds its **own** `WitSqlEngine` over the shared pair, and gives the lease
back on close; the last one out disposes the database and releases the exclusive lock. Plus
`WitSqlEngine(WitDatabase, SchemaCatalog, bool)`, so a caller that owns a database can hand one catalog
to every session on it.

**The division of labour, which § 7a is what settled:** the engine is a *session* — it holds the current
transaction — while storage and schema are properties of the *database*. Sharing the store alone was not
enough, and that was measured, not guessed.

**What now works, tested through `DbConnection` rather than the concrete type:** a second connection
opens; it sees another's committed rows *and* their `COUNT(*)`; it sees a table created after it opened;
writes are visible in both directions; ten overlapping connections behave like scoped contexts; the
database is disposed only when the last connection closes; `Close()` then `Dispose()` releases one share,
not two; reopening after the last close builds a fresh engine and finds the data.

**Refusals that remain, deliberately.** A second *engine* — `DatabaseAlreadyOpenException`, because that
means a second process. One database opened with **different options** in one process —
`InvalidOperationException` naming the mismatch, because handing the second caller an engine built to
somebody else's configuration is worse than refusing. `:memory:` connections stay private to their
connection, as they were and as SQLite's are without `Cache=Shared`; making them shared would be an
opt-in feature, and doing it silently would turn every test that wants a clean in-memory database into
one that shares.

**§ 6 closes as a side effect, and the side effect is instructive.** `ConnectionPool` now works over a
file-backed database — `Min Pool Size=2` constructs, two simultaneous borrows succeed — and **nothing in
the pool changed.** Pooled connections are ordinary connections, and connections now share an engine. The
pool was only ever pooling the cheap half; sharing the engine is what the demo-deployment shape actually
needed. The pool is still referenced by nothing in the provider, so whether to wire it at all is now a
question about connection-object reuse rather than about making the shape work.

**The revert test found a hole in my own suite, which is exactly what it is for.** With the shared
catalog reverted and the shared database kept, only **one** of eleven tests went red. The others create
and populate the table *before* the second connection opens, so its catalog picks the state up at
construction and they pass either way — and none of them checked a `COUNT(*)` taken after the second
connection was already open, which is the precise thing that used to be stale. Adding that case and the
missing count assertions took the revert from **1 red to 3 red**. A suite that passes with the fix
removed is measuring something else, and there is no way to discover that except to remove it.

**Ledger: 67 → 65.** The two instrument-B markers are **re-decided rather than closed**: with a supported
way to get agreement, per-catalog divergence stops being an open defect and becomes a documented sharp
edge of the single-argument constructor. Both are active tests again, pinning the divergence with a note
that says a future pass would mean the catalog reads through to the store and the sharing constructor
could go. Reclassifying is not the same as fixing, and the distinction is in the tests.

---

## 8. The decision the audit handed back — **taken 2026-07-30**

The audit was asked to establish the model, and it did. It could not decide **what the model should
be**, because that is desirability, not attribution, and the standing rule is explicit that the SQLite
oracle settles only the latter. So it was put to the owner, and the answer splits the question in a
way neither option offered had:

> **Single-process by design — "on to она и файловая база". But the target is drop-in for ASP.NET Core
> services, where the host is one process and the `DbContext`s are many.**

That is not "single-connection", and it is not "cross-process". It is a third model, and it is the one
the phase now works toward:

> ### One process. One engine per database. **Many connections.** One writer at a time.

Read against § 3, this decides every open item in the area:

- **Cross-process access is out of scope by design** — but the limit must be *enforced*, and this bullet
  originally got that wrong. It read: "`FileLock`, the file-locking branch of `LockManager`, and the
  `LockHandle*Combined` handles are dead weight — unreachable today and unwanted tomorrow."

  **Corrected by its own caveat, and then by measurement.** The caveat was that "out of scope" is a
  decision about what to *support*, not permission for the limit to go unenforced — and § 3a plus the
  `EnableWal` hole then showed the limit was not enforced at all on Linux, nor anywhere for a log-less
  LSM database. So `FileLock` is **not** dead weight: PR 3 made it the exclusivity guard (§ 3b), which is
  a different job from the cross-process *write coordination* it was written for. What remains genuinely
  unused is `LockManager`'s file-locking constructor and the `LockHandle*Combined` pair, because the
  guard needs the sidecar alone, not a sidecar paired with an in-process handle.

  `FileShare.None` is not the defect either; it was simply an inconsistent way to enforce the limit,
  and the typed exception now says what the limit is.
- **`FileShare.None` *is* in the way of the supported shape.** A scoped `DbContext` is one connection
  per request, and a host serves requests concurrently, so N live connections to one database inside
  one process is the ordinary case — exactly what the model probe measured as an `IOException` today.
- **The mechanism needed is not `ConnectionPool`.** The pool creates an independent `WitDbConnection`,
  and therefore an independent engine, per pooled entry — which is why it collides with itself over a
  file (§ 6). What the ASP.NET Core shape needs is the opposite: **one shared engine per data source
  within the process, with connections as lightweight handles onto it.** The pool's 15 tests describe
  borrowing and eviction, not sharing, so almost none of that suite transfers.
- **In-process write serialisation becomes load-bearing.** It is the only thing standing between two
  concurrent requests, which makes `FileLocking=false` silently removing it (§ 3) a more serious
  defect than it looked, not a lesser one.
- **The four "multiple file connections not supported for embedded database" markers are half right.**
  Multiple *processes*: correct, and now a documented limit. Multiple *connections*: to be built, so
  those markers convert rather than close.

`FileLocking=false` (§ 3) and read-only being dropped (§ 4) are defects under any reading, and neither
fix depends on the decision. Read-only gains a second purpose under it: with a shared engine, it is how
a connection declares it will not write.

**Also decided: the reserved-word defect (§ 5) is fixed now**, rather than handed to phase 7. `Key` is
an ordinary column name in every dialect the project targets, the defect is proved by execution, and it
was found here.

---

## 8a. The first half, closed — released as 5.0.0, 2026-07-30

| PR | Subject | Outcome |
|---|---|---|
| #52 | The plan, and instrument A — the concurrency-model probe | The model established by execution. Three defects in no audit; one marker refuted; **CI then found a fourth the dev machine could not** |
| #53 | `KEY` usable as an identifier, and the keyword corpus | Marker closed. The corpus measured the class at **118** keywords and handed it to phase 7 |
| #54 | One engine per database, enforced | § 3a and the `EnableWal` hole closed by an explicit guard; `FileLocking=false` no longer removes write serialisation |
| #55 | Instrument B | **Refuted the obvious shared-engine design** and found the blocker: the schema catalog was session-scoped state that had to be database-scoped |
| #56 | Many connections, one engine | The headline — the ASP.NET Core shape works. Closed § 6 as a side effect, with no change to `ConnectionPool` |
| #57 | Read-only honoured | § 4 closed, per session so a reader sits alongside writers. Found `Mode=ReadWrite` dropped the same way |
| #58 | The lock across a process boundary | Question 4 answered. Two claims that were prose in `WitSQL.md` and in an exception message are now executed |
| #59 | Marker reachability | Question 2 answered. `PageLatchManager` is **dead code**; the corruption window is reachable **only via `Dispose`** |
| #60 | Release 5.0.0 | Seven packages, verified from the **downloaded** nuspecs — version and every internal dependency |

**All four audit questions are answered.** § 3 the model, § "Question 2" reachability, § 5 parallel mode,
§ "Question 4" the process boundary.

**Five defects fixed, four of which were in no audit** — and every one of those four was found by an
instrument rather than by reading: LSM had no exclusivity on Linux; an LSM database with no write-ahead log
had none anywhere; `FileLocking=false` removed the only write serialisation there was; and a per-session
schema catalog made two connections disagree about both tables and row counts.

### What this phase changed about the method

- **CI is the arbiter of *platform*, not only of timing.** The heaviest finding — § 3a — was invisible on
  Windows, because .NET maps `FileShare.Read` to a *shared* `flock` on Unix. A probe that generalised a
  Windows measurement went red on the runner, which is the good direction: the instrument over-claimed,
  and the red exposed a data-integrity defect no local control could have caught. **Third time a second
  machine has settled something here, and the first time the two disagreed about by-design behaviour
  rather than about a race.**
- **The revert test audits the *suite*, not just the fix.** Eleven tests covered the shared engine and all
  passed; reverting the shared catalog turned **one** red, because the other ten created their table
  before the second connection opened and none checked a `COUNT(*)` taken afterwards. Adding that case
  took the revert to **three** red. *Count how many tests the revert turns red; if it is fewer than you
  expected, the suite is testing its own setup.*
- **Pin observations as assertions, labelled as observations.** Every probe asserted the measured value with
  a comment saying `PINS A DEFECT, NOT CORRECT BEHAVIOUR` and what the fix should invert it to. Pins
  flipped in four PRs, and each flip *was* the proof the fix landed. One probe has now recorded three
  different models and says so in place.
- **Reclassifying is not fixing.** Two instrument-B markers came off the ledger as *re-decided* — a
  documented sharp edge of a constructor rather than an open defect — and both stayed as active tests
  pinning the divergence. The distinction is in the tests deliberately, so a later reader cannot mistake
  one for the other.
- **A count nobody has cross-checked against a second method is a guess.** The ledger was miscounted three
  times in one phase, the last time because a marker sat on a continuation line. § 1 has the robust form.
- **The instrument was wrong before its subject, for the sixth time in this project** — and my own fix
  nearly refused the *first* engine, because `AcquireExclusiveLock(TimeSpan.Zero)` reports a timeout
  without ever trying.

### What remains in the area

> **CLOSED 2026-07-30 by PR 8 (§ 8b.10 – § 8b.12).** The two markers that needed an instrument before
> they could be worked have both been instrumented and fixed, and a third defect fell out of the first
> fix. `CoreConcurrencyFindingsTests` now holds **no markers at all**, and the concurrency area is down
> to the two entries that are deliberately not defects: the unreachable `ConnectionPool` permit leak
> (confirmed twice) and the reclassified `MVCC=false` divergence `TestCase` on
> `SharedDatabaseTwoEnginesProbeTests`.
>
> **Ledger: 52 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 66 suppressed entries**, plus 2
> `[Explicit]` — counted with the § 1 commands, which is the only form that sees a marker on a
> continuation line.
>
> The original scoping is kept below, because the *order* it argued for is what the work followed.

**15 `[Ignore(…)]` markers plus one `TestCase` property**, and reachability (§ "Question 2") is the order
to work them in. The severity ranking has changed: the marker the plan called "corrupts data outright" is
reachable only through `Dispose`, which makes it durability-adjacent, while the row-lock and MVCC
deadlock-detector markers sit on the provider's default path.

**Still open, and named so it is not quietly dropped:** two engines interleaving flushes and a compaction
over overlapping key ranges — the contended experiment § 3a said would settle whether divergence becomes
loss. It belongs with whatever mechanism closes the remaining LSM work, not with the audit. **Still not
done as of 2026-07-30**, and still the last open piece of phase 5.

---

## 8b.10 Secondary indexes had no serialisation at all — instrument, then fix (PR 8)

The last two markers both needed an instrument before they could be worked, and this is the first of
them. **`HighConcurrencyStressTest` fails about 3 times in 27 whole-fixture runs**, so no number of green
runs could have proved a fix. It was replaced with an exact experiment.

### The seam, chosen from a measurement

`SecondaryIndexConcurrencyProbeTests` parks a writer *inside* a B+Tree leaf split. The seam was not
guessed — it was measured first, and the measurement is kept as a control:

> With a warm page cache, an index `Add` that fits in the leaf makes **no storage call at all**; the only
> storage traffic during an insert is the `SetSize` that `SplitLeaf` makes when it allocates the new leaf.

So parking a thread on its first storage call parks it inside the split, after `CollectLeafEntries` has
snapshotted the leaf and before `node.Clear()` rewrites it. The parking storage records *which* call it
parked on and the probes assert it was the allocation, so the seam cannot silently move.

### What the experiment shows, and it is worse than the marker said

One writer parked mid-split, a second let into the same leaf, the first released. Over **ten runs**:

| Outcome | Runs |
|---|---|
| The second writer throws `ArgumentOutOfRangeException` / `IndexOutOfRangeException`, its entry lost | 9 |
| **Nothing throws and three entries are simply gone** — two of them the *first* writer's, already inserted | 1 |

**The exception is the lucky outcome.** The marker recorded only the crash; the quiet version loses rows
that were never in doubt. What is pinned is what was stable across all ten: damage occurred, and the
second writer's entry is missing. The *shape* is deliberately not pinned, for the same reason § 8b.6 did
not pin a row count.

### The mechanism, and a second finding inside it

`WitDatabaseBuilder` wraps the **main** store in `BTreeConcurrentStore`; `CreateBTreeIndexFactory` handed
every index a bare `StoreBTree`, which has **no locking of any kind**. `StoreInMemory` and `StoreLsm` —
the other two stores that factory can produce — both lock internally, so the gap was specific to the
file-backed B+Tree index, which is the default for a file database.

The instrument also measured something not in the finding: **`PageManager.AllocatePage` holds its lock
across the `SetSize`**, so the second writer *does* block — but only when it asks for a page of its own,
long after it has walked into the leaf and snapshotted it. **The allocator's lock delays the corruption
instead of preventing it.** That is why "the second writer had to wait" is reported by the probe and
never asserted on: it is true in both directions.

### The fix, and the regression it had to avoid

The obvious fix — hand index stores the wrapper the main store already gets — was **measured before it
was accepted**, because `BTreeConcurrentStore.Scan` materialised the whole range under one read lock:

| Store | `FindRange(x, null)` on a 200,000-entry index, consumer takes 5 | Allocated |
|---|---|---|
| Bare `StoreBTree` | 3.1 ms | ~0 MB |
| `BTreeConcurrentStore` as it was | **108.3 ms** | **25.5 MB** |

That is the shape a `LIMIT` produces, and it is the commonest thing an index does. Fixing the corruption
that way would have paid for it with a 35× regression on the read path.

So the wrapper's scan was rewritten to **stream in chunks**: each chunk is read under the read lock with
no writer inside the tree, the lock is released before the chunk reaches the consumer, and the next chunk
re-seeks from the key immediately after the last one returned — a position a concurrent split cannot
invalidate, unlike a page-and-slot cursor. Measured after: **82 KB** for the same query.

Holding the read lock across the enumeration instead was considered and rejected by reasoning that has a
test of its own: the engine deletes rows *through* an index range scan, so the consumer writes to the
store while its scan is open, and the read-to-write upgrade on one thread throws. `ScanAllowsTheConsumer
ToWriteWhileEnumeratingTest` pins that this stays possible.

**The semantic this buys is stated rather than hidden: a scan is no longer a snapshot.** Writes landing
between chunks are visible to the rest of the scan — which is exactly what the unwrapped store does, so
concurrent mode now behaves like the default mode instead of differently from it.

Index stores are then wrapped **unconditionally**, not only under a parallel mode: a second *connection*
is enough to reach the defect, and that is the shape 5.0.0 exists for.

### The marker, and what un-ignoring it is worth

`HighConcurrencyStressTest` is un-ignored, and the comparison is like-for-like: its recorded rate was
**about 3 failures in 27 whole-fixture runs**, and after the fix it is **0 in 27**, run the same way.
That is a signal, not a proof, and the test says so in place — the proof is the deterministic experiment.

### The revert counts, and the thin one is reported as thin

| Fix reverted | Tests red |
|---|---|
| Index stores wrapped | **2** — both cases of the wiring probe |
| Chunked scan | 1 |

**Two is thin, and worth being honest about.** The product-level claim this fix can make deterministically
is a wiring claim: *the builder now hands secondary indexes a serialised store*. The behavioural claim —
*that wrapper is exactly what prevents the corruption* — is proved by the parked-collaborator probe over a
store built the way the factory builds it, in both directions. There is no deterministic product-level
test between the two, because the factory builds its storage from a path and nothing can be parked inside
it without adding a seam to production code for a test's benefit.

---

## 8b.11 `FlushAllAsync` took buffers their owners were still writing into (PR 8)

The other marker that needed an instrument. It was recorded as **not reproduced**, then confirmed by CI on
both PR runs, and left suppressed as timing-dependent.

### Why it needed CI: the scenario never contended

The marker's own test **parks the producer** while the foreign flush runs, so the writes never overlap the
flush at all. Measured here: **0 failures in 20 rounds**. An instrument that cannot reach the window is
not a weak instrument, it is the wrong experiment.

`LsmParallelWriterFlushProbeTests` overlaps them — a producer writing while another thread flushes every
thread's buffer — and the defect reproduces on an ordinary development machine:

| Round | Result before the fix |
|---|---|
| 0 | lost **8 consecutive** entries |
| 1 | lost **9 consecutive** entries |
| 2 | `ArgumentException: Destination array was not long enough` inside `LsmWriteBuffer.Drain` |

A list copied while another thread is adding to it, and a contiguous run of losses — which is the
signature CI reported, *the tail of a batch*: the entries appended after the merge loop had taken its copy.

### The mechanism

`FlushAllAsync` iterated `ThreadLocal.Values` and handed each buffer to the merge loop, which drains and
**disposes** it — while the owning thread was still appending to the same `List`. It then reset only the
**calling** thread's slot, so every other owner was left holding a buffer that had already been merged and
thrown away.

The reset could not have been done any other way: `ThreadLocal<T>` hands out another thread's value but
gives **no way to replace it**. So the buffer now sits behind a per-thread `BufferSlot`, and taking it is
an exchange under the slot's gate — the same gate the owner appends under. The owner never touches a
buffer that has been handed away, and a flush still reaches every thread, which the commit path needs
(§ 8b.7: read-your-own-writes is what the commit protocol is built on).

### The instrument was wrong before its subject — the seventh time in this project

The first version of this probe was **not** the version that ships, and the corrections are the useful
part. Each was caught by a control, and each would have shipped a probe that measured nothing:

| Version | What it did | Why it was wrong |
|---|---|---|
| Unbounded producer, tight flush loop | 10/10 rounds caught the defect | Did not converge — the producer outran the merges, each flush handed over a bigger buffer, a round took **minutes** |
| Bounded producer, tight flush loop | Caught it 10/10 | Under a **whole-fixture** load one round had the flusher do 50 flushes while the producer wrote **one** entry, and another had the producer write 10× its quota before the flusher managed two. Its control went red inside the full run |
| Flusher waits for new entries, `Thread.Sleep(1)` | Stable, and its control passed | **Lost its power.** 2000 buffered writes take a millisecond and the flush that merges them takes a hundred, so a round held ONE overlapping flush — and with the fix reverted it then caught the defect in **one run out of two** |
| Credit window, flusher yields | 20 overlapping flushes per round, ~0.4 s | Ships |

**A control that only proves the subject was exercised is not enough — the instrument also has to be
re-measured against the defect it is meant to catch.** The `Sleep(1)` version was green, stable and
worthless, and only re-running it with the fix reverted showed that. Every version above was checked in
**both** directions before the next was written.

### 8b.11a `FlushAllAsync` did not flush what the size threshold had already queued — **found by CI**

The fix above went to CI and the marker's own test **still failed** — losing `k19` on one framework and
`k15..k19` on the other, after passing here 20 rounds out of 20. The two-thread reading of that failure
was too narrow, and the real defect needs **no second thread at all**:

> A buffer reaches the merge queue with **no completion attached** whenever `Put` crosses the size
> threshold — the fire-and-forget path — and `FlushAllAsync` waited only for the buffers it handed over
> itself. By then the thread's own slot is empty, so it handed over nothing, waited for nothing, and
> returned with the writes still in the channel.

`LsmParallelStore.Flush()` is `FlushAllAsync()` followed by `m_store.Flush()`, so the store was flushed
to an SSTable *without* the entries still queued behind it. Measured single-threaded, with 50 batches of
200 entries queued the fire-and-forget way:

| | Entries still in flight when `FlushAllAsync` returned |
|---|---|
| Before | **10,000 of 10,000** |
| After | 0 |

**The fix:** `FlushAllAsync` now queues an **empty buffer last** and waits for that too. The channel is
FIFO with a single reader, so a completion attached to the last thing in the queue fires only once
everything ahead of it has been applied to the store. It is not counted as a submitted buffer, because
it carries no writes.

**This is the fourth defect of the PR and the second one CI found that this machine could not** — and
the more useful half of the lesson is that the first fix was *correct and insufficient*: the marker was
red for two separate reasons, and closing one of them left the test failing for the other.

### The revert counts

| Fix reverted | Tests red |
|---|---|
| Buffers exchanged under the slot's gate | **2** — the contended probe and its control |
| The flush barrier | 1 — and 10,000 of 10,000 entries in flight, single-threaded |

With the fix reverted the contended probe fails **10 rounds out of 10**, measured three times (10 threw;
8 threw and 2 lost entries; 10 threw), and its control fails with it because the flusher dies. With the
fix applied the whole `Core.Tests` fixture is green three runs out of three, and the round reports 20
overlapping flushes every time.

The marker's own test stays **green** with the fix reverted — which is the whole point of § 8b.11 and the
reason it is kept but not treated as the evidence.

---

## 8b.12 A thread-affine lock held across an await — **a third defect, in no audit** (PR 8)

Found by the fallout of § 8b.10 rather than by looking for it: with secondary indexes now using
`BTreeConcurrentStore`, two index tests failed with

```
SynchronizationLockException: The write lock is being released without being held
  at ReaderWriterLockSlim.ExitWriteLock()
  at BTreeConcurrentStore.FlushAsync
  at IndexManager.FlushAsync
  at WitDatabase.FlushAsync
```

**`ReaderWriterLockSlim` is thread-affine** — it records which *thread* holds the lock. All four
asynchronous entry points on this store took the lock, awaited the inner store, and released: when the
continuation resumed on another thread the release threw, and — the half that matters — **the lock stayed
held by a thread that had moved on**, so every later reader and writer would wait for ever.

This shipped in 5.0.0. It was silent because only the main store used the wrapper and its awaits happened
to complete inline; an index flush is a genuinely asynchronous path, so wrapping index stores lit it up
immediately.

**Found by grepping the shape, not the place** — the same technique that found `TransactionWaitQueue` and
`PageCacheLru` in the first half. A scan of every `Enter*Lock … await … Exit*Lock` window in the product
returns exactly four hits, all in this one file, and `StoreLsm` is safe by construction because a `lock`
block cannot contain an await at all.

**The fix:** the asynchronous methods do their work through the synchronous ones. That is what they
effectively did before — the lock was held for the whole await regardless, so no concurrency is lost —
and it removes the hazard rather than narrowing it.

**The test is deterministic, not hopeful.** The storage underneath completes its asynchronous flush from
a thread of its own with continuations forced asynchronous, and the caller is a dedicated thread rather
than a pool thread, so the resuming thread *cannot* be the one that entered the lock. It then asserts the
second half as well: that another thread can still take the write lock afterwards.

| Fix reverted | Tests red |
|---|---|
| Async entry points made synchronous under the lock | **2–3**, and the spread is the finding |

Measured twice with the fix reverted: the first run turned **three** red — the deterministic test and both
index-metadata tests — and the second turned **two**, because one of the index tests happened to resume
its continuation on the thread that took the lock. **The deterministic test is red every time; the two
that found it in the first place are red only sometimes.** That spread is exactly why the defect survived
a release, and why the instrument does not rely on them.

---

## 8b.6 `Store=lsm` + parallel mode loses acknowledged writes — **the heaviest defect of the phase** (PR 5, instrument)

Going after two small LSM markers found something much larger, and this section is the instrument that
puts it on the record. **No fix here** — the fix is the next PR, and these pins are what will prove it.

### What was measured

Ten `INSERT`s over `Data Source=…;Store=lsm;Parallel Mode=Buffered`, **every one of which reported
success**:

| `Synchronous Commit` | Rows a scan returns | After a clean close and reopen | Surviving keys |
|---|---|---|---|
| **`true` — the default** | 1 *(0 on a rerun)* | **1** *(0 on a rerun)* | `[key0]` *(`[]`)* |
| `false` | 0 | **0** | `[]` |

**The reopen is what makes the verdict, and it is the harsher one.** Closing the connection disposes the
engine, which drains every write buffer. Rows still absent afterwards were *never written*. So this is
**lost data**, not the visibility problem the marker describes — and it is the **default** setting for
this store, reachable straight from a connection string.

### The marker understated its own finding

`ParallelLsmStoreReadsItsOwnWriteTest`'s reason said *"Get returned null for a key written moments
earlier"*. True, and the small half. Both LSM markers also turned out to have **different causes**, which
one reason string had been covering:

- **`Get` does not flush at all.**
- **`Scan` does flush** — through `FlushCurrentBuffer`, whose own doc comment reads *"Does not wait for
  merge to complete"*. It queues the buffer and reads the store before the merge it just requested has
  run. `ScanAsync` awaits the completion and is correct.

Both reason strings are rewritten in place, because a marker is a claim and these two were wrong about
their own cause.

### How wide the earlier verdict actually was

§ 5 of this document concluded **parallel mode is supported**, from probes that all run over the *default*
store. `WitDatabaseBuilder` wraps `StoreBTree` in `BTreeConcurrentStore` — **a wrapper that does not
buffer at all**. Only `Store=lsm` reaches `LsmParallelStore`. So the phase's own verdict was measured on
the component that cannot exhibit this defect. Same lesson as phase 4's PR 43: *a refutation is only as
wide as what was actually run.*

### The controls, and they bracket the defect to one component

| Control | Result | What it rules out |
|---|---|---|
| `Store=lsm`, **no** parallel mode, same ten INSERTs | **10 rows** ✔ | "the LSM store loses rows" — a much larger claim |
| `Parallel Mode=Buffered` over the **btree** store (§ 5's existing probe) | **10 rows** ✔ | "parallel mode loses rows" in general |
| `LsmParallelStore` driven **directly**, `Flush` after each write | **10 rows** ✔ | "the wrapper drops writes on its own" — it needs what the engine does differently |

Three green controls, so the defect is bracketed to `LsmParallelStore` *as the engine drives it*. The
third is the one that matters most for the fix: it stops the repair being aimed at the wrong layer.

### How many rows survive is deliberately NOT pinned

It came out **1 on one run and 0 on the next**, from identical code. An exact figure would be a
timing-dependent gate, and this project has already had CI inherit one of those (§ "Question 2",
`FlushAllAsync`). What is pinned is what was stable across runs:

- fewer than ten rows survive;
- the reopen count **equals** the pre-close count — the assertion that makes it a *data-loss* verdict
  rather than a visibility one;
- the survivors are the **first** rows written, not the last — which is the clue to where the writes go;
- the single-key lookup returns `null`.

Verified stable over three consecutive runs. **Invert to 10 / 10 / all ten keys / `value7` when the fix
lands.**

### Suspected mechanism, stated as a suspicion

Not yet established, and deliberately not asserted anywhere:

- `FlushAllAsync` resets the `ThreadLocal` buffer slot **only for the thread its `await` continuation
  resumed on**, while the thread that did the writing keeps referring to a buffer that has been handed
  away. This is the same root as the `FlushAllAsync` marker CI confirmed in the first half, seen from the
  engine's side.
- Separately and definitely: **`MergeBuffersBatch` applies every `Put` in a batch before every `Delete`**,
  which reorders operations within the batch. `Put k` → `Delete k` → `Put k` in one batch ends deleted.
  That is a second defect, in no audit, and it is not what loses these rows.

**Ledger unchanged at 60 + 14 = 74** — this PR adds instruments and corrects two reason strings; it closes
nothing. Said explicitly because a PR that changes no count can still be the most valuable one in a phase.

---

## 8b.7 The cause: MVCC's commit protocol is built on read-your-own-writes — PR 6

**The two "small" LSM markers were the cause of the data loss in § 8b.6.** That is the finding, and it is
the one worth carrying out of this phase.

### How it was found — by bisection, after the first hypothesis was refuted

The first hypothesis was the reordering in `MergeBuffersBatch` (all `Put`s applied before all `Delete`s).
**It was implemented and it did not fix anything** — which is the only reason the real cause was looked
for. Recorded because a plausible mechanism that survives a code read is still a guess.

The bisection, each step a measurement:

| Configuration | Rows readable |
|---|---|
| No transactions, parallel LSM | **0** |
| Non-MVCC transactions, parallel LSM | 10 ✔ |
| **MVCC transactions, parallel LSM** | **0** |
| MVCC, **no** parallel wrapper *(control)* | 10 ✔ |
| MVCC + parallel, reopened **without** the wrapper | **0** |
| One `Put`, one transaction, MVCC + parallel | **0** |

So it was not batching, not timing, and not a partial loss: **one write in one transaction was enough**,
and no configuration could read it back. Then the SSTable was dumped raw, and it contained the versioned
key with the *right* value bytes — so the write had happened. Comparing the record byte-for-byte against
the working configuration showed two 8-byte fields **transposed**: `TransactionId` still set and
`CommitTimestamp` zero, where the working one had the reverse. The version was **never marked committed**.

### The mechanism

`MvccKeyValueStore.CommitTransaction` **scans the store to find the versions it has just installed**, and
rewrites each one as committed:

```csharp
foreach (var (key, data) in m_innerStore.Scan(null, null))
    if (record.TransactionId == transactionId)
        m_innerStore.Put(key, record.AsCommitted(commitTimestamp).Serialize());
```

Over `LsmParallelStore` the install `Put` sits in a thread-local buffer, and `Scan` called the
fire-and-forget `FlushCurrentBuffer` — *"Does not wait for merge to complete"*, in its own doc comment. So
the scan read a store the merge had not reached, **the loop found nothing to commit**, and every version
stayed uncommitted for ever: on disk, invisible to every reader, unrecoverable.

**Read-your-own-writes is not a convenience for this engine. The commit protocol is built on it.** That is
why two markers filed as "Get returned null" were in fact a data-loss defect in a supported configuration.

### The fix

`FlushCurrentBufferAndWait()` on the writer, called by `Get` and `Scan`; `GetAsync` awaits the existing
async flush. It flushes **the calling thread's own buffer only** — that is exactly read-your-own-writes,
it is what the commit protocol needs (install and commit run on one thread), and it deliberately does not
reach for other threads' buffers, which is the separate `FlushAllAsync` race still on the books.

**Cost, stated rather than hidden:** a read on a thread with pending writes now pays a flush and a merge
wait. A read on a thread with nothing buffered — the ordinary reader — returns without touching the
channel, because an empty buffer short-circuits. So the cost falls precisely on the correctness
requirement. Phase 10 should measure it.

**The batch-ordering fix is kept**, as a separate defect in no audit: the buffer records operations in
order and the merge must apply them in order. It has its own test and its own revert.

### The revert counts

| Fix reverted | Tests red |
|---|---|
| `Get`/`Scan` flush-and-wait | **5** — 3 in the core fixture, 2 in the SQL probe |
| batch ordering | 1 |

The five include the § 8b.6 pins **inverted**: they asserted the loss and now assert all ten rows, before
and after a reopen, on both `Synchronous Commit` settings. That inversion is the proof the fix landed.

**Ledger: 58 `[Ignore(…)]` + 14 = 72.** The concurrency area is down to **7 markers** plus the one
`TestCase` property — 1 in `CoreConcurrencyFindingsTests` (the timing-dependent `FlushAllAsync` one),
4 in `WitDbConnectionParallelAccessTests`, 1 in `LockManagerTests`, 1 in `ConnectionPoolFindingTests`.

---

## 8b.8 Four markers whose reason was already false — and one that hid a new defect (PR 7)

All four `WitDbConnectionParallelAccessTests` markers carried the **same** reason:

> *"Multiple file connections not supported for embedded database. Use single connection with parallel
> commands instead."*

**That is a design statement, and PR #56 made it false** — several connections to one database in one
process is the shape 5.0.0 exists for. So these were records of a state the phase had already overtaken.
Rule 3 again, and this is now the fourth time in phase 5 that a carried reason string was wrong.

**Three are simply green.** `MultipleConnectionsReadTest`, `MultipleConnectionsWriteTest` and
`ConcurrentReadWriteTest` pass — and were run **15 consecutive times** before being made active, because
"it passed once" is exactly how CI inherits a flake.

### The fourth one is not flaky. It is a real, unrecorded data-integrity defect

`HighConcurrencyStressTest` fails under load with

```
ArgumentOutOfRangeException: Index out of range (Parameter 'index'). Actual value was 146.
  at BTreeNode.CollectLeafEntries          (BTreeNode.Split.cs:142)
  at BTree.SplitLeaf                       (BTree.Insert.cs:557)
  at StoreBTree.Put                        (StoreBTree.cs:240)
  at SecondaryIndexKeyValueStore.Add       (SecondaryIndexKeyValueStore.cs:234)
  at WitSqlEngine.UpdateIndexesOnInsert    (WitSqlEngine.Dml.IndexUpdates.cs:46)
```

— a **B+Tree leaf split corrupted by concurrent writers**, on the write path of a *secondary index*.

**The cause is visible in the construction, and it is a gap rather than a race in the tree.**
`WitDatabaseBuilder` wraps the **main** store in `BTreeConcurrentStore` when a parallel mode is set
(`store is StoreBTree btreeStore → new BTreeConcurrentStore(...)`), but `CreateBTreeIndexFactory` hands
every **secondary index** a bare `StoreBTree`:

```csharp
return new StoreBTree(storage, cacheSize / 4, ownsStorage: true);   // no wrapper, no serialisation
```

So concurrent connections serialise on the table's store and race freely on its indexes. This is a
data-integrity defect in the exact deployment shape 5.0.0 was built for, and it is **in no audit**.

### Why it is left suppressed rather than fixed here

- **It is load-dependent, not deterministic:** about **3 failures in 27** whole-fixture runs, and **0 in
  12** when the test runs alone — it needs the rest of the fixture running beside it. An active
  load-dependent test is a flaky gate, and this project has already had CI inherit one.
- **A fix cannot be proved by this test.** "It passed twenty times" is not evidence against a 10% race.
  The fix needs a *deterministic* instrument first — park the collaborator, as in § 2 — and that is its
  own piece of work, not a rider on a marker sweep.

The marker therefore stays, but its reason string is **replaced with the true one**, naming the stack, the
construction site, and the measured rate. That is the distinction this phase keeps making: the count did
not go down here, and it should not have.

**Ledger: 55 `[Ignore(…)]` + 14 = 69.** Concurrency area **4 markers** plus the one `TestCase` property —
`FlushAllAsync` (timing-dependent), `HighConcurrencyStressTest` (above), `LockManagerTests`, and the
unreachable `ConnectionPoolFindingTests` pool leak.

### 8b.8a Closing a database races opening it — **found by CI, in the mechanism 5.0.0 exists for**

Un-ignoring those three markers was green here **15 consecutive times** and went **red on the first CI
run**, on both frameworks. Fourth time in this project that a second machine has overturned a local
verdict, and the first where the local run was the over-optimistic one.

The failure was not the index corruption of § 8b.8. It was:

```
DatabaseAlreadyOpenException: The database '…/concurrent_rw.witdb' is already open.
  at WitDatabaseBuilder.AcquireExclusiveLock
  at SharedDatabase.Acquire
  at WitDbConnection.Open
```

**A connection was refused because the process was still closing the same database.** `SharedDatabase`
is the ref-counted registry PR #56 added to make many connections work, so this is a defect in the
mechanism the release exists for — and `Acquire` was *not* where it lived. `Acquire` already builds
inside the registry lock, with a comment explaining exactly this hazard.

`Release` was the gap. It removed the entry **inside** the lock and disposed **outside** it:

```csharp
lock (s_lock) { …; s_entries.Remove(key); database = entry.Database; }
database?.Dispose();          // ← this is what releases the exclusive file lock
```

Disposal is outside the lock deliberately — it flushes and may compact, and should not hold up opening an
unrelated database. But between the `Remove` and the `Dispose`, a concurrent `Acquire` for the same key
sees **no entry**, builds a second engine, and that engine's `AcquireExclusiveLock` hits the file lock the
first one has not let go of yet.

**The fix keeps both properties.** The entry is now *marked* `Closing` rather than removed, and is taken
out of the registry only after the database is really shut; an `Acquire` that finds a closing entry waits
on it **outside** the registry lock and then looks again. Unrelated databases still never wait, and
disposal still happens outside the lock.

**The proof is CI, and that is the honest statement.** The window is small enough that this machine could
not produce it in 15 runs; the runner produced it on the first. A local green is not evidence about this
one in either direction.

### 8b.9 The last unexamined marker — a suspicion with no verdict behind it

`LockManagerTests.MixedOperationsCompleteTest` carried *"Flaky due to file lock timing issues"*. Not a
measurement, not a finding: a note, carried unexamined, of the same kind as the parallel-mode one the
first half found to be blaming the wrong thing entirely.

**Re-run: 15 consecutive whole-fixture runs and two runs under the full project's load, all green.**
Un-ignored.

**And the reason string had it backwards, which is the part worth keeping.** The *file-lock* half of
`LockManager` is **not reachable from the product**: `WitDatabaseBuilder` only ever calls
`new LockManager(Options.LockTimeout)` — the overload that sets `m_fileLock = null`. The path-taking
constructor this fixture uses is entered by tests alone. So a failure here would be a statement about
`LockManager`'s own file-lock path, not about the engine.

That is the **third** piece of unreachable machinery in this area — after `PageLatchManager` (deleted) and
the `ConnectionPool` permit leak — and it is the same point the first half made about `LockManager`'s
class comment: *accurate about the class, false about the system.* Recorded rather than acted on: unlike
`PageLatchManager`, this constructor is a reasonable public API for a caller driving `LockManager`
directly, so there is nothing here to delete.

**Ledger: 54 `[Ignore(…)]` + 14 = 68.** The concurrency area is down to **3 markers** plus the one
`TestCase` property, and **every one of the three is now either unreachable or waiting on a deterministic
instrument** — there is no marker left in this area that could simply be run and fixed:

| Marker | Why it is still there |
|---|---|
| `LsmParallelWriter.FlushAllAsync` | timing-dependent; needs a deterministic instrument |
| `HighConcurrencyStressTest` | § 8b.8 — real defect, load-dependent; needs a deterministic instrument |
| `ConnectionPoolFindingTests` | unreachable, confirmed twice |

---

## 8b.5 The page-cache corruption window — PR 4

The marker the original plan called *"corrupts data outright"*. Question 2 had already narrowed it: **no
production code calls `IPageCache.Clear()` from outside the cache — the only caller is `Dispose`** — which
makes it a durability-adjacent defect rather than a write-path one, and which decides what the fix has to
guarantee.

**Reproduced first**, all three tests red on unfixed code.

### What was actually wrong, and it is not what the marker's wording suggests

`FlushAllAsync` **already pins** every dirty page before handing its buffer to the storage, and unpins in a
`finally`. `Evict` honours that pin — *"Cannot evict pinned page"*. `Clear` ignored it and disposed every
`CachedPage`, which returns the rented array to `ArrayPool<byte>.Shared` **while the storage is still
reading from it**. The next borrower's fill is then what reaches disk: `0xFF` instead of the `0xAB` the
caller wrote.

So the mechanism was never a missing pin — it was **one path not honouring a pin that already existed**.

### The two guarantees, and why they cannot be the same guarantee

- **`Clear` refuses a pinned page, on exactly `Evict`'s condition** — and refuses **before** flushing or
  disposing anything, so a rejected `Clear` leaves the cache untouched instead of half emptied. There is a
  test for that specifically; a pin check written *inside* the disposal loop would pass the marker's own
  test and still have recycled every page ahead of the pinned one.
- **`Dispose` waits instead of refusing.** It cannot inherit `Clear`'s refusal, because shutting down has
  to succeed. It also cannot wait for the reference count to reach zero: **`CreatePage` and `GetPage` pin
  the page they hand out**, so "pinned" routinely means "checked out by a caller" and that wait would never
  finish. A separate `m_writesInFlight` counter answers the narrower question — *is the storage still
  reading a pooled buffer* — and `Dispose` drains that, bounded at 30 s.
- **If the drain times out, a pinned page's buffer is dropped rather than returned to the pool.** Leaking a
  rented array costs a reuse; returning one a write is still reading from is the defect. That direction is
  deliberate.

### The same defect in the other cache — **in no audit**

*Fix every path with the shape, not the one the finding names.* The finding names
`PageCacheShardedClock.cs:160`. `PageCacheLru` has the **identical** structure — `Evict` refuses a pinned
page, `FlushAllAsync` pins for the duration of the write, `Clear` disposed unconditionally.

**And it corrupts identically, measured rather than assumed.** Proving "Clear does not refuse" would have
been the easy half; running it with the assertion relaxed showed **255 across the whole page** where `0xAB`
was written — the same `0xFF` mechanism, on the second implementation.

**It is reachable.** `PageCacheLru` is registered as a provider (`ProviderRegistration.cs:226`), so
`WithCacheKey("lru")` selects it — a supported configuration, not dead code like the latch subsystem. That
is the second time in this phase that checking the *other* implementation of an interface found the same
defect again.

### The revert counts

| Fix reverted | Tests red |
|---|---|
| `Clear` pin check (clock) | 2 |
| `Dispose` drain | 1 |
| `Clear` pin check (LRU) | 1 |

**Ledger: 60 `[Ignore(…)]` + 14 = 74.** Concurrency area **9 markers** plus the one `TestCase` property.

---

## 8b.4 The page-latch subsystem, deleted rather than repaired — PR 3

The marker was real and worse than filed: `Cleanup` decided a latch was idle using
`ReaderWriterLockSlim.IsWriteLockHeld`, which is **thread-affine**, so a second exclusive acquire *was*
granted while another thread held the page, and the holder's release then threw
`SynchronizationLockException` on a background thread — terminating the test host until the test wrapped
its own `Dispose`.

**It was closed by deletion, because nothing could enter it.** Re-verified before removing anything, since
a reachability record is a claim like any other — and the re-check made the finding *wider* than § "Question
2" had it:

- § "Question 2" said `PageLatchManager` was dead code. True, and **`PageLatch` is dead too** — the whole
  subsystem, 551 lines of production code, is entered by nothing. The manager is the only thing that
  constructs a `PageLatch`, and nothing constructs the manager.
- Swept `Sources/`, `Tools/`, `Samples/` and `Benchmarks/`: every reference to either type was its own
  declaration or its own test.
- **The compiler then confirmed it, which is stronger than the grep.** Deleting both classes left the whole
  solution building with **0 errors**. A reachability claim that survives `git rm` plus a full Release build
  is not an argument from reading.

**Why the mechanism was never needed.** `BTreeConcurrentStore` serialises with a single store-wide
`ReaderWriterLockSlim` — its own class comment calls it "a simple but effective ReaderWriterLock strategy".
Per-page latching is the finer-grained alternative, and under the model this phase decided — *one writer at
a time* — there is nothing for it to buy. It was built, tested, and never wired in.

**Deleted:** `Tree/PageLatch.cs` (175), `Tree/PageLatchManager.cs` (376), and their two fixtures (560
lines, 31 passing tests). **The marker's region is kept as a comment** in
`CoreConcurrencyFindingsTests.cs`, so that "the count went down" cannot be read as "the defect was
repaired" — the same distinction PR 4 of the first half had to make for two other markers.

**This is a breaking change**: two public types leave the surface. It is the reason the next release cannot
be a minor.

> **Carried to the release PR, so it is not shipped wrong:** § 15.0.2 of `WitSQL.md`, added by PR 2, says
> *"Before 5.1.0"* for the deadlock change. With public types removed the next release is a major, so that
> reference has to be corrected when the version is actually chosen.

**Ledger.** This PR removes one marker on its own: measured on its branch, **62 `[Ignore(…)]` + 14 = 76**,
because it branched from `main` before PR 2 landed. Once both are on `main` the figure is
**61 + 14 = 75**, and the concurrency area holds **10 markers** plus the one `TestCase` property — 4 in
`CoreConcurrencyFindingsTests`, 4 in `WitDbConnectionParallelAccessTests`, 1 in `LockManagerTests`, 1 in
`ConnectionPoolFindingTests`. Recount on `main` rather than trusting this arithmetic; that is the rule this
phase learned three times.

---

## 8b. The second half — the remaining concurrency markers

Work order taken from § "Question 2" rather than chosen again: the row-lock and MVCC deadlock-detector
markers first, because they sit on the provider's default path (MVCC is the default, and
`MvccTransactionalStore` constructs `RowLockManager` unconditionally).

**Reachability re-confirmed before touching anything, because a reachability record is a claim like any
other.** The SQL path does reach the row locks: `SELECT … FOR UPDATE` is planned in
`QueryPlanner.Clauses.cs` → `IteratorLocking.cs:91` → `MvccTransaction.GetForUpdate` → `GetWithLock` →
`IRowLockManager.AcquireLock`. So this is not a library-only corner.

### 8b.1 Row locks release, and no waiter's continuation runs on the wrong thread — PR 1

**Three markers closed, and a fourth defect found that was in no audit.** All three markers reproduced
first, on unfixed code, with the numbers the ledger recorded:

| Marker | Observed before the fix |
|---|---|
| `RowLockHandle.Dispose` releases nothing | `IsLocked` still **True** after `Dispose` |
| …from the caller's side | second transaction got `RowLockException`, "Lock held by transaction 1" |
| `ReleaseAllLocks` runs the waiter's continuation inline | **1023 ms** for a 1 s continuation (1007 ms when first recorded) |

**What the fix is.** `RowLockHandle.Dispose` was an empty body carrying the comment *"Individual lock
release is handled by the manager internally"* — which was false; there was no such path. `IRowLockManager`
gained `ReleaseLock(key, transactionId)`, releasing **one holder** rather than the entry, and running the
same grant-to-waiters path `ReleaseAllLocks` does. The two `TaskCompletionSource<bool>` constructions got
`RunContinuationsAsynchronously`.

**The engine deliberately does not use the new path, and that is now written down where it will be read.**
`MvccTransaction.GetWithLock` acquires a handle and never disposes it: under two-phase locking a row lock
is held to the end of the transaction and released by `ReleaseAllLocks` at commit or rollback. That is
correct, and it is also why this marker was invisible in the engine's own tests. The remark on
`IRowLockManager.ReleaseLock` says so, so the next reader does not "fix" the engine into an isolation bug.

### 8b.2 A second inline-continuation defect, found by grepping for the shape — **in no audit**

Rule: *fix every path with the shape, not the one the finding names.* Of the **eight**
`TaskCompletionSource` constructions in `Sources/`, six already passed `RunContinuationsAsynchronously`.
The two that did not were `RowLockManager` — the marker's own site — and
**`TransactionWaitQueue.EnqueueAndWaitAsync`**, which no finding mentions.

Measured the same way, *time the wrong thread*: **`cts.Cancel()` took 1004 ms** for a cancelled
transaction whose continuation sleeps 1 s. `CancellationToken.Register` callbacks are synchronous, so the
thread that cancels one waiting transaction pays for whatever that transaction does next.

**Calibrated in both directions, because the two sites are not equally severe.** The row-lock one completes
its waiter from *inside* `m_syncLock`, so the releasing thread ran foreign code under the manager's lock.
The wait-queue one does not: `SignalNext` signals a wait handle and the
`ThreadPool.RegisterWaitForSingleObject` callback completes the source on a pool thread, so only the
**cancellation** path is measurable — and no production code enters the queue at all. `WaitInQueueAsync`
has no caller outside tests; only the signal side (`SignalNextWaiting`, on every commit and rollback) is on
the hot path, and it finds the queue empty. So this is public API a consumer can reach and the engine
cannot, which is the third defect in this area of that kind. Fixed anyway — it is one constructor argument
and the alternative is leaving a known-wrong idiom in place.

### The revert counts

| Fix reverted | Tests red |
|---|---|
| `RowLockHandle.Dispose` | **5** |
| `RowLockManager` completion source | 1 |
| `TransactionWaitQueue` completion source | 1 |

The 5 is the point. The ledger's own two tests were the only ones covering handle release, so the revert
would have painted **2**; three cases added with the fix took it to 5. They are deliberately controls on
*the fix* rather than on the finding:

- **a shared lock with two holders** — a fix that dropped the whole `LockEntry` would pass both ledger
  tests and silently unlock a row another transaction still holds;
- **a queued waiter** — releasing through the handle must run the grant path, or the waiter sits until it
  times out;
- **a repeat `Dispose`** — once the row has been granted onward, disposing the stale handle again must not
  release the *new* holder's lock.

**Ledger after this PR: 63 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 77 suppressed entries** (from
66 + 14 = 80). The concurrency area goes from 15 markers to **12**, plus the one `TestCase` property —
which is in `Concurrency/SharedDatabaseTwoEnginesProbeTests.cs:189`, the `MVCC=false` divergence, and is
the marker that sat on a continuation line and broke the count three times.

### 8b.3 The MVCC deadlock detector, fed — PR 2

The last marker on the provider's default path. `DeadlockDetector` was **already complete** — wait-for
graph, cycle finding, four victim strategies — and already constructed unconditionally by
`MvccTransactionalStore`, which is what made this a one-sided defect: `TransactionCompleted` was called on
commit and rollback, but **no edge was ever added**. The check was an empty `if` body carrying the comment
*"This is a simplified check - full implementation would track all holders"*.

**Reproduced first.** The AB/BA case: both transactions got `TimeoutException` after the full 2 s and
neither got a `DeadlockException`, exactly as the ledger recorded.

**What the fix is.** `IRowLockManager` gained `GetHoldingTransactions(key)` — the inverse of the existing
`GetLockedKeys(transactionId)`, and literally the "track all holders" the comment asked for. A waiting
acquire now registers an edge per holder before it waits and removes them in a `finally`, so a wait that
times out does not leak its edges. **The async path had not even fetched the detector**, so a deadlock
between two `await`ing waiters was equally invisible; it gets the same treatment.

**The victim decision, made explicitly rather than inherited.** `RegisterWait` throws on whichever
transaction closes the cycle, and the detector's chosen victim may be a *different* transaction — its own
tests pin that (`Oldest` strategy: tx3 registers, victim is tx1). That cannot be honoured here: the other
participants are blocked inside `AcquireLock` and there is no mechanism to abort a transaction from another
thread. So **the transaction that closes the cycle is the victim**, and the exception is re-thrown with its
own id in `VictimTransactionId` so the report matches who actually aborts. The deadlock genuinely resolves
— that transaction fails, its caller rolls back, its locks go. The strategy stays meaningful for the
detector's on-demand and background APIs, where nobody is blocked. Written up for consumers in
`WitSQL.md` § 15.0.2.

**Revert count: 2**, and the third test is deliberately *not* one of them:

| Test | On revert |
|---|---|
| the AB/BA marker | red |
| a deadlock is reported *before* the timeout, not after | red |
| an ordinary lock wait is **not** reported as a deadlock | **green — it is the attribution control** |

The timing test is the one that earns its place. The marker's own assertion would also pass if the report
arrived *after* the full wait, leaving the user-visible cost unchanged; it gives the wait a 30 s timeout and
requires the answer in under 1 s. The revert makes that visible another way: the fixture's runtime went
from **6 s to 35 s**, because the reverted code waits the timeout out. And the third test exists because a
wait-for edge is now added on *every* waiting acquire, so the cheap way to pass the first two would be to
cry deadlock whenever anyone waits.

**Ledger: 62 `[Ignore(…)]` + 14 = 76.** Concurrency area **11 markers** plus the one `TestCase` property.

---

## 9. The four standing rules, applied to this phase

1. **The oracle settles attribution, never desirability.** SQLite is single-file and serialises
   writers, and it *also* separates `:memory:` databases per connection unless asked for
   `Cache=Shared` — so it agrees with WitDatabase on § 3 and § 6. That agreement is **not** a defence:
   the target is drop-in for PostgreSQL and SQL Server, where a connection pool over one database is
   the ordinary case. The oracle is used above only where it belongs — to confirm that separate
   `:memory:` databases is a defensible *design*, which is different from it being desirable here.
2. **Prove by execution.** Every claim in §§ 3–6 is an observed verdict, and the observation is in
   this document before any fix. Several started as confident readings of the code and only one
   survived unchanged: the LSM refusal came from a file the reading had not predicted, and
   `FileLocking=false` turned out to affect in-process locking, which the option's name denies.
3. **A record of a past fix is a claim.** The parallel-mode marker was such a record, carried for
   months, and it named the wrong cause (§ 5). `LockManager`'s own class comment — "FileLock is only
   used for write operations to coordinate between processes" — is accurate about the class and false
   about the system, because nothing reaches that constructor. Both were re-decided by running
   something.
4. **Build the control into the instrument.** Six controls, § 2, all green on the first run. The
   attribution control is the one that changed a verdict: without running the marker's own statement
   with parallel mode removed, the misattribution would have survived this phase too.

   **And the instrument was wrong before its subject was — the sixth time in this project.** The LSM
   probe asserted `Threw` unconditionally, generalising a Windows measurement into a claim about the
   model. It went **red on the Linux runner**, which is the *good* direction: the failure was the
   instrument over-claiming, and it surfaced a data-corruption defect (§ 3a) that no control on this
   machine could have caught. Two things to carry forward:

   - **A verdict without a platform on it is a guess about the other platform.** Every report line in
     the model probe now carries `[windows]` or `[unix]`, and the assertions branch on
     `OperatingSystem.IsWindows()` rather than assuming one model.
   - **A second machine is *still* the only thing that settles this — third time now.** Phase 4 had
     two (the scan/compaction race and `LsmParallelWriter.FlushAllAsync`); this is the third, and the
     first where the two machines disagree about *by-design behaviour* rather than about a race. The
     rule needs widening: CI is not only the arbiter of timing, it is the arbiter of **platform**.

---

## 10. Acceptance for the phase

From `Docs/NEXT-SESSION-PLAN.md` § "Phase 5", with the state of each after the first half:

- ⏳ **Every marker fixed with a deterministic test, or reclassified with the model written down.**
  *Partly.* The model **is** written down — `WitSQL.md` § 15.0 and § 15.0.1, stated as what a consumer may
  rely on rather than as an implementation note. **15 markers plus one `TestCase` property remain**, and
  they are the second half.
- ✅ **Parallel mode either supported and covered, or removed** — decided: **supported** (§ 5), and its
  marker was closed rather than reworded.
- ✅ **Two connections in one process see each other's committed writes**, deterministically (§ 7b) — and a
  second *process* is refused with a **typed** exception, proved across a real process boundary (§ Q4).
- ✅ **CI green on both frameworks, with no timing-dependent gate introduced.** This needed a correction
  *during* the PR rather than after it — see below.
- CI green on both frameworks, with no timing-dependent gate introduced. This needed a correction
  during the PR rather than after it: the parked-collaborator probe first gave the second writer a
  2-second budget to enter the store, which on a loaded runner would have reported an **unserialised**
  writer as serialised — a timing-dependent gate of exactly the kind phase 3 § 17 removed and phase 4
  was told not to reintroduce. Fixed by taking thread-start latency out of the measurement with an
  explicit `Started` handshake and raising the entry budget to 10 s. The asymmetry is now the safe
  way round: a genuinely serialised writer stays out however long it is given, so a generous budget
  costs seconds in the serialised case and removes the flake in the other. **Watch this on the first
  CI run anyway** — a second machine is the only thing that settles a race, and it has overturned a
  local verdict twice in this project.
