# Phase 9 — unbuilt capability

Working record, opened 2026-08-01. A **decision pass**, not an audit: the only phase whose output may
legitimately be "no, and here is why". A skip is allowed; a silent skip is not.

---

## 1. The list was wrong in three places

The plan's list was assembled during phase 3 and read a month later. Re-measured before planning
against it — the tenth time in this project that a record about the past turned out false when re-run.

| Recorded as | Measured 2026-08-01 |
|---|---|
| **JSON columns** unbuilt | **Work end to end.** The type reaches the catalog and `INFORMATION_SCHEMA` reports `JSON`; a document survives storage; `JSON_EXTRACT` reads a nested array index, in the select list and in a `WHERE` |
| **Database-first scaffolding** cannot work at all | **Works.** Phase 7 rewrote the model factory onto `INFORMATION_SCHEMA` |
| **`HAVING COUNT(*) BETWEEN`** unbuilt capability | **A defect**, and *already recorded* on 2026-07-28 in `HavingAggregateFindingsTests` — with a better diagnosis than the re-measure produced, since that fixture also covers `IN`, which is what shows the fault is not `BETWEEN`'s |

Nothing new was pinned for the third: a second marker for one finding splits it across two files and
inflates the ledger. The ledger is **36 + 14 = 50** - one marker added, for the derived-table
defect in section 4.

**Genuinely absent, all refused by the parser:** `LATERAL`, `CROSS APPLY`, `OUTER APPLY`, a `VALUES`
table source, a derived column list, `TOP n`, `CREATE FUNCTION`, `CREATE PROCEDURE`.
`UnbuiltCapabilityCorpusTests` pins the status of each, so the list cannot drift again between the
sessions that read it.

## 2. The oracle, and why SQLite could not answer

Every conformance instrument in this repository compares against SQLite — and SQLite lacks most of
this list itself, so it cannot answer the question the phase asks, which is whether the **drop-in
target** has a capability. `DialectCoverageOracle` asks PostgreSQL 17 and SQL Server 2022 directly,
via Testcontainers, and produces a report rather than a verdict.

The corpus carries **one spelling per dialect**, because the same capability is spelled differently in
each, and distinguishes *"this dialect has no spelling for it"* (`-`) from *"rejected"* — different
facts that a single pass/fail column would merge.

```
capability                            PostgreSql    Sqlite        SqlServer
lateral-join                          yes           -             -
cross-apply                           -             -             yes
outer-apply                           yes           -             yes
values-as-table-source                yes           yes           yes
derived-column-list                   yes           REJECTED      yes
row-limit                             yes           yes           yes
user-defined-function                 yes           -             yes
stored-procedure                      yes           -             yes
json-extract                          yes           yes           yes
aggregate-inside-between-in-having    yes           yes           yes
```

### The one line that justifies the whole instrument

**`derived-column-list`: rejected by SQLite, supported by both targets.** Against the SQLite oracle
this would have read as "SQLite does not have it either, so we are at parity" — and that would have
been the wrong answer for a drop-in for PostgreSQL and SQL Server. The oracle exists because that
mistake was available.

### And it settles a defect

**`aggregate-inside-between-in-having`: accepted by all three.** WitDatabase is the only engine of the
four that refuses it. That is no longer an argument, it is a measurement, and it moves the item out of
"capability" for good.

### The probe refuses to report unless it can discriminate

Before any result, the probe runs a positive and a negative control **on the connection it is about to
use**: a plain `SELECT` must succeed and deliberate nonsense must fail. A probe that swallowed errors
would report every capability as universally supported, and that report would become a roadmap.

Its own mechanism is proved against in-memory SQLite in `DialectProbeControlTests`, which runs in CI
and needs nothing external — so the oracle cannot rot between the sessions that can reach a server.
That control immediately earned itself: the first corpus entry wrote
`(VALUES (1),(2)) AS V (N)`, which measures the `VALUES` source **and** the derived column list at
once. SQLite has the first and lacks the second, so its rejection was being attributed to the wrong
item. **Twelfth time an instrument here was wrong before its subject.**

## 3. What the report changes about the list

Two items on the plan's list are not capability questions at all once both targets are visible:

- **`LATERAL` and `CROSS APPLY` are one capability with two spellings** — a correlated subquery in
  `FROM`. PostgreSQL spells it `LATERAL`, SQL Server spells it `CROSS APPLY`, and `OUTER APPLY` is the
  outer-join form of the same thing (`LEFT JOIN LATERAL … ON TRUE` in PostgreSQL). Read as three
  separate items it looks like three one-dialect features; read correctly it is **one capability both
  targets have**, which is a much stronger case.
- **`TOP n` is a spelling, not a capability.** All three engines limit rows; WitDatabase already does
  it with `LIMIT`. The question is only whether to accept SQL Server's spelling as well.

## 4. The decisions, taken 2026-08-01

Dmitry, against the report:

| Item | Decision |
|---|---|
| derived column list `AS V(Id)` | **build** |
| `VALUES` as a table source | **build** |
| `TOP n` | **build** |
| correlated subquery in `FROM` (`LATERAL` / `APPLY`) | **build if not hard**, skip if it is |
| user-defined functions | **build** — "for a real drop-in it is necessary" |
| stored procedures | **build** — same |
| aggregate inside `BETWEEN`/`IN` in `HAVING` | **fix** |

On the functions and procedures, in his words: hard, and not that important for a *file* database as
such — but necessary for a genuine drop-in, and held in the plan from the start. They are complex
subsystems and the requirement is to **embed them carefully and within the design**, not bolt them on.

### And a measurement that changes one of those decisions

`LATERAL` was priced as "real planner work: the subquery sees the outer row". Measured 2026-08-01,
**the engine already evaluates a subquery per outer row, everywhere an expression can appear**:

```
SELECT Id FROM T WHERE EXISTS (SELECT 1 FROM S WHERE S.TId = T.Id)         works
SELECT Id FROM T WHERE Id IN (SELECT TId FROM S WHERE S.Score > 250)       works
SELECT Id, (SELECT MAX(Score) FROM S WHERE S.TId = T.Id) FROM T            works
SELECT Id FROM T WHERE (SELECT MAX(Score) FROM S WHERE S.TId = T.Id) > 250 works
```

So correlation is not the missing part. What is missing is reaching that machinery from a **table
source** rather than from an expression. That is much closer to plumbing than to a new planner, and
it moves `LATERAL`/`APPLY` out of "skip if hard" on the evidence rather than on optimism.

### A defect found while measuring it, and it blocks 9b

`SELECT *` over a derived table expands **every column twice**, once qualified and once bare —
`(SELECT Id, TId FROM S) AS X` yields `X.Id, X.TId, Id, TId`. Pre-existing; `v9.0.0` behaves
identically. Recorded in `DerivedTableColumnsFindingsTests`.

It matters here specifically: a derived **column list** is built on the same star expansion, so
building the feature first would bake the duplication into the new path.

---

## 5. The plan

Ordered by what depends on what, not by value — the same rule the phases 5–10 plan used.

### 9a — the two defects the feature work sits on

- Aggregate inside `BETWEEN`/`IN` in `HAVING`. Recorded since 2026-07-28 in
  `HavingAggregateFindingsTests`; now confirmed accepted by **all three** reference engines, so it is
  a defect by measurement and not by argument.
- `SELECT *` over a derived table duplicating columns.

Both are in query/expression resolution, both are small, and **9b builds directly on the second**.
Doing them first keeps the features off a broken base.

### 9b — the three cheap grammar additions

`TOP n`, a `VALUES` table source, a derived column list `AS V(Id)`.

`TOP n` is a spelling that maps onto the existing `LIMIT`. The other two interact — the corpus's own
first version entangled them in one shape — so they are done together and measured apart.

**Risk: the grammar.** The project's standing rule is that the grammar goes after everything that
touches it, and phase 3's rework is the reason. These are additive rather than structural, but the
grammar round-trip corpus is the net and must stay green.

### 9c — a correlated subquery in `FROM`

One capability, three spellings: `LATERAL` (PostgreSQL), `CROSS APPLY` and `OUTER APPLY` (SQL
Server), and `LEFT JOIN LATERAL … ON TRUE` is PostgreSQL's spelling of the outer form. Both targets
have it; only the spelling differs.

Cheaper than the plan assumed — see § 4 — but genuinely planner-adjacent, so it comes after 9b and
before the subsystem.

### 9d — functions and procedures

The subsystem, and the part that has to be designed rather than added. Open questions to settle
**before** any code, because each one is a fork the rest depends on:

1. **What is a body?** Phase 8 made the AST MemoryPackable and the catalog store trees; a routine
   body is a statement tree exactly as a trigger body now is. That is the design already in place and
   the answer should be the same one.
2. **Re-entrancy against the write lock.** Phase 8 measured DDL inside a trigger deadlocking against
   the lock held by the statement that fired it — *and failing part-way*. A procedure is a nested
   execution with the same exposure, and it must be designed for rather than discovered. This is the
   single largest risk in 9d.
3. **What may a routine contain?** Triggers were restricted to DML for the reason above. Procedures
   want to be less restricted than that, which is precisely why question 2 comes first.
4. **SQL-bodied only.** No external code execution, no assembly loading. PostgreSQL's `LANGUAGE SQL`
   and SQL Server's inline table-valued function are the shapes that matter for drop-in.
5. **Determinism, and where a function may appear.** A computed column, an index expression and a
   `CHECK` all evaluate on the row path. A function reachable from there re-enters the engine per row,
   which is question 2 again in its worst form.
6. **Catalog and reporting.** `DefinitionFunction`/`DefinitionProcedure` with appended MemoryPack
   union tags — never renumbered — and `INFORMATION_SCHEMA.ROUTINES` / `PARAMETERS`, which is what
   the standard exposes and what scaffolding would read.

**Acceptance for 9d is a design note answering 1–6, agreed before implementation.**

### 9e — what is skipped, documented

Whatever survives as a "no" gets a reasoned skip and a marked test that turns green if it is ever
built. Nothing on the list is currently in this bucket: every item was decided as build.

---

## 6. What was built, 2026-08-01

**9a and 9b and 9c are done.** 9d is the remaining item and is deliberately left to its own session.

### 9a - the two defects the feature work sat on

**An aggregate now resolves the same way wherever it appears.** One capability was torn across three
places, and each had to be fixed for any of them to matter: detection was a switch over four of the
AST's nineteen expression types falling through to `false`; `HAVING` evaluation was a switch over the
same four whose default handed the expression to the plain row evaluator, which refuses aggregates;
and the select-list projection used the accumulator only when the item *was* an aggregate rather than
when it *contained* one. A fourth thing surfaced only after those three - the group's rows were kept
only when a `HAVING` clause existed, so an item that needed them computed its aggregate over an empty
list and returned NULL, a wrong answer rather than an error.

**The revert test earned its keep again.** Putting the detector back left all three recorded tests
green: each writes `GROUP BY` explicitly, and that alone routes the query, so the detector was never
asked anything. The test that asks it drops `GROUP BY` - and finding that is what exposed the third
and fourth breaks.

**`SELECT *` over a derived table returns its columns once.** `IteratorAlias` puts every column into
the row twice on purpose, qualified and bare, so both `X.Id` and `Id` resolve; its schema is correct
and its rows are not a result. `SELECT *` passed rows through untouched unless an internal column was
present, and a derived table has none. Applying the wrapper conditionally was measured to be wrong:
with the condition "the top iterator is an alias", the same query under `WHERE`, `ORDER BY` or
`LIMIT` slips past, because each hands the row through unchanged.

### 9b - three additions, and a regression the corpus caught

`TOP n`, a `VALUES` table source, a derived column list. `VALUES` is a query term rather than only a
table source, so it works wherever a query goes, and it is carried on the select statement rather
than given a statement type of its own - which would have needed a union tag and forced every place
typed to `WitSqlStatementSelect` to learn a second shape.

**Adding `TOP` as a token took `Top` away as a column name**, and the keyword corpus said so:
*"these keywords stopped working as column names - a grammar regression"*. It is non-reserved now.
A pinned list that has been proved able to fail is worth having.

### 9c - one capability, cheaper than priced

`LATERAL`, `CROSS APPLY`, `OUTER APPLY`. The estimate said "real planner work"; the measurement said
the engine already evaluates a subquery per outer row in `EXISTS`, `IN` and a scalar position, and
what was missing was reaching that from a table source.

Join reordering is off whenever a `LATERAL` is present: the optimiser cannot know that a lateral
reads the row beside it, and one moved to the front would resolve its outer columns against nothing.

### Ledger

**33 `[Ignore(…)]` + 14 = 47**, from 35 + 14 = 49 when the phase opened. Three markers closed, one
opened and closed within the phase.

---

## 7. Next

- **9d, the routine subsystem**, in its own session. Its acceptance is the design note in § 5, and
  the question that shapes everything else is re-entrancy against the write lock: phase 8 measured
  DDL inside a trigger deadlocking against its caller *and failing part-way*, and a function reachable
  from a computed column or an index expression re-enters the engine **per row**.
- The oracle measures **syntax acceptance**. Whether the engines agree on the *answer* is a further
  question, and phase 3's lesson applies: an acceptance oracle cannot see a wrong answer. The shapes
  9b and 9c added are the natural first candidates for value comparison.
- `UnbuiltCapabilityCorpusTests` now pins six capabilities as built and two as absent. It inverted as
  each landed, which is what it was written to do rather than to be remembered.
