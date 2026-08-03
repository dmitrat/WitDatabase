# Phase 10 — Performance

The last phase in the plan. Its acceptance is **a measured baseline that can be trusted, and a
decision about what to optimise taken from it.** Optimising without a baseline is forbidden in this
project, and the prohibition is earned.

Opened 2026-08-02, on `main` at `7fd043a` (tag `v11.0.0`, all seven packages published, no open
PRs). Ledger counted with the commands rather than trusted: **33 `[Ignore(…)]` + 14
`[TestCase(… Ignore =)]` = 47**, plus 2 `[Explicit]` (grep says 3; one is prose inside another's
reason).

---

## 1. The instrument was audited before any number from it was believed

Every phase in this project has found its instrument wrong before its subject, so the benchmark
suite was audited first. Five defects, all proved by execution rather than by reading.

### 1.1 Nothing verified that the three engines compute the same thing

Every benchmark runs "the same" operation against WitDatabase, SQLite and LiteDB and times it.
Across all seven classes and 113 benchmark methods there was **not one assertion** — no check that a
query returned rows, and no check that the three engines agreed. A query answering zero rows
benchmarks as fast.

The missing control was built and run: each benchmark body invoked once, its return value rendered,
and the three engines compared per operation. **Result: every read benchmark agrees exactly** —
aggregates, queries, joins, index seeks and scans all return identical values on all three engines.
That is a real and reassuring result, and it is the first time it has been known rather than
assumed.

Two gaps the control also exposed:

- **The write benchmarks return `void`**, so the control can say nothing about them. Nothing anywhere
  checks that an `INSERT` benchmark inserted. In an engine whose history includes `Store=lsm`
  losing acknowledged writes on default settings, a write benchmark that cannot tell the difference
  between writing and not writing is a live hazard.
- **`COUNT(*)` is not the same work on the three engines** — see § 1.5.

### 1.2 A LiteDB benchmark threw on every run and reported `NA` from January onwards

`Index Seek (unique) x100 - LiteDB` interpolated a string *inside* the predicate lambda:

```csharp
m_liteCollection.FindOne(x => x.SKU == $"SKU-{id:D8}");
```

LiteDB translates the lambda to BSON and throws `NotImplementedException`. The January report carries
`NA` in that row for all four engine modes, and nobody noticed — in the class that measures the thing
indexes exist for. **Fixed** by hoisting the interpolation out of the lambda; the control now returns
100 on all three engines.

### 1.3 Six of seven classes measured a configuration no consumer runs

`WitDbEngineMode.Default` — plain `Data Source=…`, MVCC on, durable commit, B+Tree, which is what
every ADO.NET and EF Core consumer gets — was in the `[Params]` of `TransactionBenchmarks` only.
The other six measured `MVCC=false`, and the LSM modes also `SyncWrites=false`. **Fixed:** `Default`
is now in every class's matrix.

### 1.4 The `Ratio` column compared unlike operations

BenchmarkDotNet allows one baseline per class unless the benchmarks are split into categories, and
each class carried exactly one `[Benchmark(Baseline = true)]`. So every operation in a class was
rated against one unrelated operation. In the January index report a 20-iteration seek is reported
as **"2.74x faster"** than a 100-iteration seek — a ratio between two different amounts of work.

**Removed**, rather than moved to LiteDB as the plan proposed: with one baseline per class, *any*
choice of baseline produces the same category error. Until the classes carry `[BenchmarkCategory]`
the honest report has no `Ratio` column at all, and ratios are computed per operation from the `Mean`
column instead. Splitting 113 methods into categories is the correct fix and is deliberately not
done here.

### 1.5 `COUNT(*)` compares a counter read against counting

Measured, not read off the source — WitDatabase's `COUNT(*)`, B+Tree, January report:

| N | WitDatabase | SQLite | LiteDB |
|---|---|---|---|
| 1,000 | 0.0006 ms | 0.0630 ms | 0.2323 ms |
| 10,000 | 0.0006 ms | 0.0612 ms | 3.2702 ms |

**Flat in N to four decimal places** — an O(1) counter read, not a scan. LiteDB's scales 14x for 10x
the rows, which is what counting looks like.

Both directions have to be stated, per the standing rule. *As an engine result it is not a scan
comparison at all* and must never be quoted as one. *As a product result it is real* — a consumer
issuing `SELECT COUNT(*)` genuinely gets an answer in under a microsecond. The caveat that belongs
with it is this project's own: the counter is separate state from the rows, and after a crash the
two can disagree.

---

## 2. Two claims in the record were wrong, and both were load-bearing

### 2.1 The three "missing" benchmark projects were deleted, not untracked

`Docs/NEXT-SESSION-PLAN.md` § C states that `Comparison.Benchmarks`, `Core.Tests.Benchmarks` and
`EntityFramework.Benchmarks` "exist only as `bin`/`obj` and **have never been tracked by git**", and
concludes that the historical numbers "cannot be reproduced".

They were tracked. All three were deleted in **`5b8fff9`, 2026-01-02, "redundant benchmark
removed"** — 9,038 lines, including `FullComparisonBenchmarks.cs` at 1,667 lines. They are
recoverable in full with `git show 5b8fff9^:<path>`.

What actually follows is different from what the plan concluded: nothing is lost, and the deletion
looks deliberate and reasonable, because **the surviving `OutWit.Database.Benchmarks` already
references LiteDB 5.0.21 and Microsoft.Data.Sqlite 9.0.6 and compares against both in every class**.
The comparison did not live in the deleted project. What the deleted project held that the survivor
does not is mixed-workload, concurrent-access and parallel-mode shapes — worth recovering
deliberately if they are wanted, not worth restoring wholesale.

### 2.2 The 78 benchmarks had been run — the reports were on disk the whole time

The plan and the project memory both say 78 benchmarks (aggregates, queries, joins, indexes,
updates) "have never been run". Every one of the seven classes has a full report in
`BenchmarkDotNet.Artifacts/results/`, dated **2026-01-04**, alongside `TransactionBenchmarks` from
2026-07-26. The directory is `.gitignore`d, which is why the reports never travelled and why the
claim survived: they existed only on this machine and nobody opened them.

The count was also wrong. The seven classes hold **113 benchmark methods**; the six non-transaction
classes hold 99, not 78. "78" is the sum of aggregates + queries + joins + indexes, leaving out the
updates and inserts that the same sentence names.

**The honest restatement:** the discriminating workloads were measured seven months and nine
releases ago, the results were never read, and they describe an engine that has since had phases
4–9 land on its write path, lock paths and statement path. They are a **map, not a baseline**.

---

## 3. What the January map points at

Recomputed per operation from the raw `Mean` column, since the suite's own `Ratio` column is
unusable (§ 1.4). All B+Tree mode, ShortRun, Ryzen 9 5950X. **Stale by nine releases — treated as
hypotheses to re-measure, not as findings.**

### The one that matters most: `LIMIT` does not short-circuit

`SELECT * FROM Users LIMIT 100`, and the equivalent on each engine:

| N | WitDatabase | SQLite | LiteDB | WitDatabase allocated |
|---|---|---|---|---|
| 1,000 | 0.143 ms | 0.074 ms | 0.150 ms | 402 KB |
| 10,000 | **3.240 ms** | 0.078 ms | 0.160 ms | 2,635 KB |

Both competitors are **flat in table size** — they stop after 100 rows. WitDatabase grows **22.7x
for 10x the rows**, and allocates 6.6x more doing it. The shape says the whole table is materialised
before the limit is applied.

Two things make this the strongest lead in the set:

- **It is on the default read path**, not on LSM or a tuned mode — every consumer meets it.
- **It is invisible at small N.** At 1,000 rows WitDatabase is *faster than LiteDB* (0.143 against
  0.150). The defect only appears as the table grows, which is exactly when a user notices.

It also matches a mechanism already recorded in the plan and never measured: `IteratorSort`,
`IteratorGroupBy`, `IteratorHashJoin` and the `StatementExecutor.Select` fast path all build
complete result sets in memory with no spill and no row budget.

### The rest of the map, in order of size

- **LSM writes**: 17–45x slower than LiteDB on inserts, worst at `LsmParallelAuto`. The known
  non-linearity, unchanged.
- **Composite index query**: 6–8x slower than LiteDB, ~4x its allocation, in every mode.
- **Index seek (non-unique)**: ~6x slower than LiteDB, ~3x its allocation.
- **`UPDATE` by indexed column, LSM**: 13–19x.

### What does not survive as stated

The memory records "WitDatabase allocates ~30% less than LiteDB" as a result that generalises,
because allocation is not distorted by P/Invoke or by document-vs-relational framing. **It does not
generalise** — it was measured on `TransactionBenchmarks` alone. On the index and query paths the
January report has WitDatabase allocating 3–11x *more* than LiteDB, and on the unique index seek
207 MB against SQLite's 53 KB. The allocation claim is true of the write path and false of the read
paths, and it was being quoted without that boundary.

---

## 4. Method for the baseline

The rule this phase is built on: **one timing run lies.** Twice in the previous session a single run
reported the opposite of what repeated interleaved runs reported.

- Every sweep is run **at least twice** and the spread between passes is reported, not an average of
  the two.
- Where two ranges overlap the finding is **"no measurable difference"**, never the direction of the
  means.
- The full matrix is five engine modes across two or three sizes in seven classes — roughly 1,175
  benchmark cases, hours per pass, which is what makes a second pass unaffordable and a single pass
  tempting. `BenchmarkSweep` now narrows the matrix from the environment
  (`WITDB_BENCH_MODES`, `WITDB_BENCH_SIZES`) so that running the same sweep twice is cheap. Both
  directions of the selector were verified: it narrows as asked, and it **throws** rather than
  silently measuring nothing when the selection is empty.


---

## 5. The baseline

Taken 2026-08-02 on `main` at `7fd043a` plus the instrument fixes above. Ryzen 9 5950X, .NET 10.0.8,
BenchmarkDotNet 0.15.8, `--job short --inProcess`, `MemoryDiagnoser` on. **`EngineMode=Default`** -
plain `Data Source=…`, which is MVCC on, durable commit, B+Tree, and is what every ADO.NET and EF
Core consumer gets.

**Every sweep was run twice** and both passes are reported. Of roughly 110 readings in the broad
sweep, 8 moved more than 10% between two identical passes - `Tx Rollback` on SQLite moved 50%,
`UPDATE RETURNING` on LiteDB 41%. Nothing below rests on any of those. The numbers that carry the
findings repeated to within 5%.

### Where WitDatabase stands against LiteDB, in the default configuration

**Ahead** (WitDatabase faster; both passes):

| operation | WitDatabase | LiteDB | |
|---|---|---|---|
| Sequential reads x100 | 0.36-0.42 ms | 8.7-9.0 ms | **~22x faster** |
| Transaction rollback (100 ops) | 0.475 ms | 7.6-7.9 ms | **~16x faster** |
| Point query by PK x100 | 0.24-0.26 ms | 1.31-1.40 ms | **~5.4x faster** |
| Index range scan (`BETWEEN`) | 1.41-1.44 ms | 5.0 ms | ~3.5x faster |
| Bulk `UPDATE` | 3.10-3.19 ms | 10.6-10.9 ms | ~3.4x faster |
| Single transaction, 100 `INSERT`s | 2.87-3.04 ms | 7.3-7.9 ms | ~2.5x faster |
| `SELECT … LIMIT 100` | 0.088 ms | 0.148 ms | ~1.7x faster |
| aggregates, scans, projections | | | 1.2-1.8x faster |

**Behind**:

| operation | WitDatabase | LiteDB | |
|---|---|---|---|
| **Index seek on a UNIQUE index, x100** | **48.9-51.1 ms** | **2.06-2.19 ms** | **~23x slower, 33x the allocation** |
| `INSERT` without a transaction, 100 rows | 206 ms | 10.6-12.3 ms | ~17-19x slower (but **3.3x faster than SQLite**) |
| `UPDATE` by indexed column | 3.54-3.64 ms | 0.87-0.95 ms | ~3.9x slower |
| `ORDER BY` at 10,000 rows | 55.8-57.1 ms | 23.6-23.9 ms | ~2.4x slower |
| Index seek, non-unique, x20 | 19.9-19.7 ms | 8.3-9.1 ms | ~2.4x slower |
| Composite index query | 9.9-10.0 ms | 3.8-3.9 ms | ~2.6x slower |
| `INNER JOIN` over 4 tables | 0.511 ms | 0.187 ms | ~2.7x slower |

Against **SQLite** the shape is the expected one and both halves of it are real: WitDatabase is
**2-3x faster on every write and transaction path** (where SQLite pays P/Invoke per call on a few
microseconds of work) and **4-19x slower on scans and aggregates** (where that overhead amortises to
nothing and a native engine shows its speed). Neither half is an engine result on its own; both are
what a .NET consumer actually experiences.

### One recorded claim does not survive the default configuration

The memory and the plan both carry "WitDatabase allocates ~30% less than LiteDB" as a result that
generalises. Measured here it is **mode- and workload-specific and does not hold as stated**. It came
from `TransactionBenchmarks` at 500 inserts with `MVCC=false`. In `Default` at 100 inserts
WitDatabase allocates 984 KB against LiteDB's 776 KB - **27% more**, not 30% less - and on the index
paths it allocates 1.9-3.4x LiteDB, rising to **33x** on the unique-index seek. It remains true on
bulk update (0.38x) and mixed transactions (0.56x). The honest form is per workload, never as a
property of the engine.

---

## 6. The decision: optimise the unique-index equality seek

**The engine already contains a fast implementation of exactly this operation, and one of the two
paths to it is 200x more expensive than the other.**

Both of these fetch one row by an indexed equality predicate, in the same configuration, in the same
sweep, and both are **flat in table size**:

| | per lookup, time | per lookup, allocated |
|---|---|---|
| `WHERE Id = @id` — primary key | **0.0025 ms** | **5.4 KB** |
| `WHERE SKU = @sku` — UNIQUE secondary index | **0.489 ms** | **1,253 KB** |
| | **~198x** | **~233x** |

The evidence that makes this the target rather than a guess:

- **It is not a missing index and not a scan.** Cost is constant in table size: 48.85 ms at 5,000
  rows and 49.02 ms at 20,000 rows, with allocation of **125,327 KB at both** — identical to within
  0.2 KB across a 4x change in table size. A scan would grow; this is a fixed per-seek cost.
- **It is stable.** Two passes at each size, spread 0.7-4.5%.
- **It is on the default path**, not on LSM or a tuned mode — every ADO.NET and EF Core consumer
  meets it.
- **It is the only place WitDatabase is more than 4x behind LiteDB on a read**, and it is behind
  SQLite by 9.2x at the same time, so neither of the two standing caveats (P/Invoke on the SQLite
  side, document semantics on the LiteDB side) explains it away.
- **The comparison is internal**, so no cross-engine caveat applies at all: the primary-key path in
  the same engine, same benchmark suite, same run, does the same logical work for 1/233rd of the
  allocation.

1.25 MB allocated to return a single row that the engine can already return for 5.4 KB is a defect
signature, not a tuning opportunity. **The next step is to find where that fixed cost is
constructed** — profile the secondary-index seek path against the primary-key path and account for
the difference — and nothing should be optimised before that comparison is in hand.

**Second, smaller target: `ORDER BY` is superlinear.** 1.79 ms at 1,000 rows and 57.1 ms at 10,000 —
**31x for 10x the rows**, against LiteDB's 13.4x and SQLite's 7.6x, allocating 30 MB. That matches
the recorded and never-measured mechanism that `IteratorSort` materialises completely with no spill
and no row budget.

**Explicitly not a target:** the trivial-insert gap to LiteDB, per the standing rule. And
`INSERT` without a transaction, at 206 ms for 100 rows, is the price of durable autocommit that
phase 4 chose deliberately — it is 3.3x *faster* than SQLite doing the same thing, so it is being
paid for correctness rather than lost to a defect.

### What the January map got wrong, which is why the baseline had to be re-measured

The strongest lead in the seven-month-old reports was that `SELECT … LIMIT 100` did not
short-circuit: 0.143 ms at 1,000 rows growing to 3.240 ms at 10,000 while both competitors stayed
flat. **It is fixed on current `main`** — 0.088 ms at 1,000 rows and 0.088 ms at 10,000, allocation
252.7 KB at both, flat and 1.7x faster than LiteDB. Something in phases 4-9 repaired it and no
record anywhere mentions it.

Optimising from the stale map would have meant fixing a bug that no longer existed. That is the
entire argument for the rule this phase opened with.

---

## 7. Instrument work deliberately left undone

- **`[BenchmarkCategory]` on 113 methods**, which is what would let the `Ratio` column return
  honestly (§ 1.4). Ratios are computed from `Mean` outside the suite in the meantime.
- **The write benchmarks return `void`**, so the equivalence check cannot compare them (§ 1.1).
  Making them return a verifiable row count would close the last hole in the control, and matters
  more here than in most projects: this engine has lost acknowledged writes before.
- **The deleted `Comparison.Benchmarks`** held mixed-workload, concurrent-access and parallel-mode
  shapes that nothing replaces (§ 2.1). Recoverable from `5b8fff9^` if those shapes are wanted.
- **`--inProcess` was used** to make two passes affordable (~8 s per case against ~34 s). Both passes
  used it, so the spread and the engine-to-engine comparisons are sound, but these numbers are not
  directly comparable to the out-of-process January ones.

---

## 8. The unique-index seek, diagnosed

The decision in § 6 was to localise the fixed per-seek cost before changing anything.
`IndexSeekAnatomyBenchmarks` does that: eight shapes, each fetching exactly one row out of the same
table 100 times from the same seeded key sequence, so the shapes differ only in the one property
each is named for. Every shape returns 100 and the equivalence check confirms it, so none of them is
quietly measuring a lookup that found nothing.

### The cost is invariant to everything it could plausibly depend on

Default mode, 5,000 rows, allocated per 100 lookups, two passes agreeing within 10%:

| shape | mean | allocated |
|---|---|---|
| PK equality | 0.258 ms | 569 KB |
| PK equality, narrow projection | 0.227 ms | 463 KB |
| UNIQUE index, **string** key | 52.0 ms | 131,710 KB |
| UNIQUE index, **int** key | 50.2 ms | 131,706 KB |
| **Non-unique** index, string key | 51.1 ms | 131,782 KB |
| UNIQUE index, **narrow projection** | 50.5 ms | 131,676 KB |
| UNIQUE index, **PK-only projection** | 53.0 ms | 131,676 KB |
| No index, forced scan | 291.8 ms | 662,489 KB |

Every secondary-index shape costs the same to within 0.1%. So the cost is **not** the string key
(the int key costs the same), **not** uniqueness (the non-unique index costs the same), **not**
materialising the row (asking for one column, or only the key the index already holds, costs the
same), and **not** the table size. It is also **not the storage layer**: run with `Mode=Memory` and
no file underneath, the same shapes allocate 131,696 KB — indistinguishable.

The index is genuinely being used: the forced scan costs 292 ms against the seek's 51 ms. The engine
is paying 51 ms to avoid 292 ms, while the primary-key path does the identical logical work for
0.26 ms.

### The mechanism, and the prediction that proved it

`QueryPlanner.EstimateTableRowCount`
([QueryPlanner.Sources.Indexes.cs:128](../Sources/Engine/OutWit.Database/Query/QueryPlanner.Sources.Indexes.cs#L128))
**opens a table scan and reads up to 1,000 rows, on every query execution**, to estimate a row count
so that `CreateOptimizedTableIterator` can decide whether an index is worth using. Its own comment
says *"do a quick scan to count rows (expensive but accurate) … TODO: Implement proper statistics
collection"*.

Reading that is not evidence in this repository, so it was turned into a falsifiable prediction: if
the cost is a scan capped at 1,000 rows, it must grow **linearly below 1,000 rows and be flat above
them**. Measured:

| rows in table | 250 | 500 | **1,000** | 2,000 | 5,000 | 20,000 |
|---|---|---|---|---|---|---|
| KB per lookup | 335 | 660 | **1,314** | 1,317 | 1,317 | 1,317 |
| ms per 100 lookups | 13.3 | 25.9 | 51.7 | 50.6 | 52.3 | 51.8 |

250 → 500 is 1.97x, 500 → 1,000 is 1.99x, and 1,000 → 2,000 is 1.002x. The measurement lands on the
`sampleLimit = 1000` constant to three significant figures, and the plateau is 1,000 rows times the
~1.32 KB this engine allocates per row scanned. The primary-key path is 568 KB at every size, so it
does not reach this code at all.

**The diagnosis: every `SELECT` carrying a `WHERE` clause against a table that has any index scans up
to 1,000 rows of that table before deciding whether to use an index. Deciding to use the index costs
about 200x the lookup it saves.**

### What this explains beyond the seek

This is not one benchmark's problem. It is a fixed tax on every indexed-predicate query, and it is
the plausible cause of most of the "behind LiteDB" column in § 5 — `UPDATE` by indexed column
(3.9x), composite index query (2.6x), non-unique index seek (2.4x). Those all run the same planner
on tables that all have indexes.

### The fix, and why it is small

The engine **already maintains an O(1) per-table row counter** — that is exactly what § 1.5 measured
when `COUNT(*)` came back flat at 0.0006 ms for both 1,000 and 10,000 rows. A planner estimate is
precisely the consumer that counter is good enough for: it is an estimate, the code asks for one, and
the `TODO` asks for statistics rather than a scan.

**The one caveat to carry into the fix**, from this project's own record: the counter is separate
state from the rows and the two can disagree after a crash. That is disqualifying for answering a
user's `COUNT(*)` and irrelevant for choosing a plan — but it must be stated in the change, not
discovered later.

**Not done here.** The change alters plan selection (an exact count where a capped sample stood, so
estimates cross `MIN_ROWS_FOR_INDEX` differently) and therefore needs the full suite behind it. It is
a `Sources/` behaviour change and deserves its own decision and its own PR, with this benchmark as
the before-and-after.

---

## 9. Closing the information gaps

Three holes were named in § 7 and § 5: the control was blind to writes, everything was measured at
20,000 rows or fewer, and only `Default` had been swept. Two are closed here. The third is not.

### 9.1 The write benchmarks are now verifiable, in both halves

The write benchmarks returned `void`, so an engine that wrote nothing benchmarked as fast. The
obvious repair - return the affected-row count - is **not sufficient here, and this project knows
exactly why**: the worst defect ever found in it was `Store=lsm` with a parallel mode losing
acknowledged writes, where ten `INSERT`s all reported success and 0-1 rows were present. Affected
rows is the acknowledgement that lied.

So the claim and the data are checked separately. Each write benchmark returns what the engine
claimed, and `IterationCleanup` - outside the timed region, before the databases are deleted -
counts what a scan can actually see and throws if they disagree. It never asks `COUNT(*)`: § 1.5
measured that to be a cached counter, which is separate state from the rows.

Both directions verified. Green: all three engines claim and show 100 rows. Red: making WitDatabase
claim one row more than it wrote produces

> `WitDb claimed 101 row(s) written but a scan sees 100. The benchmark timed a write that did not
> happen as reported.`

and the check exits 1.

### 9.2 The sweep selector was wrong, and using it is what found that

`BenchmarkSweep` threw when the requested narrowing matched none of a class's declared values, on
the principle that an empty selection silently measures nothing. The principle is right and the
implementation was wrong: **BenchmarkDotNet evaluates every class's `[ParamsSource]` before it
applies `--filter`**, so asking for 100,000 rows while filtering to `QueryBenchmarks` killed the run
on `JoinBenchmarks`, which declares 100 and 500 and was never going to be measured. It now keeps the
class's own values and says so loudly on stderr. Nothing is measured silently and a narrowing aimed
at one class no longer breaks the others.

### 9.3 At scale, the read picture is better than the small-table one

`QueryBenchmarks`, `Default`, 1,000 to 100,000 rows:

| operation | 1,000 | 10,000 | 50,000 | 100,000 | vs LiteDB at 100k |
|---|---|---|---|---|---|
| Point query by PK x100 | 0.221 ms | 0.237 | 0.251 | 0.262 | **0.17x** (flat in N) |
| `SELECT … LIMIT 100` | 0.077 ms | 0.077 | 0.077 | 0.079 | **0.59x** (flat in N) |
| `SELECT *` full scan | 0.747 ms | 10.57 | 63.02 | 128.98 | 0.96x |
| `SELECT Id, Name` | 0.760 ms | 10.71 | 65.98 | 131.12 | 0.95x |
| `SELECT … ORDER BY Name` | 1.654 ms | 44.59 | 286.96 | 599.16 | 2.16x |
| `SELECT … WHERE Age > 30` | 1.484 ms | 11.99 | 169.92 | 405.14 | **3.53x** |

Two things worth having:

- **Scans converge to parity with LiteDB as the table grows** (0.56x at 1,000 → 0.96x at 100,000)
  and hold ~8x behind SQLite, with allocation steady at ~2.4 KB per row returned.
- **The two flat operations stay flat to 100,000 rows.** `LIMIT` short-circuits and the primary-key
  path does not degrade at all.

### 9.4 A correction to § 6, from having more points on the curve

§ 6 named `ORDER BY` as a second target on the strength of *"31x for 10x the rows - superlinear"*.
That reading came from a single interval, 1,000 → 10,000, and **more points refute it**: 10,000 →
50,000 is 6.4x for 5x, and 50,000 → 100,000 is **2.09x for 2x**. Beyond 10,000 rows the curve is
linear and the ratio against LiteDB is steady at 2.10x / 2.13x / 2.16x.

So sorting is not a runaway; it is a **stable ~2.1x behind LiteDB and ~25x behind SQLite**, and the
1,000 → 10,000 jump is a threshold effect worth a look but not a defect signature. The GC hypothesis
in the plan - that the superlinearity was allocation-driven - is **not supported**, because there is
no superlinearity to explain. Demoting it from "second target" is the honest move.

### 9.5 A hypothesis raised and refuted in the same pass

`SELECT * FROM Users WHERE Age > 30` costs 405 ms at 100,000 rows while an unfiltered `SELECT *`
over the same table costs 129 ms. Reading 74% of the rows is three times more expensive than reading
all of them. `Age` is indexed, which suggested an obvious cause: with no statistics the planner has
no selectivity estimate, so an index it *can* use is an index it *will* use, and fetching 74% of a
table one index entry at a time need not be cheaper than reading it in order.

Tested directly - the same range, ~75% selectivity, on an indexed column and an unindexed one in the
same table:

| | 20,000 | 100,000 |
|---|---|---|
| Range 75% on **indexed** column | 30.14 ms | 172.80 ms |
| Range 75% on **unindexed** column | 30.51 ms | 149.79 ms |

**They are the same.** At 20,000 rows the indexed one is marginally *faster*; at 100,000 it is 15%
slower, which is nothing next to the 3x that needed explaining. The hypothesis is refuted: a
non-selective range over an index is not what makes that query expensive.

**So the `WHERE Age > 30` cost at scale is unexplained and stays open.** The next thing to vary is
the one property the refutation did not hold constant: `IX_Users_Age` is over a column with roughly
sixty distinct values, so it is a **highly non-unique** index with many rows per key, while the
`AltInt` index used above is unique. A low-cardinality non-unique index range is the next experiment,
not a conclusion.

### 9.6 Still not measured, and it is the last real gap

**Every number in this phase is `EngineMode=Default`.** The LSM and parallel modes have not been
swept on current `main` at all, which matters because the one write-side finding carried in the
project's memory - LSM being non-linear in N - lives precisely there and is still quoted from
January. `InsertBenchmarks` can now verify its own writes, so that sweep is worth taking and is the
obvious next measurement.

---

## 10. The engine modes, and the last recorded claim to fall

`InsertBenchmarks` across all five modes at 100, 1,000 and 5,000 rows, **twice**, with the write
verification of § 9.1 active on every iteration.

### 10.1 The verification never fired, which is a result in itself

Across both passes, every mode, every size, the scan count matched the engine's claim every time.
**No mode lost a write** - including `Lsm` and `LsmParallelAuto`, the combination that lost
acknowledged writes on default settings before 6.0.0. That fix holds, and it is now checked by an
instrument rather than believed.

### 10.2 LSM is uniformly slow on writes, and it is *not* non-linear in N

`INSERT` in a transaction, microseconds per row, both passes:

| mode | 100 rows | 1,000 rows | 5,000 rows | per-row change, 1,000 → 5,000 |
|---|---|---|---|---|
| Default | 30.7 / 30.1 | 8.3 / 8.4 | 7.7 / 8.0 | x0.93 / x0.95 |
| BTree | 28.2 / 27.8 | 6.3 / — | 5.0 / 5.1 | x0.80 / — |
| **Lsm** | **269 / 277** | **111 / 116** | **98.7 / 99.5** | **x0.89 / x0.86** |
| BTreeParallelAuto | 27.3 / 27.3 | 6.7 / 6.2 | 27.8 / 5.1 | x4.13 / x0.83 |
| LsmParallelAuto | 336 / 339 | 117 / 121 | 100 / 103 | x0.86 / x0.86 |

**The memory and the plan both carry "LSM is non-linear in N (12 ms at 100 inserts, 53 ms at 500 - a
defect signature)". It does not reproduce.** On current `main` LSM's per-row cost *falls* as the
table grows - x0.89 and x0.86 from 1,000 to 5,000 rows, in both passes. That is sublinear, which is
the normal and healthy shape.

What is true, and is a different statement, is that **LSM is uniformly 12-20x slower than B+Tree per
row at every size measured**, and `LsmParallelAuto` is slightly worse than plain `Lsm` rather than
better. Autocommit is where it is worst: 100 rows without a transaction cost 1,911-1,926 ms on LSM
against 194-205 ms on B+Tree, a flat 9.4x.

So the finding changes shape entirely - from "there is a scaling defect in the LSM write path" to
"the LSM write path has a large constant factor". Those need different work, and only one of them
was on the books.

### 10.3 Two anomalies appeared in pass one and were killed by pass two

This is the phase's own rule earning itself for the third time, and it is worth recording exactly
because the first pass looked like a finding:

- `BTreeParallelAuto` at 5,000 rows read **27.8 µs/row against 6.7 at 1,000** - a 4.13x per-row
  regression while every other mode improved. It was the only non-linearity in the sweep and it was
  tempting. Pass two: **5.1 µs/row, x0.83.** Noise.
- `BTree` at 1,000 rows read **153.2 µs/row** in pass two against 6.3 in pass one - a 24x outlier in
  the other direction, on the mode with the *best* numbers everywhere else.

Neither survived. Had the modes sweep been run once, as every previous performance claim in this
repository was, one of them would have been written up.

### 10.4 A recorded claim that *does* hold

Phase 4 recorded that making autocommit durable cost **~1.5x** on the write path. Measured here at
5,000 rows in a transaction: `Default` (MVCC on, durable) at 7.7-8.0 µs/row against `BTree`
(`MVCC=false`) at 5.0-5.1 - **~1.55x**, in both passes. The price of the D in ACID is what it was
said to be.

---

## 11. The refutation in § 9.5 was wrong, and the corrected experiment names a second planner defect

§ 9.5 reported that the obvious explanation for `WHERE Age > 30` costing 405 ms at 100,000 rows -
the planner taking an index for a range that selects most of the table - had been **tested and
refuted**, because the same range on an indexed and an unindexed column cost the same.

**That experiment did not refute the hypothesis. It failed to test it.** The "indexed" column it used
was `AltInt`, seeded as `i` and inserted in order, so the index key order was *perfectly correlated
with physical row order*. Walking that index visits rows in exactly the order a table scan would.
The control did not vary the thing it was believed to vary - which is the failure this project has a
rule about, applied here to my own instrument.

The corrected experiment adds `Bucket`, an integer with **60 distinct values** and a non-unique
index - the shape of `Users.Age`, the column that produced the unexplained number. Same table, same
~75% selectivity, three ways of reaching the rows:

| range selecting ~75% | 20,000 rows | 100,000 rows |
|---|---|---|
| no index (forced scan) | 31.8 ms | 199.5 ms |
| **unique** index, correlated with row order | 33.1 ms | 229.2 ms |
| **low-cardinality** non-unique index | 34.9 ms | **499.5 ms** |

**It reproduces.** At 100,000 rows the low-cardinality index costs 2.5x the scan it replaced, and
2.2x the correlated index. Allocation is unchanged across all three (282 / 280 / 305 MB), so this is
time, not materialisation - the rows cost more to *reach*, not more to build.

**SQLite does the same thing, which identifies the mechanism rather than excusing it.** Its
low-cardinality range costs 228.6 ms against 16.7 ms for the correlated index and 19.5 ms for the
plain scan - a 13.7x jump on the same shape. Visiting rows in index-key order when the key is
uncorrelated with physical order is random access into the table, and both engines pay for it.
WitDatabase pays 2.2x what SQLite pays, which is its own read-path cost on top, not a separate
defect.

### Why the planner cannot avoid this today

The cost model in `OptimizerQuery` is:

```
table scan      = N x 1.0
equality seek   = 5.0 + max(1, N x 0.01) x 1.0
index range     = max(1, N x 0.2) x 0.5   =  N x 0.1
```

`RANGE_SELECTIVITY` is the constant **0.2**, applied to every range predicate regardless of what it
actually selects. So an index range is costed at `0.1N` against a scan's `1.0N`: **an applicable
index always wins, for any table size, whatever the predicate really selects.** Both sides are linear
in `N`, so the estimate cancels - which is why § 8's 1,000-row scan buys a number that cannot change
the decision.

That is two distinct defects in the same place, and they are opposite in shape:

- **§ 8 - the planner pays for a number that does not matter.** A 1,000-row scan per query execution
  to estimate a row count that cancels out of every comparison.
- **§ 11 - the planner lacks the number that does matter.** No selectivity and no notion of
  correlation between index order and row order, so it takes an index for a predicate matching 75% of
  the table and turns a sequential scan into random access.

Both are "the planner has no statistics", and the `TODO` in `EstimateTableRowCount` asks for exactly
that. They are worth separating because their fixes differ sharply in size and risk: the first is a
small, near-zero-risk substitution measured in § 8; the second changes which plan is chosen and needs
statistics that do not exist yet.

---

## 12. Fix one: the planner reads the counter instead of scanning

`QueryPlanner.EstimateTableRowCount` now calls `IDatabase.GetTableRowCount`, which the catalog
already answers in O(1) - the same counter that makes `SELECT COUNT(*)` flat in table size (§ 1.5).
The interface member already existed; nothing new had to be plumbed.

The whole change is one function. It replaces a table scan of up to 1,000 rows **per query
execution** with a dictionary lookup.

### Why the risk was low, established before the change rather than after

The estimate feeds a cost model that is **homogeneous in it**: a table scan is costed at `N x 1.0`,
an index range at `N x 0.2 x 0.5`, an equality seek at `5.0 + N x 0.01`. `N` cancels out of the
comparison, so the same plan is chosen almost regardless of what the estimate says. The old code also
returned `count * 10` whenever it hit its cap, meaning **every table of 1,000 rows or more was
reported as exactly 10,000** whatever its real size - so the value being replaced was not accurate
either.

The one behaviour that had to be preserved deliberately: `GetRowCount` answers `-1` for a table the
catalog does not know, and `FindBestIndex` refuses any estimate at or below zero. Passing `-1`
straight through would have silently switched off every index on that path. It falls back to the old
default of 100 instead.

### Measured

`IndexSeekAnatomyBenchmarks`, `Default`, per 100 lookups, same machine and job as § 8:

| shape | before | after | |
|---|---|---|---|
| **UNIQUE index, string key** | 52.00 ms / 131,710 KB | **0.459 ms / 1,009 KB** | **113x faster, 131x less allocated** |
| UNIQUE index, int key | 50.24 ms | 0.443 ms | |
| Non-unique index, string key | 51.11 ms | 0.477 ms | |
| UNIQUE index, narrow projection | 50.48 ms | 0.444 ms | |
| No index, forced scan (5,000 rows) | 291.8 ms / 662,489 KB | 214.6 ms / 580,802 KB | 26% faster |
| PK equality | 0.258 ms | 0.266 ms | unchanged, as predicted |

The primary-key path is untouched because it never reached this code - which is what made it the
control in the first place.

**The gap that opened the phase is closed and inverted.** The unique-index seek was **23x slower than
LiteDB** (52.0 ms against 2.07 ms). It is now **4.5x faster** (0.459 ms against 2.07 ms). Against the
primary-key path in the same engine it went from ~200x to 1.7x.

The forced scan improving by 26% is the same tax being removed from a query that never used an index
at all: it has a `WHERE` and the table has indexes, so it paid the 1,000-row estimate too.

### Suite

Green across every project, `Category!=Performance`:

| project | passed | failed |
|---|---|---|
| `OutWit.Database.Tests` | 2,216 | 0 |
| `OutWit.Database.Core.Tests` | 2,278 | 0 |
| `OutWit.Database.AdoNet.Tests` | 798 | 0 |
| `OutWit.Database.Parser.Tests` | 797 | 0 |
| `OutWit.Database.EntityFramework.Tests` | 554 | 0 |
| **total** | **6,643** | **0** |

`WitSqlEngineSelectWhereRowCountTests`, which exists specifically to pin the interaction between the
planner's `MIN_ROWS_FOR_INDEX` threshold and this estimate, passes unchanged.

### What this fix does *not* address

The second planner defect from § 11 is untouched: `RANGE_SELECTIVITY` is still the constant 0.2, so
an applicable index still always wins a range comparison, and a range selecting 75% of a
low-cardinality index still costs 2.5x the scan it replaced. That needs statistics the engine does
not keep - distinct-value counts, and some notion of whether index order tracks physical row order.
It changes *which plan is chosen*, where this fix changed only *what it costs to choose*.

---

## 13. Re-baselined: the fix moved far more than its target

A fix without a re-measure is not finished, so the § 5 sweep was taken again - same classes, same
`Default` mode, same sizes, same job, two passes.

### Everything with a `WHERE` improved, not just the seek

| operation | before | after | |
|---|---|---|---|
| Index seek, unique, x100 | 48.42 ms / 125,332 KB | **0.500 ms / 970 KB** | **97x** |
| Index seek, non-unique, x20 | 19.01 ms / 42,149 KB | 8.40 ms / 17,277 KB | 2.3x |
| Composite index query | 9.89 ms / 20,926 KB | 4.52 ms / 8,490 KB | 2.2x |
| Index range scan (`>`) | 1.571 ms | 0.839 ms | 1.9x |
| Index range scan (`BETWEEN`) | 1.397 ms | 0.832 ms | 1.7x |
| `SELECT … WHERE Age > 30` | 1.637 ms | 1.114 ms | 1.5x |
| Full scan, no index | 7.53 ms | 5.94 ms | 1.27x |
| aggregates, joins, projections | | | 5-8% |
| `INSERT`, `UPDATE`, transactions | | | unchanged |

The write paths are untouched, as they should be - the planner is not on them. The full scan gains
because it still has a `WHERE` over a table that has indexes, so it paid the estimate without ever
using one.

### Where WitDatabase now stands against LiteDB

Same sweep, `Default`, ratios recomputed per operation:

| operation | before | after |
|---|---|---|
| Index seek, unique | 23.4x **slower** | **0.25x — 4x faster** |
| Index range scan (`BETWEEN`) | 0.28x | 0.18x |
| Point query by PK | 0.20x | 0.19x |
| Index seek, non-unique | 2.14x slower | 1.05x — parity |
| Index range scan (`>`) | 1.87x slower | 1.08x — parity |
| `SELECT … WHERE Age > 30` | 1.43x slower | 1.02x — parity |
| Full scan, no index | 1.18x slower | 1.00x — parity |
| Composite index query | 2.51x slower | 1.25x |
| `ORDER BY` | 0.95x | 0.91x |
| `INNER JOIN` over 4 tables | 2.74x slower | 2.73x — unchanged |

**Only two operations remain more than 3x behind LiteDB, and both are inserts** - `INSERT` without a
transaction at 21.4x and `INSERT … RETURNING` at 3.1x. The first is the durable-autocommit cost phase
4 chose deliberately, and on that same operation WitDatabase is 3.3x *faster* than SQLite. Before the
fix the list also held the index seek at 23.4x.

The picture changed shape. Against the stated competitor, WitDatabase went from roughly half the
operations behind to **ahead or at parity on everything except joins and the deliberate durability
cost**.

### One apparent regression, and it was noise

Pass one read `Tx Rollback` at 0.604 ms against the pre-fix 0.475 - a 27% regression on a path the
change does not touch, which is exactly the shape that gets written up as a side effect. Pass two:
**0.461 ms**. The three readings overlap and the honest finding is no measurable difference. Four
readings in the sweep moved more than 10% between the two passes and none of them carries a
conclusion here.

---

## 14. The LSM write path, first diagnosis

`LsmWriteAnatomyBenchmarks`: 1,000 rows inserted in one transaction, four shapes differing by exactly
one property, five modes, two passes. Every shape reports what the engine claimed and
`IterationCleanup` checks it against a scan; nothing disagreed in either pass.

### What is stable, and it is inside the LSM column

| shape, 1,000 rows | `Lsm` pass 1 | `Lsm` pass 2 |
|---|---|---|
| Bare table — no key, no index | 118.1 ms | 111.7 ms |
| PK only | 119.4 ms | 115.8 ms |
| PK + 1 secondary index | 286.1 ms | 502.0 ms |
| PK + 3 secondary indexes | 534.1 ms | 560.0 ms |

Two findings survive both passes:

- **The primary key is free.** Bare and PK-only are indistinguishable (118.1/111.7 against
  119.4/115.8). Whatever LSM is paying, it is not the generated key or a uniqueness check on it.
- **Secondary index maintenance dominates.** No index costs ~115 ms per 1,000 rows; three indexes
  cost ~547 ms. That is **~430 ms of index maintenance per 1,000 rows — roughly 48 µs per row per
  index** — where B+Tree pays close to nothing for the same indexes.

So the LSM constant factor is two separate costs, not one:

1. **The base row write is already expensive** — ~115 µs per row into a bare LSM table with nothing
   else to maintain. For a structure whose whole purpose is that a write is an append to a memtable,
   that is the number that should not be there.
2. **Each secondary index multiplies it** — and this is what makes the LSM total 12-20x B+Tree rather
   than 5x.

### What is *not* established, and why

**The B+Tree column was too unstable to quote per-shape ratios.** It read 22.7 ms and 6.0 ms for the
same bare insert in two passes — a 3.6x swing — and `PK + 1 index` moved 286 → 502 ms on LSM. With
`IterationSetup` recreating the database every iteration, BenchmarkDotNet is forced to
`InvocationCount=1`, so a ShortRun measurement is three single insert batches and the variance is
large.

The `Lsm/BTree` ratio per shape is therefore **not reported here** — it would be a number computed
from a denominator that moved by 3.6x. The stable cross-engine figure remains the one from § 10,
taken with many more samples: LSM is 12-20x B+Tree per row overall.

**This instrument needs more iterations before its per-shape ratios can be believed** — a limitation
found by using it, and stated rather than worked around. The within-LSM comparisons above are quoted
because they repeated; nothing else from this sweep is.

### Where to look next

The base cost points away from the memtable and at what happens around each write: `SyncWrites=false`
in this mode, so it is not fsync, which leaves a per-row write-ahead-log append and memtable rotation
as the candidates. `StoreLsm` truncates its WAL the moment a memtable is flushed, so flush frequency
is directly observable and is the cheapest next measurement.

The index cost is the larger share and has a specific suspect worth testing first: whether index
maintenance performs a **read** per inserted row. A read on an LSM store must consult the memtable
and every SSTable, which is exactly the operation LSM is worst at, and it would explain why the same
indexes cost nearly nothing on a B+Tree.

### On the product question

The measurements say LSM currently loses to B+Tree by 12-20x **on writes** — the workload an LSM tree
is chosen for. That is a defect signature rather than a trade-off, and the two costs above are where
it lives. Whether it is worth fixing is a product decision, but "LSM is the write-optimised option"
is not true of this engine today, and no configuration guidance should say so until it is.

---

## 15. The LSM constant factor found: the write-ahead log bypasses the OS write cache

The § 14 diagnosis said the cost is two things - a base row write of ~115 µs and secondary index
maintenance at ~48 µs per row per index - and named the next suspects. Both turn out to be one cause.

### The mechanism

`WriteAheadLogBase` opens its file like this:

```csharp
m_stream = new FileStream(filePath, mode, FileAccess.ReadWrite, FileShare.Read, 4096,
                          FileOptions.WriteThrough);
```

**`FileOptions.WriteThrough` tells the OS not to cache the write** - every append goes to the
device. `AppendEntry` also calls `SeekToEnd()` first, which sets `Position = Length`; on a
`FileStream` both reading `Length` and assigning `Position` flush the write buffer, so the 4 KB
buffer never accumulates anything either.

The result is one device-level write per row **per store**. In LSM mode every secondary index gets
its **own** `StoreLsm` with its own WAL (`WitDatabaseBuilder.CreateLsmIndexFactory`), so three
indexes mean four write-through streams per inserted row. That is exactly the shape § 14 measured:
a large base cost, multiplied by the number of indexes.

### It contradicts its own option

`LsmOptions.SyncWrites` documents itself as:

> *Whether to sync WAL to disk after each write operation. If false, relies on OS buffering (faster
> but less durable per-write). Default: false (matches SQLite behavior…)*

The default is `false` and the benchmark configuration sets it explicitly. **There is no OS buffering
to rely on** - the stream is opened in a mode that forbids it. The option promises the fast path and
the file handle refuses to provide it.

### Measured, by removing it

`FileOptions.WriteThrough` replaced with `FileOptions.None`, nothing else changed, same benchmark,
1,000 rows in one transaction:

| shape | `Lsm` with WriteThrough | `Lsm` without | |
|---|---|---|---|
| Bare table, no key no index | 111.7-118.1 ms | **33.10 ms** | 3.4x |
| PK only | 115.8-119.4 ms | **36.91 ms** | 3.2x |
| PK + 1 secondary index | 286.1-502.0 ms | **41.35 ms** | 7-12x |
| PK + 3 secondary indexes | 534.1-560.0 ms | **44.30 ms** | 12x |

**The index multiplication disappears.** Going from no index to three cost +375% before and +34%
after. And LSM stops losing to the B+Tree store: at three indexes it measures 44.30 ms against
B+Tree's 49.27 ms in the same run - faster.

So this one flag is most of the 12-20x reported in § 10, and it is the answer to "is there a defect
in the LSM implementation": yes, and it is one line.

### Whether removing it is safe - the part that needs a decision

Durability does **not** depend on it. `WriteAheadLogBase.Sync()` calls
`m_stream.Flush(flushToDisk: true)` - a real fsync - and that is what `SyncWrites=true` invokes per
write and what `AppendCommitTransaction` invokes on commit. `WriteThrough` is redundant with the
durable path and defeats the fast one.

What removing it genuinely changes is the *fast* path: with `SyncWrites=false` a write would now sit
in the OS cache until a sync, which is what the option says it does and what SQLite does. Today it
reaches the device. **Nobody could have been relying on that deliberately** - it is undocumented and
contradicts the option's own text - but it is a real reduction in accidental durability and this
project does not make durability changes quietly. Phase 4 already established that LSM durability is
partial in a different way: `SyncWrites` syncs the WAL only, and the WAL is truncated the moment the
memtable is flushed into an SSTable that was never fsynced.

**A second, smaller question found on the way:** index stores are built with `new LsmOptions()` -
plain defaults - so a connection string's `SyncWrites` never reaches them. Whatever is decided for
the main store, the indexes should follow it rather than silently doing something else.

**Not changed here.** The measurement above was taken by editing the file and reverting it; nothing
in this commit alters the engine. The fix is one line, its performance case is measured, and its
durability case is stated - it wants an explicit decision rather than an assumption.

---

## 16. Fix two: the write-ahead log no longer bypasses the OS write cache

`FileOptions.WriteThrough` is gone from `WriteAheadLogBase`. Durability never depended on it and does
not now: it comes from `Sync()`, which calls `Flush(flushToDisk: true)` - `StoreLsm` calls it per
write when `SyncWrites` is on and the LSM log calls it on commit, and `TransactionalStore` calls it
through the journal on commit.

**Verified rather than argued.** The full suite is green at **6,643 tests**, and separately the
**13 `Category=Crash` tests pass** - the out-of-process kill tests phase 4 built to prove a committed
transaction survives a process being killed. Those are the arbiter for this change, and they are
unchanged.

### What it bought, two passes

`InsertBenchmarks`, `UpdateBenchmarks`, `TransactionBenchmarks`, 100 rows, `Lsm` against `BTree`:

| operation | Lsm/BTree pass 1 | pass 2 |
|---|---|---|
| `INSERT` without a transaction, x100 | 4.76x | 5.60x |
| `INSERT` in a transaction | 3.67x | 3.86x |
| Transaction with savepoint | 2.32x | 2.57x |
| `UPDATE` by PK | 1.81x | 1.80x |
| `UPDATE … RETURNING` | 1.81x | 1.75x |
| Single transaction, N `INSERT`s | 1.75x | 1.73x |
| Bulk `UPDATE` | 1.68x | 1.68x |
| `UPDATE` by indexed column | 1.69x | 1.55x |
| Sequential reads x100 | 1.08x | 1.18x |
| Transaction rollback | 1.04x | 0.98x |

**The LSM penalty went from 12-20x to 1.0-5.6x**, and reads and rollback reached parity.

Two readings are excluded rather than quoted: `INSERT … RETURNING` moved 0.92x → 1.69x because its
B+Tree denominator halved between passes, and `Mixed Tx` produced a 98x B+Tree outlier in pass two
(2.90 ms → 284.21 ms). Neither carries a conclusion.

### The remaining penalty is a fixed cost per transaction, not per row

The anatomy at **1,000 rows** measured LSM at 44.30 ms against B+Tree's 49.27 ms with three
secondary indexes - LSM *faster*. These benchmarks at **100 rows** put it 1.7-3.9x behind. Same
engine, same fix, opposite verdict, and the difference is batch size: what is left is a fixed cost
per transaction that amortises as the batch grows.

That is also the answer to *"is there a workload where LSM helps?"* - **on the evidence so far, no,
and the suite cannot answer it properly.** LSM reaches parity on reads and on large batches and wins
nowhere. The workload the structure is designed for - large sequential write batches - is not in the
suite at all: `InsertBenchmarks` stops at 5,000 rows and the anatomy at 1,000. Answering it needs a
benchmark aimed at that shape, not another reading of the ones that exist.

**Autocommit remains the worst place**: ~12.8 ms per single inserted row on LSM against ~2.7 ms on
B+Tree, roughly 5x, and the fix barely moved it. It is a separate cost from the one removed here and
has not been diagnosed.

### Found on the way and not fixed

Index stores are constructed with `new LsmOptions()` in
`WitDatabaseBuilder.CreateLsmIndexFactory`, so a connection string's `SyncWrites` never reaches them
- every secondary index silently runs on defaults. Whatever is decided for the main store, the
indexes should follow it rather than doing something else unannounced.

---

## 17. The LSM audit, second pass: two more defects, and why the obvious fix cannot ship yet

Context for this pass, from the author: **LSM is meant to be a supported choice**, not a curiosity -
WitDatabase is built as a construction kit where a workload picks its configuration, and LSM is the
one intended for write-heavy work. Measured against that intent, "slower than the B+Tree on writes"
is not a trade-off to document; it is a defect to find.

### 17.1 The number that pointed at it was already in the data

| | one transaction of 100 rows | 100 transactions of 1 row |
|---|---|---|
| `Lsm` | 10.93 / 11.11 ms | 1281.8 / 1097.9 ms → **11.0 / 12.8 ms per transaction** |
| `BTree` | 2.98 / 2.88 ms | 269.1 / 196.1 ms → 2.69 / 1.96 ms per transaction |

**A transaction costs ~11 ms on LSM whether it contains one row or a hundred.** The rows are nearly
free; the commit is everything. That is a fixed per-commit price, and it is 4-5x the B+Tree's.

### 17.2 Defect: every commit writes an SSTable

`Transaction.Commit` calls `m_store.Flush()`, and `StoreLsm.Flush()` flushed the MemTable whenever it
held **anything at all**:

```csharp
if (m_activeMemTable.Count > 0)
    FlushMemTableInternal();
```

So every commit created a file, wrote its blocks, bloom filter and index, fsynced it, and handed the
compactor more work. That is the exact opposite of what an LSM tree is for: the MemTable exists so
that many writes accumulate in memory and reach disk **once**, as one large sorted file.
`MemTableSizeLimit` was unreachable in transactional mode - the flush policy was effectively "one
SSTable per commit".

**Measured**, by making `Flush()` sync the write-ahead log instead - which is what an LSM log is for,
and what RocksDB does - and flush the MemTable only when it is actually due:

| | before | after |
|---|---|---|
| 50 transactions x 1 row | 14.67 ms/tx | **2.95 ms/tx** |
| 50 transactions x 10 rows | 10.57 ms/tx | **2.35 ms/tx** |
| 50 transactions x 100 rows | 13.32 ms/tx | **5.30 ms/tx** |

The cost stops being fixed and starts growing with the rows written, which is the correct shape. A
scan still returns every row, and the 13 `Category=Crash` tests still pass - durability moves from
the SSTable to the WAL, and `Open` already replays the WAL.

### 17.3 Defect: `MemTableSize` from the connection string is inert

Trying to ship § 17.2 broke `MemTableSizeParameterWorksTest`, which sets `MemTableSize=1024`, writes
~10 KB and asserts SSTables exist. **It had been passing for the wrong reason**: every commit flushed,
so it would have passed with the parameter wired to nothing.

Measured directly, 5 MB of payload in one transaction:

| `MemTableSize` | SSTables produced |
|---|---|
| 1,024 bytes | **1** |
| 4,194,304 bytes (the default) | **1** |

At a 1 KB limit that should be thousands of flushes. The two are identical, so **the connection
string's value never reaches `LsmOptions.MemTableSizeLimit`** - the store always uses the 4 MB
default. The size threshold itself works: one SSTable appears once the default is crossed, and none
appears below it.

`ProviderRegistration` does read `MemTableSize` into the options, and `WitDbConnectionStringBuilder`
knows the keyword, so the break is between them. Locating it exactly is a bounded next step.

### 17.4 Why § 17.2 is not shipped here

The change is drafted and measured, and it **breaks seven tests**, so it is deliberately not in this
branch:

- **Six in `Core.Tests`** use `Flush()` as a lever meaning *"write an SSTable now"* - they test
  compaction and failed-flush handling (`ControlCompactionReallyReplacesTheTables`,
  `RowsOfAFailedFlushAreStillScannable`, and four more). They are not testing the commit contract,
  and they pin real defects found in phases 4 and 5, so they must not be rewritten casually. What
  they need is an explicit force-flush entry point, separate from `Flush()`-means-make-durable.
- **One in `AdoNet.Tests`** is § 17.3 - a defect the change exposes rather than causes.

And § 17.3 makes § 17.2 load-bearing in a way it should not be yet: with the commit flush gone, the
size threshold becomes the **only** thing that ever flushes a MemTable, so a connection string asking
for a small MemTable would be ignored and the MemTable would grow to 4 MB regardless.

**So the order is: fix the parameter, split `Flush()` into "make durable" and "force out", update the
six tests to say which they mean, and only then remove the per-commit SSTable.** Each of those is
small; doing them together, at the end of a long session, is how a durability path gets broken
quietly.

### 17.5 Where this leaves the construction-kit intent

Three defects now separate the LSM store from what it is supposed to be, and they compound:

1. **The WAL bypassed the OS write cache** - fixed, 12-20x down to 1.0-5.6x.
2. **Every commit wrote an SSTable** - diagnosed and measured, ~5x on the per-commit cost, not yet
   shipped.
3. **`MemTableSize` is inert** - so the one remaining flush policy cannot be configured at all.

None of them is a trade-off inherent to log-structured storage. All three are the implementation
doing more I/O than the design calls for, and the first two are exactly the properties that make an
LSM tree worth choosing. Until 2 and 3 are fixed, the honest statement is that **LSM is not yet the
write-heavy option this project intends it to be** - and after them it is worth re-asking, with a
benchmark aimed at sustained high-volume ingest, which the suite still does not have.

---

## 18. Fixes three, four and five: the LSM store now behaves like one

The order § 17.4 laid out, carried out in that order because each step depends on the one before.

### 18.1 Every LSM connection-string parameter was inert

Not just `MemTableSize`. `WitDatabaseBuilder.BuildLsmStore` constructed `StoreLsm` directly and asked
only for a **ready-made** options object:

```csharp
var lsmOptions = Options.StoreParameters.Get<LsmOptions>("options") ?? new LsmOptions();
```

The mapping that turns connection-string keys into `LsmOptions` lived in `ProviderRegistration` and
was reachable only through the provider registry, which this path does not use. So `MemTableSize`,
`SyncWrites`, `EnableWal`, `BlockSize`, `CompactionTrigger`, the block cache settings - **every knob
the connection-string builder documents** - were dropped for the main store. The per-index stores did
the same with their own `new LsmOptions()`.

The mapping moved onto `LsmOptions.FromParameters`, where the registry, the main store and the index
stores can all reach it. Measured, 5 MB written in one transaction:

| `MemTableSize` | SSTables before | after |
|---|---|---|
| 1,024 bytes | 1 | **5,556** |
| 4,194,304 (default) | 1 | **2** |

Before, the two were indistinguishable. Now the knob turns, and it turns by a factor of 2,778.

**Why it hid for so long:** the defaults are reasonable, and the one test that claimed to cover it -
`MemTableSizeParameterWorksTest` - passed for a different reason. Every commit flushed the MemTable
regardless of its size, so SSTables appeared whatever the limit said. The test would have passed with
the parameter wired to nothing, which is exactly what it was.

### 18.2 `Flush()` and `Checkpoint()` are now different operations

`IKeyValueStore.Flush()` means **make durable** and is what a commit calls.
`IKeyValueStore.Checkpoint()` means **force the accumulated state out now** and is what a size
threshold, maintenance, or a caller wanting an on-disk sorted file calls. `Checkpoint` defaults to
`Flush`, so only stores with an in-memory staging area differ - nothing else had to change.

This is the split every database makes and this engine had collapsed: PostgreSQL commits by syncing
the WAL and moves buffers on `CHECKPOINT`; SQLite has `PRAGMA synchronous` and
`PRAGMA wal_checkpoint`; RocksDB has `SyncWAL()` and `Flush()`. **Durability is an operation on the
log; reorganising the data structure is a separate decision.**

The 61 call sites in the LSM tests that meant *"write an SSTable now"* say `Checkpoint()`, which is
what they always meant and what they always did - the transformation preserves their behaviour
exactly rather than relaxing them to fit a fix.

### 18.3 The per-commit SSTable is gone

`StoreLsm.Flush()` now syncs the write-ahead log and flushes the MemTable **only when it is due**.
`Recover` already replays the WAL on open, so a crash after commit restores the MemTable. With
`EnableWal` off there is nothing to recover from, so the old behaviour is kept - the MemTable is then
the only record of those writes.

Measured, 50 transactions, three rounds each:

| rows per transaction | before | after |
|---|---|---|
| 1 | 14.67 ms/tx | **3.00 / 2.17 / 2.11 ms/tx** |
| 10 | 10.57 ms/tx | **2.32 / 2.33 / 2.38 ms/tx** |
| 100 | 13.32 ms/tx | **4.54 / 5.03 / 4.47 ms/tx** |

Roughly **5-7x on a small commit**, and - the part that matters more - **the cost now grows with the
rows written**. Before, a transaction cost the same whether it held one row or a hundred, which is
the signature of paying for something other than the work.

### 18.4 The tests caught a real defect in the fix

Forwarding `Checkpoint()` through `LsmParallelStore` straight to the wrapped store skipped the
buffer drain that `Flush()` does, so three parallel-store tests read **0 rows where they expected 800
and 1,000** - writes still sitting in `LsmWriteBuffer`. That is the same read-your-own-writes hazard
that once made this store lose acknowledged writes, and it appeared within an hour of touching the
path. `Checkpoint()` drains first now.

Worth recording plainly: the fix was wrong, the existing suite said so immediately, and that is what
those tests were built for in phases 4 and 5.

### 18.5 State

Green: **6,643 tests** across all five projects, and the **13 `Category=Crash` tests** - phase 4's
out-of-process kill tests - unchanged.

Three defects closed in this pass, on top of § 16's:

| | |
|---|---|
| WAL bypassed the OS write cache | § 16, PR #109 |
| Every LSM connection-string parameter inert | § 18.1 |
| `Flush()` conflated durability with reorganisation | § 18.2 |
| Every commit wrote an SSTable | § 18.3 |

**What is not yet answered** is the question that started this: whether there is a workload where LSM
wins. It is now plausible for the first time - the structure finally does what an LSM tree does - but
plausible is not measured, and the suite still has no sustained high-volume ingest benchmark to
measure it with. That is the next experiment.

---

## 19. The ingest benchmark, and the answer to "does LSM ever win?"

`IngestBenchmarks` is the workload the suite never had: tens to hundreds of thousands of rows written
in steady batches of 1,000, which is what an event pipeline does. At this volume the MemTable fills
repeatedly, SSTables are genuinely produced and the compactor has real work - so the structure is
finally asked the question it is chosen to answer. Every existing write benchmark stopped at 5,000
rows in transactions of 100, where a B+Tree's update-in-place is at its best and an LSM has nothing
to amortise over.

Each shape returns what the engine claimed and `IterationCleanup` checks it against a scan. Nothing
disagreed.

### The measurement, two passes

Medians, because the means carry outliers at the larger size:

| | pass 1 | pass 2 |
|---|---|---|
| **50,000 rows** | | |
| LiteDB | 275.9 ms | 262.7 ms |
| SQLite | 398.4 ms | 397.1 ms |
| WitDatabase, B+Tree | 486.7 ms | 491.8 ms |
| WitDatabase, **LSM** | 549.3 ms | 552.5 ms |
| **200,000 rows** | | |
| LiteDB | 1,068.3 ms | 1,057.8 ms |
| SQLite | 2,420.0 ms | 1,601.7 ms |
| WitDatabase, LSM | 2,585.4 ms | 3,361.0 ms |
| WitDatabase, B+Tree | 2,066.9 ms | 5,033.9 ms |

**Only the 50,000-row block is quoted as a result.** At 200,000 the WitDatabase readings move by up
to 2.4x between identical passes (B+Tree 2,067 → 5,034) and SQLite by 1.5x. Allocation is ~1.8 GB per
operation there, so the run is measuring the garbage collector and the disk as much as the engine.
Two passes are not enough at that size; saying so is the finding, not a number.

### The answer

**No. On the workload an LSM tree exists for, LSM still does not win** - it is **~12% behind the
B+Tree store** at 50,000 rows, and the gap repeated across both passes (549/553 against 487/492).

That is a very different sentence from the one this phase started with. Before the four fixes, LSM
was **12-20x** behind on writes. It is now within about a tenth. But "nearly as good as the
alternative on its own home ground" is not a reason to choose it, and no configuration guidance
should suggest otherwise.

Against the other engines on this workload, WitDatabase is 1.85x LiteDB and 1.24x SQLite - and the
allocation claim that § 5 had to withdraw **does hold here**: ~450 MB against LiteDB's 609-649 MB per
50,000 rows, about 30% less, on the workload where allocation matters most. SQLite's 31 MB is what a
native engine with no managed object graph costs, and is not the target.

### What would have to be true for LSM to win

The remaining ~12% is not obviously a defect - it may simply be what this LSM costs. Three things
that were found but not chased could each be worth more than that margin:

- **Each secondary index still gets its own `StoreLsm`** - its own WAL, MemTable and compaction
  schedule. RocksDB shares one WAL across column families for exactly this reason. This benchmark
  has no secondary indexes, so it does not even exercise the cost.
- **Autocommit is still ~5x the B+Tree**, undiagnosed. Group commit - several transactions sharing
  one log sync, as PostgreSQL and MySQL do - is the standard answer and would help both stores.
- **Compaction is not measured at all.** Nothing here reports how much of the time is compaction, or
  what read amplification the resulting SSTable count costs. Both are the levers an LSM is tuned on.

The honest position for a construction kit: **LSM is now a defensible option rather than a broken
one, but there is still no measured workload where choosing it beats the default.** Finding one, if
it exists, means measuring the three items above - not re-running this benchmark.
