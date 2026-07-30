# Phase 5 — Concurrency and concurrent access

> **Status: the audit is under way.** PR 1 (this document plus instrument A) establishes the
> concurrency model by execution and settles two of the four questions the phase-5 plan set. No
> production line has been changed yet, deliberately: the plan's shape is *audit the area, then work
> it*, and phase 4 showed what happens when a phase fixes a list instead of a subject — six of its
> thirteen defects were in no audit.
>
> **The target model, decided 2026-07-30 (§ 8): one process, one engine per database, many
> connections, one writer at a time.** Cross-process access is out of scope by design; concurrent
> connections *within* one host process are the supported shape, because the goal is drop-in for
> ASP.NET Core services where the `DbContext`s are many and the host is one.
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
for defects at all: it **measures the model**. Two fixtures, 26 tests, all green, each pinning an
observation rather than a wish.

- `Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/ConcurrencyModelProbeTests.cs`
  — 18 tests, asking every question through the ADO.NET surface, which is also what answers
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

| Configuration | Second concurrent opener | Enforced by |
|---|---|---|
| btree over a file (default) | `IOException` — "used by another process" | `StorageFile` → `FileShare.None` |
| LSM over a directory | `IOException` on `<dir>/wal.log` | the write-ahead log, **not** the store |
| `Read Only=true` | `IOException` — the setting is dropped, see § 4 | `FileShare.None` again |
| `Data Source=:memory:` | Opens, and is **a different database** | nothing is keyed by connection string |

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

### The cross-process mechanism exists and is unreachable

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

### `FileLocking=false` removes the only write serialisation there is — **a defect, in no audit**

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
unquoted reserved identifiers on a round trip. This is a statement a **user** writes by hand, and
`Key` is an ordinary column name in every dialect the project targets. It belongs to phase 7.

**Verdict: parallel mode is a supported configuration.** The marker's reason string has been rewritten
to name the real cause; the test stays ignored because the statement still does not parse, so the
ledger is unchanged at 66 and now accurate.

---

## 6. The access question — the pool cannot serve the shape it exists for

`ConnectionPool` exists, is `sealed`, is keyed by connection string, has roughly thirty tests, and
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
the property the pool exists for: that the connections it hands out address the same data. Thirty
green tests, and the load-bearing property is untested.

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
  subject. Not yet designed; it wants its own PR and its own instrument, because "two connections see
  each other's committed writes" is the property to test and nothing in the suite tests it today.

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

- **Cross-process access is out of scope by design.** `FileLock`, the file-locking branch of
  `LockManager`, and the `LockHandle*Combined` handles that exist to pair a process handle with a file
  handle are dead weight — unreachable today (§ 3) and unwanted tomorrow. `FileShare.None` is not the
  defect; it is the enforcement of a deliberate limit, and it should say so with a typed exception
  instead of a raw Windows sharing violation.
- **`FileShare.None` *is* in the way of the supported shape.** A scoped `DbContext` is one connection
  per request, and a host serves requests concurrently, so N live connections to one database inside
  one process is the ordinary case — exactly what the model probe measured as an `IOException` today.
- **The mechanism needed is not `ConnectionPool`.** The pool creates an independent `WitDbConnection`,
  and therefore an independent engine, per pooled entry — which is why it collides with itself over a
  file (§ 6). What the ASP.NET Core shape needs is the opposite: **one shared engine per data source
  within the process, with connections as lightweight handles onto it.** The pool's ~30 tests describe
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
