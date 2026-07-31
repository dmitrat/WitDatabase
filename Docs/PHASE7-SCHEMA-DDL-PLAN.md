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

## 3a. The corpus widened — three clusters, three signatures

Extended to the forms the markers name. **Asking three questions instead of one pays off here**: the
three clusters fail in three different ways, and counting them would have hidden that.

```
named-check                a constraint named ck_v      recorded=no   reported=no   enforced=yes
named-unique               a constraint named uq_s      recorded=no   reported=no   enforced=yes
named-foreign-key          a constraint named fk_p      recorded=no   reported=no   enforced=yes
add-column-unique          IsUnique on the added column recorded=no   reported=n/a  enforced=no
add-column-check           a CHECK on the added column  recorded=no   reported=n/a  enforced=no
add-column-references      a foreign key on the added column  recorded=no  reported=n/a  enforced=no
```

| Cluster | Signature | What it means |
|---|---|---|
| Declared sizes | `no / no / no` | Declared, accepted, and gone in every dimension |
| Named constraints | `no / no / **yes**` | **The constraint works and is anonymous** — so it can never be dropped |
| `ALTER TABLE ADD COLUMN` | `no / n/a / no` | Everything but the type is discarded |

The named-constraint row is the sharpest of the three: the marker recorded that
`ALTER TABLE DROP CONSTRAINT` cannot find the name, and the corpus adds that
`INFORMATION_SCHEMA.TABLE_CONSTRAINTS` cannot see it either, while the constraint itself is being
enforced the whole time. A constraint that works, cannot be listed, and cannot be removed.

## 3b. And one recorded finding turned out to be stale

The plan's *already measured* list says **`DROP COLUMN` leaves foreign-key and primary-key metadata
pointing at the dropped column, and the next insert throws `KeyNotFoundException`** — two of four.

Measured 2026-07-31, and it does not reproduce in any shape tried:

| Shape | Result |
|---|---|
| Drop columns carrying a foreign key, a `UNIQUE` and an index | no stale metadata of any kind; the next insert is accepted |
| Drop the column the **primary key** is on | **refused** with `InvalidOperationException` |
| Drop a column another table's **foreign key points at** | **refused** |

**Phase 1 fixed it in 2.2.0** — its record lists *"`DROP COLUMN` metadata"* among the twelve — and this
plan carried the pre-fix wording forward. The behaviour that is there now is the correct one, and
refusing to drop a depended-on column is what PostgreSQL and SQL Server do too, so it is **pinned as a
guarantee** rather than left as an observation.

Sixth time in this project that a record about the past turned out false when re-run. The rule earns its
keep: *a record about the past is a claim requiring re-verification.*

---

## 3c. Cluster 1 fixed — named constraints keep their names, and dropping one takes effect

The smallest of the three, and it turned out to be two changes rather than one.

**The name was never recorded.** `CREATE TABLE` built its `DefinitionTable` with `PrimaryKey`,
`UniqueConstraints`, `CheckExpressions` and `ForeignKeys` — and never `NamedConstraints`. A converter
from parsed constraint to catalog record already existed and was reachable only from
`ALTER TABLE ADD CONSTRAINT`, which is exactly why *that* path worked and the inline one did not. It is
now shared by both.

**And recording the name was not enough.** With it recorded, `DROP CONSTRAINT` stopped saying *not found*
and started saying nothing at all: it removed the name and left the enforcement behind, so the constraint
went on refusing rows under no name. The measurement said so immediately — *"the CHECK was dropped, so a
value of 99 must now be accepted"* — which is why the tests assert the **consequence** rather than the
absence of an exception.

Dropping now removes both halves:

| Constraint | What else had to go |
|---|---|
| `CHECK` | the matching entry in `CheckExpressions` |
| `FOREIGN KEY` | the matching entry in `ForeignKeys` |
| `UNIQUE` | the entry in `UniqueConstraints` **and** the column's own `IsUnique` mark — validation reads that separately, so leaving it kept refusing duplicates |

**Exactly one match is removed.** An identical constraint declared anonymously alongside a named one is a
different constraint, and dropping the named one must not take it with it.

**Why not simply keep named constraints out of the anonymous structures?** Because those structures are
what `INFORMATION_SCHEMA`, cascade handling and validation all read — checked before choosing, not
assumed. Keeping both and teaching `DROP` to remove both is the change that touches only the defect.

### What it cost, stated

A named inline constraint is now validated twice — once from the anonymous structure and once from the
named one. Same verdict, one extra expression evaluation. Worth knowing for phase 10 and not worth a
second mechanism today.

### Revert counts

| Fix reverted | Tests red |
|---|---|
| Both halves (production only, tests kept) | **4** — the three markers and the corpus |

The first attempt at this measurement reverted the *whole* diff and turned nothing red, because it took
the tests back with it. A revert count means nothing unless the tests survive the revert.

**Corpus after:** `named-check`, `named-unique`, `named-foreign-key` all `recorded=yes reported=yes
enforced=yes`. Two clusters left.

---

## 4. What is next in this phase

- ~~**Widen the corpus**~~ **Done** — § 3a. `DROP COLUMN` came out of it as stale rather than as work.
- **Then fix**, in the order the corpus makes obvious. The three signatures suggest their own order:
  **named constraints** are the smallest change (the constraint already works; only its name is lost),
  **`ADD COLUMN`** is next (the declaration is parsed and dropped on the floor), and **declared sizes**
  are the largest, because nothing downstream has anywhere to put them yet.
- **Watch the risk the plan names:** enforcement that was never applied will start rejecting data that
  used to be accepted. That is a behaviour change consumers will meet, and it belongs in a major.

**Acceptance, unchanged:** everything the DDL accepts is recorded, enforced, or refused at declaration
time — never accepted and ignored. `INFORMATION_SCHEMA` describes what was declared.
