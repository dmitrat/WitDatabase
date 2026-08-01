# Phase 8 — serializer round-trip

Working record. Written 2026-07-31. § 5 is an audit taken deliberately before merging; § 6 is the
decision it produced.

The phase opened with an instrument already paid for — a 193-entry corpus and `GrammarRoundTripTests`
pinning 69 failures **by cause**. The plan's task was not "fix 69" but "establish how many of them are
one defect in different clothes".

---

## 1. The classification, and what it overturned

Re-measured before anything was touched: **193 corpus entries, 124 clean, 69 failing** — the record
from 2026-07-28 held across three releases.

The 69 are **two** causes, not sixty-nine:

| Cause | Count | How it fails |
|---|---|---|
| The statement serializer handles DML only — `NotSupportedException` across 22 statement types | **62** | Loudly, at serialization |
| Every subquery emitted as the literal text `SELECT ...` | **7** | Silently at write, loudly at read |

**The plan's hypothesis was wrong on the count.** It expected the subquery placeholder to explain "a
large share"; it explains 7 of 69, ten per cent. The reserved-word list explains **none** of them —
that is a different corpus.

And reachability inverts the severity. The product calls the serializer in nine places, and **none of
them serializes a DDL statement back to SQL**. The 62 were a gap in the corpus, not in the product.
All 7 were reachable and destructive.

## 2. The instrument was blind, for the tenth time in this project

The property `GrammarRoundTripTests` checked was a **fixpoint**: serialize, re-parse, serialize, and
compare the two texts. That property is **idempotent under loss**. A clause dropped on the first pass
is absent from the second pass too, both texts agree, and the entry is counted clean.

Dumping pass-one output for the 124 "clean" entries found roughly **21 being silently destroyed**:

`UNION` / `UNION ALL` / `INTERSECT` / `EXCEPT` — the second branch gone entirely; `WITH … AS` — the
CTE definition dropped, leaving a reference to an undefined table; `ON CONFLICT`; `RETURNING`;
`INSERT OR REPLACE` / `OR IGNORE`; `UPDATE … FROM`; `DELETE … USING`; `FOR UPDATE`; `OFFSET` with no
`LIMIT`; `NULLS LAST`; the window frame clause.

Proved on the product, not on the serializer:

```
CREATE VIEW V1 AS SELECT Id FROM A UNION SELECT Id FROM B   -> CREATE ok
SELECT Id FROM V1                                            -> [1, 2]     correct is [1,2,3,4]
   stored body: <SELECT Id FROM A>

CREATE VIEW V2 AS WITH C AS (...) SELECT Id FROM C           -> CREATE ok
SELECT Id FROM V2                                            -> Table 'C' not found
```

**Created-then-silently-wrong**, which is worse than the created-then-broken shape the phase was
scoped around: no error at any point, ever.

## 3. Why the catalog now stores trees

The serializer was never a pretty-printer. The catalog's storage format for schema **was SQL text**,
and the engine re-parsed it on every use — so the serializer was the *write half of a persistence
codec* whose read half is `WitSql.Parse`. The two are required to be exact inverses; a gap is schema
corruption on disk.

The three reference databases solve this two ways. **SQLite** and **SQL Server** store the original
text verbatim and have no deparser. **PostgreSQL** re-renders from the tree and pays for a complete
one, maintained in lockstep with the grammar. WitDatabase had taken PostgreSQL's architecture at
SQLite's investment level, which is the root cause of every finding above.

Decided by Dmitry 2026-07-31: **store the tree**, in MemoryPack, as the rest of the catalog already
is. That makes drift impossible rather than fixed, and demotes the renderer from a codec to a
display function — a gap in a display is cosmetic, a gap in a codec is data loss.

### What that took

- **90 AST classes** made `[MemoryPackable]`, **7 union tables, 67 tags** assigned alphabetically by
  a script so the assignment is reproducible from the source tree.
- **One obstruction in the whole assembly**: `WitSqlExpressionLiteral.Value` is `object?`, which
  MemoryPack refuses. `LiteralValueFormatter` gives it an explicit tag table, so the set of types a
  stored literal can produce is closed and listed.
- Every catalog write path stores the tree: view body, partial-index filter, indexed expressions,
  computed columns, column `CHECK`, `DEFAULT`, table `CHECK`s, named constraints, trigger `WHEN` and
  body.

```
statements surviving the catalog : 194 of 194     (was 0 of 194)
```

### The pattern, for the ninth time in this project

A grep for `WitSql.ParseExpression` found 21 call sites, and they were all converted. **Ten more were
behind a caching wrapper** the grep never saw. Had the conversion stopped at 21, `UPDATE` and
validation would still read the *rendering* of the schema instead of the schema.

`StoredSchemaIsNeverReparsedTests` makes that mechanical: it scans the sources and fails on any parse
outside the resolvers, with an allow-list carrying a reason per entry — and it has its own control,
so a wrong root path cannot make it silently green.

## 4. Findings the instrument's control produced before it was used

Comparing trees needs `ModelBase.Is`, which is hand-written across 90 types — the same failure mode
as the hand-written serializer. `AstStructuralEqualityTests` mutates one stored value anywhere in a
parsed statement and requires the comparison to notice **at the root**, so an ancestor that ignores a
child is caught too. It found four defects, none in any audit:

| | |
|---|---|
| `TableSourceJoin.OnCondition` declared `required` non-nullable while the visitor assigns null for `CROSS JOIN` | `Is` and `Clone` both threw `NullReferenceException` on any cross join. The compiler had reported it as `CS8601` on every build |
| `WitSqlStatementInsert.Is` ended `Values?…== true` | `INSERT … SELECT` has no `VALUES`, so `null == true` — **such a statement never compared equal to itself** |
| the same expression flattened all rows | `VALUES (1,2),(3)` and `VALUES (1),(2,3)` compared equal |
| `WitSqlExpressionLiteral.Is` compared `Value` with `Equals` | `Value` for a BLOB is `byte[]`, so reference equality — `X'DEADBEEF'` never equalled itself |

The instrument was itself wrong first, for the eleventh time: its first run reported 43 findings, of
which **41 were phantoms** — it replaced an already-empty collection with an empty collection and
recorded that as a mutation. "A value was assigned" is not "a value changed"; it now reads the value
back and requires a real difference.

## 5. Audit before merge

Taken 2026-07-31 at Dmitry's instruction, deliberately before merging.

### It broke something, and only the audit found it

**`ALTER TABLE … ALTER COLUMN … SET DEFAULT` and `DROP DEFAULT` silently stopped working.**

```
                       branch (broken)   main    branch (fixed)
after CREATE DEFAULT 1     N = 1         N = 1       N = 1
after SET DEFAULT 5        N = 1         N = 5       N = 5
after DROP DEFAULT         N = 1         N = NULL    N = NULL
```

`SetColumnDefault` wrote the text and left the tree; the resolver prefers the tree. The statement
reported success, changed what `INFORMATION_SCHEMA` said, and changed nothing about what was
inserted — the catalog claimed no default while the engine kept applying the old one.

**All 5,600 tests across five projects were green.** `ALTER COLUMN SET DEFAULT` has no behavioural
test; the suite asserts that DDL is *accepted*, not what it *does*.

This is the cost of the design, not an accident of it: **the same fact is now stored twice.** So the
fix is an invariant rather than a patch. `CatalogCoherenceTests` asserts, after any sequence of DDL
including rewrites, that the text a definition carries is what its tree renders to — or is absent.
One check covers the whole class, including the write path nobody has added yet. Verified red without
the fix: `T.N DEFAULT: tree renders to <1> but the catalog reports <5>`.

A second of the same family: `INFORMATION_SCHEMA` dereferenced `ComputedExpression!` under
`IsComputed`, and the text is now allowed to be absent while the tree is present.

### Baselines — everything else is pre-existing

| Suite | Branch | `main` |
|---|---|---|
| Parser | 774 / 0 | — |
| Engine | 2019 / 0 | — |
| Core | 2278 / 0 | — |
| AdoNet | 788 / 0 | — |
| EntityFramework | 554 / 0 | — |
| **EF Specification** | **1198 failed / 6934 passed** | **1198 failed / 6934 passed** |

The EF Specification failures are **identical on both sides** — pre-existing, and that suite is an
information source rather than a gate. The `Conformance` and `Oracle` categories the CI filter
excludes do not exist in any of these projects.

Two further defects found and measured against `main` as **pre-existing, not introduced**:
`RENAME COLUMN` leaves the column's `CHECK` naming the old column, so the table cannot be written to
at all afterwards; `DROP COLUMN` leaves a table `CHECK` naming the dropped column, likewise. Recorded
in `ColumnRenameAndDropFindingsTests`.

### What the change costs, measured

| | |
|---|---|
| **Downgrade is impossible** | 8.x opening a 9.0.0 file: `property count is 3 but binary's header marked as 4`. MemoryPack's version tolerance is one-directional — new code reads old files, old code cannot read new ones |
| **Schema records grow ~3.4×** | 38 → 128 bytes for a small view. Irrelevant in absolute terms; schema is small and written rarely. The tree is **not** more compact than the text it replaces |
| **67 union tags are a permanent format commitment** | Renumbering one makes old files deserialize into a different node type, silently. Pinned exhaustively by `AstMemoryPackContractTests` |
| **One extra parse per DDL statement** | The display rendering is verified by re-parsing. ~90 µs per expression, on a cold path |
| **The same fact stored twice** | The coherence hazard above. Held by an invariant test |

Against that, measured: the created-then-broken and created-then-silently-wrong classes are gone
structurally; 194 of 194 statements survive the catalog against 0 before; the ANTLR parse is off the
row write path (37–70 µs per row, which the end-to-end measurement could **not** resolve against
run-to-run noise — the mechanism is certain, the magnitude is single-digit percent); the renderer can
no longer refuse a write or report a definition that is not the object's; and the reserved-word list
cannot drift, where it had drifted to **102 keywords that did not survive a round trip**.

### Ledger

**35 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 49**, unchanged. Two markers closed (the view
body's subquery, the partial index's filter) and two opened for the pre-existing rename/drop defects.

The partial-index marker was **rewritten as it was closed**: its original assertion was
`FILTER_CONDITION` does not contain `...`, which would now pass for the wrong reason, since the text
is withheld and nothing contains an ellipsis either. It asserts on rows now.

---

## 6. The decision, taken: C

Dmitry chose **C — store only the tree, render the text on demand** — 2026-07-31. Nothing writes the
legacy text fields; they are read for a database written before 9.0.0, and `INFORMATION_SCHEMA` renders
its own SQL from the tree when asked (~90 µs per expression, memoised, on a cold path).

**C is the honest end-state of the argument for A, and the audit is what made it visible.** The one
thing that actually broke came from storing a fact twice.

### Removing the second copy found a class the first version had hidden

When the text stopped being written, three branches that had been asking the **description** whether the
schema existed started answering "no". They had been right by accident:

| | |
|---|---|
| `Validation.cs` and `Update.cs`: `if (col.CheckExpression == null) continue` | **a column `CHECK` stopped being enforced** — `INSERT … VALUES (3, 99)` against `CHECK (V < 10)` was accepted |
| `DefinitionColumn.IsComputed` read the text | a computed column stopped reporting itself as computed |
| `DefinitionIndex.IsFiltered` read the text | **and `OptimizerQuery` reads that** to decide whether an index covers a query. A partial index reporting itself as unfiltered gets used for a query needing every row, and **rows go missing from the answer** |

The third is worse than anything else this phase touched: a wrong query result with no error anywhere.
**Option C is what exposed it** — removing the text turned a silent wrong answer into a loud failing
test. Under A all three would still be sitting there, right by coincidence, until someone changed a
write path.

### What holds the design now

- `CatalogCoherenceTests.NothingWritesTheLegacyTextFieldsTest` — no write path may populate a legacy
  text field again, which is the only way the two-copy class can return.
- `CatalogCoherenceTests.DerivedAnswersDoNotDependOnTheRenderedTextTest` — `IsComputed`, `IsFiltered`,
  `HasExpressions` and `CHECK` enforcement answer from the tree.
- `CatalogCoherenceTests.CatalogStillReportsWhatItCanRenderTest` — dropping the stored text did not
  empty the catalog.
- `StoredSchemaIsNeverReparsedTests` — nothing outside the resolvers parses stored schema.
- `AstMemoryPackContractTests` — the 67 union tags are pinned exhaustively.
- `CatalogBackwardCompatibilityTests` — every 8.x record shape still opens.

### Final state

```
Parser 774 / 0     Engine 2021 / 0     Core 2278 / 0     AdoNet 788 / 0     EntityFramework 554 / 0
Engine on net9.0: 2021 / 0
EF Specification: 1198 failed / 6934 passed — identical to main, pre-existing
```

**Ledger: 35 + 14 = 49**, unchanged. Two closed, two opened for the pre-existing rename/drop defects.

---

## 7. Next

- **Drop `net9.0`.** Its support ended 12 May 2026; `net10.0` is LTS to November 2028. Keeping a target
  with no security updates is a promise the package cannot keep, and it halves the build and test
  matrix. Deliberately a **separate PR after this one**, so a storage-format change and a target-matrix
  change do not fail together and become indistinguishable.
- **`netstandard2.0` was considered and is not recommended.** The MemoryPack markup this phase now
  depends on needs a source generator and `init`/`required`; supporting .NET Framework that way would
  mean a second set of behaviour, which is the shape behind most defects this project has found. If it
  is ever needed, the service wrapper already agreed for multi-process access answers it better.
- Mutation coverage: **1,432 of 6,681 sites** in the `Is` control have no mutation strategy (21%).
  Reported rather than hidden; raising it is the instrument's remaining work.
