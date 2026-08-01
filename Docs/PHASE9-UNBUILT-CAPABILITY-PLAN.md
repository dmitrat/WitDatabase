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
inflates the ledger. The ledger stays at **35 + 14 = 49**.

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

## 4. Open — the decision itself

Costs below are estimates and are **not** measured; they are stated as estimates deliberately.

| Item | Both targets? | Shape of the work |
|---|---|---|
| derived column list `AS V(Id)` | yes | grammar + alias binding |
| `VALUES` as a table source | yes | grammar + a table source that yields literal rows |
| `TOP n` | spelling only | grammar; maps onto existing `LIMIT` |
| correlated subquery in `FROM` (`LATERAL` / `APPLY`) | yes, two spellings | real planner work: the subquery sees the outer row |
| user-defined functions | yes | a subsystem — stored code, lifecycle, security |
| stored procedures | yes | a subsystem |

**Not yet decided.** The rule agreed for it is value for the drop-in goal × how often real code uses
it, against implementation cost and the risk it adds to the engine, and the hard rule is that a skip
must be documented as a reasoned skip with a marked test that turns green if it is ever built.

## 5. Next

- Take the decision, item by item, against the table above.
- Fix `HAVING` with an aggregate inside `BETWEEN`/`IN` — already recorded, now confirmed by three
  engines, and independent of every decision here.
- The oracle currently measures **syntax acceptance**. Whether the two targets *agree on the answer* a
  shape produces is a further question, and the phase-3 lesson applies: an acceptance oracle cannot
  see a wrong answer.
