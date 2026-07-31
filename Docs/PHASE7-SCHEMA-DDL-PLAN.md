# Phase 7 — schema and DDL fidelity

Working record. Phase 6 closed on 2026-07-31 and shipped as 7.0.0; this is the phase that follows it.

**The premise, from `NEXT-SESSION-PLAN.md`:** *the database's description of itself, and whether it
matches what was declared.* This is the phase whose instrument had to be built before anything could be
worked, because **a whole class went unrecorded through a 104-finding audit** — declared sizes — and
surfaced only when enforcement was written and never fired.

---

## 1. The area, counted

| Where | Markers |
|---|---|
| `Engine.Tests/AuditVerification/EngineSchemaDdlFindingsTests.cs` | 8 — three named constraints, three `ALTER TABLE ADD COLUMN`, two catalog-compatibility (`sqlite_master`, `PRAGMA`) |

Plus the four items the plan lists as *already measured* and unmarked: declared sizes never recorded,
`DROP COLUMN` leaving key metadata behind, and EF migrations dropping `maxLength`/`precision`/`scale`.

---

## 2. Instrument — the DDL round-trip corpus

`Engine.Tests/AuditVerification/DdlRoundTripCorpusTests`, pinned in
`Engine.Tests/Schema/ddl-round-trip-corpus.txt`.

**A declaration can fail in three independent ways, and the recorded findings show all three diverging.**
So the corpus asks three questions of every entry rather than one:

| | |
|---|---|
| **Recorded** | did the declaration reach the catalog? |
| **Reported** | does `INFORMATION_SCHEMA` describe it? |
| **Enforced** | is a value that violates it refused? |

Each entry gets its own table, its own violating `INSERT`, and — where a violation needs an existing row
to collide with — a seed insert first, so that a refusal cannot be a table that never accepted anything.

**Pinned as data, not as expectations.** The corpus writes a table and compares it against the file,
failing in **both** directions: a declaration that stops being honoured is a regression, one that starts
being honoured means the pin is stale, which is a fix landing. Same shape as phase 3's keyword corpus and
for the same reason — a diff should read as *"these declarations changed status"*.

**Verified in both directions before being trusted:** altering the pinned file to claim a fix that had
not happened turns the corpus red.

---

## 3. What the first run says

```
varchar-length             MaxLength = 5                recorded=no   reported=no   enforced=no
char-length                MaxLength = 3                recorded=no   reported=no   enforced=no
decimal-precision-scale    Precision = 5, Scale = 2     recorded=no   reported=no   enforced=no
numeric-precision-scale    Precision = 4, Scale = 1     recorded=no   reported=no   enforced=no
not-null                   IsNullable = false           recorded=yes  reported=yes  enforced=yes
default                    DefaultValue = 'x'           recorded=yes  reported=yes  enforced=n/a
primary-key                IsPrimaryKey = true          recorded=yes  reported=n/a  enforced=yes
unique                     IsUnique = true              recorded=yes  reported=n/a  enforced=yes
check-column               a column CHECK               recorded=yes  reported=n/a  enforced=yes
check-table                a table CHECK                recorded=yes  reported=n/a  enforced=yes
```

**The size class is absent in all three dimensions at once**, which is the sharpest available statement
of it: not "recorded but not enforced", not "enforced but not reported" — declared, accepted, and then
gone. Everything else the corpus covers so far is honoured end to end.

### The instrument was wrong before its subject, for the ninth time

The first version asked only whether the **table** carried a check expression, and reported
`check … recorded=no enforced=yes` — a constraint that works but does not appear in the catalog, which
would have been a striking finding and was the instrument's mistake. A column-level `CHECK` lands on the
**column**; a table-level one on the table. The corpus now asks both separately, and both are recorded.
Caught before it became a claim, which is the only reason it is a footnote rather than a correction.

### SQLite is the wrong oracle for this phase, and saying so matters

The standing rule is *SQLite settles attribution, never desirability*, and here it does not even settle
attribution: **SQLite does not enforce `VARCHAR(5)` either.** Its type affinity system ignores declared
lengths, and `DECIMAL(5,2)` is stored without precision. So an oracle run against SQLite would bless
exactly the gap this phase exists to close. The reference for declared sizes is PostgreSQL and SQL
Server, which is also what the drop-in target actually is.

---

## 4. What is next in this phase

- **Widen the corpus** to the forms the markers name: named constraints in `CREATE TABLE`,
  `ALTER TABLE ADD COLUMN` with `UNIQUE`/`CHECK`/`REFERENCES`, and `DROP COLUMN` leaving key metadata
  behind. Each is a round trip the corpus can already express.
- **Then fix**, in the order the corpus makes obvious: a declaration that is recorded but unenforced is a
  smaller change than one that never reaches the catalog.
- **Watch the risk the plan names:** enforcement that was never applied will start rejecting data that
  used to be accepted. That is a behaviour change consumers will meet, and it belongs in a major.

**Acceptance, unchanged:** everything the DDL accepts is recorded, enforced, or refused at declaration
time — never accepted and ignored. `INFORMATION_SCHEMA` describes what was declared.
