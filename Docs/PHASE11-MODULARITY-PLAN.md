# Phase 11 - the modular structure

**Opened 2026-08-03**, on `phase11-modularity`, after every phase of the plan closed with 11.2.0.

WitDatabase is built as a construction kit, like WitRPC: a workload picks a store, a transaction model,
a parallel mode, encryption, a journal, a cache. **The combinations have never been enumerated, let
alone run.** This phase enumerates them, runs them, and says which work, which refuse, and which quietly
do something else.

## 1. Why this phase, and what it inherits

Phase 10 found that **every LSM connection-string parameter was inert** - `MemTableSize`, `SyncWrites`,
`EnableWal`, `BlockSize`, `CompactionTrigger`, the block cache. The cause was structural rather than
LSM-specific: `WitDatabaseBuilder.BuildLsmStore` built the store directly and accepted only a ready-made
options object, while the connection-string mapping lived in `ProviderRegistration`, on a path the
builder does not take. **Two construction routes that disagree.** (`PHASE10-PERFORMANCE-PLAN.md` § 18.1.)

That is the phase-7 defect class - *accepted but not enforced* - one level out: accepted at the
**configuration** surface rather than the DDL one. Nothing had checked whether `Cache`, `Journal`,
`PageSize`, `Parallel Mode` and the rest fare any better.

**Acceptance for this phase:**

1. Every combination either works, or is refused **at open** with a legible message. No "accepted and
   ignored".
2. Every connection-string keyword is either **proved to reach the engine**, or removed.
3. What is supported and what is not is written in `WitSQL.md`, where a consumer will read it.

## 2. The instruments

Two, because "does the setting arrive" and "does the engine that received it still answer correctly" are
different questions, and phase 6 / phase 7 both showed that asking one question gets one answer.

### 2.1 Instrument A - the configuration census

`Sources/Providers/OutWit.Database.AdoNet.Tests/Modularity/ConfigurationCensusTests.cs`

For each keyword: build the database **through the real connection-string path** with the baseline value
and with a different value, take a **structural fingerprint** of the built object graph by reflection
(runtime type at every reachable field, value at every scalar; fields only, never properties), and
compare. Identical engines mean the keyword reached nothing.

The verdicts are `REACHES`, `INERT`, `ECHO ONLY` (the only difference is a copy of the keyword's own
text, kept as text) and `REFUSED`.

**Controls, in both directions, asserted rather than printed:**

- **Positive** - `Store`, `MVCC`, `MemTableSize`, all proved to reach. If one reports INERT the
  fingerprint is blind and no INERT verdict in that run may be believed.
- **Negative** - `Nonexistent Keyword=42`, which reaches nothing by construction. If it reports REACHES
  the fingerprint is noisy and no REACHES verdict may be believed.
- **Per-knob noise calibration** - the baseline side is built **twice**, in two directories, and every
  path that already differs between two builds of the same configuration is excluded before the variant
  is compared. That is what keeps temporary paths and handles out of the verdict; the negative control is
  what proves it worked.

Both controls held on the first run and on a repeat, and the two runs produced identical verdicts.

**What the census cannot say** is whether a setting that arrives is then honoured. The isolation level is
the standing example: it is stored on the transactional store, so it REACHES, and it is applied by
nothing. Reaching is necessary and not sufficient.

### 2.2 Instrument B - the combination matrix

`Sources/Providers/OutWit.Database.AdoNet.Tests/Modularity/CombinationMatrixTests.cs`

153 cases: the cross product of store x transaction model x parallel mode x encryption (108), plus
thirteen orthogonal add-ons swept against each store, each asked two questions - does it answer like
every other configuration, and does the data survive a close and reopen.

**The workload is written so its answers do not depend on the configuration.** A configuration without
transactions writes the committed row directly and never writes the rolled-back one, so every
combination must produce byte-identical answers. Rows are compared by **scanning them back**, never by
`COUNT(*)` - on this engine that is a cached counter and has disagreed with the rows before.

**The control is the reference itself**: the default configuration is asserted against **hard-coded
literals**, not against another run of the engine, so an engine-wide regression cannot make the matrix
agree with itself and pass.

**The control earned itself immediately.** Its first run went red - `8:45.50` expected against `8:42.75`
measured - and **the literal was wrong, not the engine**. Eighth time in this project that the instrument
was wrong before its subject.

Runtime: 153 cases in 10.4 s.

## 3. What the census found

Verdicts stable over two runs. `REACHES` for 25 keywords, and:

| Keyword | Verdict | What it means |
|---|---|---|
| `Cache=clock` vs `lru` | **ECHO ONLY** | The chosen key is written into the database header. No cache of that type is ever constructed. |
| `Journal=wal` vs `rollback`, MVCC **on** (the default) | **ECHO ONLY** | Same: the key reaches the header, the journal reaches no store. |
| `PageSize` | **INERT** | From a connection string. Works from the fluent builder. |
| `CacheSize` | **INERT** | Same. |
| `Parallel Mode` `Auto`/`Buffered`/`Latched`/`Optimistic` | **INERT between each other** | Four documented modes with four different descriptions, one behaviour - on both stores. |
| `Max Writers` on B+Tree | **INERT** | Reaches the LSM parallel store; the B+Tree concurrency options have no such field. |
| `Isolation Level` with `MVCC=false` | **INERT** | With MVCC on it reaches a field and is then applied by nothing (phase 6). |

`Parallel Mode=None` vs any other value REACHES - a wrapper appears. It is the four non-`None` values
that are indistinguishable from one another.

**One cause explains `PageSize` and `CacheSize`, and it is bigger than those two.** Every value from a
connection string arrives as a **string**, and `ProviderParameters.Get<T>` is
`if (m_values.TryGetValue(name, out var value) && value is T typed)` - no conversion. `Get<int>("PageSize")`
on the string `"16384"` fails the type test and **returns the default, silently**. The fluent builder
sets the same key as an `int`, so the same code works there. The LSM options escaped only because
`LsmOptions.FromParameters` parses strings by hand - which is what phase 10 built, one parameter set at a
time. Every other `p.Get("pageSize", ...)`, `p.Get("capacity", ...)`, `p.Get("readOnly", false)` in
`ProviderRegistration` has the same hole.

**This is the same defect as phase 10's, in a different coordinate**: not two routes that read different
places, but two routes that supply different *types*.

## 4. What the matrix found

**129 of 153 pass.** Every combination opens, runs the workload and returns identical answers - the
"opens and quietly answers something else" category is **empty**, which is worth stating plainly.

All 24 failures are in the reopen question, in three groups.

### 4.1 `Store=lsm` + `Transactions=false` + a parallel mode loses an acknowledged write

Four cases: `lsm tx=off par=auto` and `lsm tx=off par=buffered`, plain and encrypted. Seven rows survive
the reopen and **the last row written does not** - the write that reported success, then a clean close,
then it is gone.

`lsm tx=off par=none` survives. `lsm tx=mvcc par=*` survives. So it needs the LSM store, no transaction
layer, and a parallel wrapper.

This is the family of the 6.0.0 defect - `Store=lsm` + any parallel mode losing acknowledged writes -
in a configuration that one was not: there is no MVCC commit protocol here to blame, because there is no
transaction layer at all. **Not yet diagnosed.**

### 4.2 `Store=inmemory` with a file `Data Source` leaks the file handle

Eighteen cases. Reopening throws `IOException: the process cannot access the file ... because it is being
used by another process` - the **same** process, after the connection was disposed.

`WitDatabaseBuilder.BuildStoreFromRegistry` builds an `IStorage` for every store, which for a file data
source **opens the file**, and passes it to the provider factory as the `storage` parameter. The
in-memory store's factory ignores every parameter and returns `new StoreInMemory()`. Nothing owns the
storage, nothing disposes it, and the handle is held until finalization.

So the combination is not merely non-persistent, which would be honest: it is **unopenable a second time
in the same process**.

### 4.3 `Journal=wal` leaks the journal file handle

Two cases, one per store, and it is the ECHO ONLY census verdict with teeth.

`BuildTransactionalStoreInternal` calls `BuildJournal()` **before** it branches on MVCC.
`WalTransactionJournal` opens its file in its constructor. With `MVCC=true` - the default - the MVCC
store is built without a journal, and the object just built is dropped: never referenced, never disposed,
its file handle held for the life of the process.

`Journal=rollback` does not fail the same way (it opens lazily), and `Journal=wal;MVCC=false` does not
fail at all - there the journal is wired into `TransactionalStore` and disposed with it.

### 4.4 What the matrix did **not** find, and it is worth saying

**No combination opens, accepts every statement and answers something different.** All 108 cross-product
cases and every add-on produced byte-identical answers to the reference. The dangerous middle category is
empty. Every defect this phase found is in construction or in close, not in the answers.

## 4a. The fixes

Five, each with its red measurement taken before the fix existed.

| Defect | Fix | Proved by |
|---|---|---|
| `Store` decided whether any other keyword arrived | `ConfigureStore` forwards the parameter bag whether or not the store is named (`WithStoreParameters`) | `PageSize` census verdict INERT -> REACHES, both spellings |
| Numeric and boolean keywords silently defaulted | `ProviderParameters.Get<T>` converts text to the requested scalar and **throws** on a present-but-unreadable value | same |
| `Store=inmemory` held the data file open | storage is built through a `Lazy<IStorage>`, so a store that never asks for it never opens it | 18 matrix reopen cases |
| `Journal=wal` held the journal file open | the journal is built only on the branch that uses it, and the combination is now refused (§ 6) | 2 matrix reopen cases |
| `Store=lsm` + `Transactions=false` + a parallel mode lost the last row written | `LsmParallelWriter.Dispose`/`DisposeAsync` hand over the buffers that are still filling **before** closing the queue | 4 matrix reopen cases |

**The data loss, in one paragraph.** `LsmParallelWriter.Dispose` completed the buffer channel - which
drains what is already *queued* - and then disposed the thread-local slots, discarding every entry below
the size threshold. That is the tail of every workload. With MVCC the commit path calls `FlushAllAsync`
and hid it; with `Transactions=false` nothing does. Seven rows survived the reopen and the eighth did
not. The store now flushes before it closes the queue: **a store that accepted a write does not get to
discard it at close.**

Full suite after all five, with the CI filter: **green**, 10,406 tests across eight projects.

## 5. Work order

1. ~~Build both instruments and take the census.~~ **Done.**
2. ~~Fix § 4.3 and § 4.2 - two handle leaks.~~ **Done.**
3. ~~Diagnose § 4.1 - the data loss.~~ **Done, § 4a.**
4. ~~Decide and act on the census's inert settings.~~ **Two of three done, § 6.**
5. ~~Write the supported matrix into `WitSQL.md`.~~ **Done, § 14.10.**
6. Open: the parallel modes (§ 6.3), and the async builder route (§ 6.5).

## 6. Decisions

### 6.1 `Cache=clock|lru` - wired, decided 2026-08-03

`StoreBTree` takes an `IPageCache` now, and the builder constructs the one the configuration chose - for
the main store and for each secondary index store, each with its own instance, because a page cache is
bound to one storage. `WithCache(IPageCache)`, which was read by nothing, reaches the main store.
`Cache` moved from ECHO ONLY to REACHES in the census.

### 6.2 `Journal` with MVCC - refused, decided 2026-08-03

A journal is only reachable through the lock-based `TransactionalStore`. `Journal=…` with `MVCC=true` -
the default - or with transactions off is now refused at `Open` with a message naming the way out. That
is the phase's rule applied to itself: a setting that cannot be honoured is an error, not a silence.

### 6.3a Both measurements taken, 2026-08-03 - and they decide it

`MainStoreConcurrencyProbeTests` reuses phase 5's parking seam against a database built the ordinary
way: one writer parked inside a leaf split of the **main** store, a second let in, released, count what
survived. Controls in both directions in every pass - a bare store must be damaged (or the harness is
blind) and parking alone must destroy nothing.

**Measurement 1 - is `Parallel Mode=None` safe?** Five runs, completely stable:

| configuration | outcome |
|---|---|
| bare `StoreBTree` (positive control) | **damaged** 5/5 - 2-9 entries gone, usually with **no exception at all** |
| `BTreeConcurrentStore` (control) | clean 5/5 |
| database, MVCC, no parallel mode | clean 5/5 |
| database, lock-based transactions, no parallel mode | clean 5/5 |
| **database, `Transactions=false`, no parallel mode** | **damaged 5/5** - threw *and* lost a row |
| database, MVCC, parallel mode | clean 5/5 |

So the transaction layer - either one - does serialise the main store, and `Parallel Mode=None` is safe
**as long as transactions are on**. With `Transactions=false` there is nothing between two writers and
one leaf split, and the store is exposed exactly as a secondary index store was before 6.0.0. **A new
defect, and the keyword was load-bearing precisely where it should not have had to be.**

**Measurement 2 - what does serialising cost a single thread?** Five interleaved passes of 20,000
put+get, wrapped against bare, ratios `1.001, 1.070, 1.009, 0.996, 1.000` - **median 1.001**, four of
five within 1%. The 1.070 is the outlier and the same shape as phase 10's "4.13x regression" that a
second pass measured at 0.83.

**Decided: the B+Tree store is serialised unconditionally**, main store and index stores alike, exactly
as 6.0.0 already decided for index stores. It closes the `Transactions=false` hole, it costs nothing,
and it takes correctness off the list of things a connection string can switch off. Three tests that
pinned the old shape were inverted with the reason written into them.

**What is left of `Parallel Mode` is LSM write buffering** - a throughput choice, since `StoreLsm` locks
internally and is safe without it. That is what `WitSQL.md` § 14.10 now says. The four names remain four
spellings; building distinct mechanisms would be a phase, and nothing now depends on it for safety.

### 6.3b The setting is removed, 2026-08-03 - and the third measurement is why the mechanism stays

Dmitry's decision once § 6.3a landed: remove `Parallel Mode`, delete the public API rather than
deprecate it, and **measure the LSM write buffer before deciding whether the mechanism goes with it**.

`LsmWriteBufferingCostProbeTests`, three rounds per shape, interleaved, with the store's own counters
asserted so that a round which buffered nothing cannot pass as a null result:

| | ratio, buffered / direct |
|---|---|
| Straight into the store, one writer | 1.00 - noise, 0.77-1.02 across passes |
| Straight into the store, four contending writers | **0.80** (0.810 / 0.803 / 0.774) |
| Through a database, four writers, autocommit | **1.14** (1.177, 1.136) |
| Through a database, four writers, batches of 1,000 in a transaction | **1.04** |

**The win needs four threads inside the store at once, and a transaction layer will not let that
happen** - § 6.3a measured exactly that, from the other side. So through the engine the buffer only
costs, and a knob that selects it is a knob that makes things worse.

**What was removed:** the `Parallel Mode` and `Max Writers` keywords (refused at `Open`, not ignored),
`WitDbParallelMode`, the ADO.NET properties, EF's `UseParallelWrites`/`MaxWriters`, `ParallelMode`,
`ParallelModeOptions`, `KeyValueStoreFactory`, and the three builder extensions. **What stays:**
`LsmParallelStore`, public, for a caller who drives a store directly - which is the one place the 0.80
is real.

Also recorded on the way past, not chased: through the engine, four writers doing batches of 1,000 in a
transaction took **181 s** against autocommit's **61 s** for the same 100,000 rows. Concurrent MVCC
transactions contend badly, and that is a bigger number than anything this phase set out to find.

### 6.3 The four parallel modes - the reasoning, before the measurements above

Measured, not argued: **which concurrency mechanism you get is decided by the store, not by the keyword.**

| | `Parallel Mode=None` | any other value |
|---|---|---|
| `Store=btree` | bare `StoreBTree` | `BTreeConcurrentStore` - a reader-writer lock |
| `Store=lsm` | bare `StoreLsm` | `LsmParallelStore` - thread-local write buffers, background merge |

So on the B+Tree the honest name is `Latched` and on the LSM it is `Buffered`, and each store has exactly
one strategy. `Auto`, `Buffered`, `Latched` and `Optimistic` are four spellings of "make this store
thread-safe". `Latched` cannot mean what its XML doc says under any of them - the page-latch subsystem
was deleted as dead code in 6.0.0.

**What the options actually give**, which is the question worth answering before choosing:

- `None` -> non-`None` is a **real** difference and the only one: it decides whether the main store is
  serialised at all.
- The other three names differentiate nothing today, and building the differences would be a phase, not
  a fix - `ParallelModeOptions` also carries `FlushIntervalMs`, `LatchTimeout` and `UseOptimisticReads`,
  none of which the builder passes anywhere.
- The sharper question behind it: **is `None` safe for the shape this engine is designed for?**
  Since 6.0.0 secondary index stores are wrapped **unconditionally**, because a second *connection* is
  enough to corrupt a B+Tree leaf split - and the main store is wrapped only when a parallel mode is
  asked for. `StoreBTree` has no locking of its own. That is not a documentation question, and it is not
  answered here: it needs the deterministic parked-collaborator experiment phase 5 built, run against
  the **main** store with `Parallel Mode=None` and two connections.

Until that is measured, `WitSQL.md` § 14.10 states what is true - the mode is decided by the store, the
four names are spellings - rather than repeating the XML docs.

### 6.4 `Max Writers` on the B+Tree

Documented as LSM-only in § 14.10. Not refused: it is a throughput hint, not a promise, and refusing a
harmless hint would break connection strings that name it for a store they later change.

### 6.5 The async builder route disagrees with the sync one - new, not yet acted on

`WitDatabaseBuilder.BuildStoreInternalAsync` builds a `StoreBTree` for **every** configuration that is
not LSM: `Store=inmemory` and any custom registered store provider are ignored on that path. It is the
same two-routes-disagree shape again, in the third place this phase has found it. ADO.NET consumers do
not reach it - `WitDbConnection.OpenAsync` runs the synchronous `Open` - so it is a builder-API defect,
recorded here rather than fixed in the same pass.

## 7. Ledger

47 suppressed entries (33 `[Ignore(…)]` + 14 `Ignore =`) plus 2 `[Explicit]`, counted with the commands
on this branch. Unchanged so far by this phase.
