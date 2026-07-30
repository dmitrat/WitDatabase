# Phase 5 — Concurrency and concurrent access

> **Status: the audit is done for questions 1 and 3; the fixes have started.** PR 1 (this document plus
> instrument A) established the concurrency model by execution and changed **no** production line,
> deliberately: the plan's shape is *audit the area, then work it*, and phase 4 showed what happens when
> a phase fixes a list instead of a subject — six of its thirteen defects were in no audit. PR 2 is the
> first fix, § 5a.
>
> | PR | Subject | Outcome |
> |---|---|---|
> | #52 | The plan, and instrument A — the concurrency-model probe | The model established; three defects in no audit; one marker refuted; CI found a fourth defect the dev machine could not |
> | #53 | `KEY` usable as an identifier, and the keyword corpus | Marker closed (66 → 65). The corpus then measured the class at **118** keywords and handed it to phase 7 |
> | #54 | One engine per database, enforced | § 3a and the `EnableWal` hole closed by an explicit guard; `FileLocking=false` no longer removes write serialisation. **Breaking — ships as 5.0.0** |
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

- **Question 2 — reachability of the 10 `core-concurrency` markers from the provider.** Not yet
  measured. The one already known — the pool permit leak — is real but unenterable, and now doubly
  so: nothing reaches the pool at all.
- **Question 4 — a second machine, and a second process.** Every verdict above is from this machine, and
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

**Why this PR stops at the measurement.** The fix is a real architecture change — moving the schema
catalog from session scope to database scope and adding a `WitSqlEngine` constructor that accepts one —
and the shape of it was only knowable *after* these results. Landing the instrument first is the same
order phase 4 used, where the instrument PR preceded every fix and each fix then had a verdict to aim at.

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

Unchanged from `Docs/NEXT-SESSION-PLAN.md` § "Phase 5", with one item now answerable:

- Every marker fixed with a deterministic test, or reclassified as unreachable or by-design **with
  the model written down**. § 3 is the model as it stands and § 8 is the model as intended; **both**
  belong in `WitSQL.md`, stated as what a consumer may rely on rather than as an implementation note.
- Parallel mode either supported and covered, or removed — **decided: supported** (§ 5).
- **New, from § 8:** two connections in one process see each other's committed writes, with a
  deterministic test; and a second *process* is refused with a typed exception rather than a raw
  `IOException` carrying a Windows sharing message.
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
