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

## 3d. Cluster 2 fixed — `ALTER TABLE ADD COLUMN` was a second, partial column builder

The defect reads plainly once the two are side by side:

| | Constraints understood |
|---|---|
| `CREATE TABLE` → `BuildColumnDefinition` | `NOT NULL`, `PRIMARY KEY`, `UNIQUE`, `DEFAULT`, `CHECK`, `REFERENCES` |
| `ALTER TABLE ADD COLUMN` | **`NOT NULL` and `DEFAULT`** |

Everything else fell through the second switch **in silence**. A column added with `UNIQUE` arrived
without it: constrained in the DDL the user wrote, unconstrained in the database, and every violating
insert accepted.

**Two column builders is the defect; there is one now.** `ADD COLUMN` calls the same
`BuildColumnDefinition` that `CREATE TABLE` uses — the same shape as cluster 1, where a converter existed
and one path did not reach it. That is twice in one phase, which is worth noticing as a pattern rather
than as two coincidences: **when a DDL form misbehaves here, look first for a second implementation of
the thing it should have called.**

**`PRIMARY KEY` is refused rather than half-recorded.** A primary key is a property of the table — it
needs the key list rewritten and every existing row checked — and `AddColumn` only appends a column.
`ALTER TABLE ADD CONSTRAINT` already refuses it for the same reason, so refusing here is consistent
rather than new.

### Measurements

**Corpus after:** `add-column-unique`, `add-column-check`, `add-column-references` all
`recorded=yes enforced=yes`. **One cluster left.**

| Fix reverted | Tests red |
|---|---|
| The shared column builder (production only, tests kept) | **4** — the three markers and the corpus |

---

## 3e. Cluster 3 — declared sizes, recorded, reported and enforced

The class that escaped the audit, and the phase's behaviour change.

### Recording was three lines, and reporting came free

**The parser had always carried the sizes.** `VARCHAR(5)` arrives at the DDL executor with `Length = 5`,
`DECIMAL(5,2)` with `Precision` and `Scale`; `DefinitionColumn` had always had `MaxLength`, `Precision`
and `Scale` to put them in. **Nothing copied one to the other.** That is the whole of why a 104-finding
audit missed it — both halves looked right, and only asking the database what it thought it had stored
showed that they were never connected.

`INFORMATION_SCHEMA.COLUMNS` reads those fields, so `recorded=yes` made `reported=yes` in the same
change, with nothing else touched.

### Enforcement follows PostgreSQL rather than being stricter

Drop-in is the target, so the rules are the reference's:

| Declaration | Rule |
|---|---|
| String longer than `VARCHAR(n)`/`CHAR(n)` | **Refused**, not truncated — silently losing the end of a value is the one outcome nobody can want |
| More decimals than the scale | **Accepted** — PostgreSQL rounds; it is not an error |
| Integer part too large for `precision - scale` | **Refused** — that is overflow, and no rounding saves it |

**This is the behaviour change the plan predicted:** data that used to be accepted is now refused. It
belongs in a major.

### And the fix has a gap, pinned rather than glossed

The declared **scale is not applied to the stored value**: `123.456` into `DECIMAL(5,2)` is accepted, its
precision checked against the rounded value, and then stored as `123.456` where PostgreSQL stores
`123.46`.

**The test caught it, which is the reason the semantics got a test at all** — the rule had been written
down in a comment and would have shipped as a description of something that was not happening. Applying
the scale means *coercing the row before it is written*, in the insert and update paths, which is a
change to the write path rather than another check, and it is not made here.

It is pinned in place, the right way round:

> `Assert.That(stored, Is.EqualTo(123.456m), "the scale is not applied to the stored value yet - if it
> now is, invert this pin")`

Saying "scale enforced" while storing an unrounded value would be the same defect this phase exists to
close, one level in.

### The corpus, complete

Every one of the sixteen entries is now `recorded=yes`, `reported=yes` where `INFORMATION_SCHEMA` has a
column for it, and `enforced=yes` where there is something to violate. **All three clusters closed.**

---

## 3f. The size was being dropped in FOUR places, and only the last one was recorded

With the engine half done, the EF half became testable for the first time — and the plan's entry for it
turned out to be **stale in the same way `DROP COLUMN` was**: the migrations generator already emitted
`VARCHAR(n)` for `AddColumn`, with a comment explaining the fix. So that was two of the plan's *already
measured* items now overtaken.

**But the end-to-end path had never been tested**, and it failed at once: a model with
`HasMaxLength(5)`, created with `EnsureCreated()`, accepted six characters and reported no length at all.
Following it down found **two more places** the size was being lost:

| Layer | What it did |
|---|---|
| EF type mapping source | Returned the **plain `TEXT` mapping** whatever the model declared, so `ColumnType` was already `"TEXT"` before anything else ran |
| Migrations generator, `CreateTable` | Called `GetColumnType(column.ClrType)` — the CLR-only overload — where `AddColumn` called `GetColumnType(column)` |
| DDL executor | Never copied `Length`/`Precision`/`Scale` into the catalog *(§ 3e)* |
| Validation | Never enforced them *(§ 3e)* |

**The third occurrence of this phase's pattern, and the sharpest.** In `CreateTable` the "second
implementation" was an **overload of the same method name**: `GetColumnType(column.ClrType)` reads exactly
like a call to the right thing. `ADD COLUMN` kept the size and `CREATE TABLE` dropped it, which is why a
migrated schema and a created one disagreed.

And the type mapping source was the deepest of the four: with `ColumnType` already `"TEXT"`, the
generator's own size handling could never run — `column.ColumnType ?? GetColumnType(column)` short-
circuits. **Fixing the generator alone changed nothing observable**, which is the same shape as the
original pair of defects and the reason this class survived so long.

### End to end, executed

```
PROBE  six characters into HasMaxLength(5)  ->  Value too long for column 'Codes.Value': 6 characters, declared 5.
PROBE  INFORMATION_SCHEMA says the column's length is  ->  5
```

`DeclaredSizeEndToEndTests` is deliberately an end-to-end test rather than another seam test: every seam
here already had one, and **not one of them would have caught this**.

---

## 3g. Scaffolding asked SQLite's catalog — of a database that is not SQLite

The last two markers in the area, and the interesting part is that **the right answer was not to implement
what they asked for**.

`WitDatabaseModelFactory` — this provider's own code — issued `SELECT name FROM sqlite_master` and four
`PRAGMA`s, evidently carried over from the SQLite provider it was started from. The engine has never had
either, and `PRAGMA` is not even a word in its grammar, so `dotnet ef dbcontext scaffold` failed on its
first query: database-first was not incomplete, it was **inoperative**.

**Emulating another database's private catalog would have been the wrong answer to a right complaint.**
The engine implements `INFORMATION_SCHEMA` — the standard catalog, which is what PostgreSQL and SQL
Server expose and what the drop-in target actually is. The factory reads that now: tables from `TABLES`,
columns from `COLUMNS`, keys from `KEY_COLUMN_USAGE`, delete rules from `REFERENTIAL_CONSTRAINTS`,
indexes from `INDEXES`.

**Two defects fell out of the rewrite:**

- The primary key was **inferred from which columns came back auto-generated**, so a table whose key was
  not auto-generated scaffolded with no key at all. It is read from the catalog now.
- The store type was to be taken straight from `DATA_TYPE` — and the standard catalog reports the base
  type in one column and the **size in others**, so a scaffolded model would have lost the declared
  length again. Composed instead. *That is this phase's own defect, one layer further out, and it was
  avoided only because the phase had just spent its length chasing it through four others.*

**The two tests are kept, inverted.** They now assert that the engine **refuses** `sqlite_master` and
`PRAGMA` — because pretending to be SQLite is what got the factory written that way in the first place.

---

## 3h. The scale, applied — and the hole that finding it exposed

The gap § 3e pinned rather than glossed: `DECIMAL(5,2)` had its precision checked and its **scale
ignored**, so `123.456` was stored unrounded. Closing it means coercing the row before it is written -
rounding to the declared scale, as PostgreSQL does - and the row is only rebuilt when a value actually
changes, so an ordinary write allocates nothing.

**Six write paths reach the store**, so the test was written as one case per path rather than one per
feature. That is what caught the real finding.

### `UPDATE` had a third validation entry point, and it enforced no sizes at all

The insert paths call `ValidateConstraints` or `ValidateConstraintsWithAutoGen`. `UPDATE` has a **fast
path** with its own — `ValidateConstraintsFastPath` — which the size enforcement shipped earlier in this
phase never reached. So:

> **A hundred characters could be written into a `VARCHAR(5)` by updating a row that already existed.**

The enforcement looked complete because every test that exercised it inserted. **It was found by a test
written for the scale, not for the length**, and only because that test wrote through more than one path.

**Fourth occurrence of this phase's pattern**, and the fourth different disguise: a converter reachable
from one caller (§ 3c), a partial second builder (§ 3d), an overload of the same name (§ 3f), and now a
*fast path* — an optimisation that quietly became a second implementation of validation.

### The rule this phase earned

> **When a check is added, ask which paths reach it — not whether the check is right.** Every defect in
> this phase was a correct piece of code that one route did not go through.

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
