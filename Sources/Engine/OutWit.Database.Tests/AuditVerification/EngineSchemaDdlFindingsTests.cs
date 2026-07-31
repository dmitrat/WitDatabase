namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the six <c>engine-schema-ddl</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Run 2026-07-27 against <c>main</c> at a668f73. One of the six is a duplicate carried across
/// dimensions: "self-referencing foreign keys never cascade"
/// (<c>StatementExecutor.Validation.cs:89</c>) is the same defect as the <c>engine-dml</c> entry at
/// line 91, already confirmed in <see cref="EngineDmlFindingsTests"/>. The remaining five are
/// verified here and all reproduce, though the DROP COLUMN claim is narrower than it was written.
///
/// Same convention as the sibling fixtures: every test asserts the <b>correct</b> behaviour, so a
/// failure confirms the finding. Confirmed tests carry <c>[Ignore]</c> with what was observed;
/// passing ones stay active as pins. See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class EngineSchemaDdlFindingsTests : WitSqlEngineTestsBase
{
    #region Named constraints declared in CREATE TABLE lose their names

    // CONFIRMED 2026-07-27 for all three constraint kinds. ALTER TABLE DROP CONSTRAINT raises
    // "InvalidOperationException: Constraint '<name>' not found on table '<table>'" from
    // WitSqlEngine.Ddl.Tables.cs:800 - the name given in CREATE TABLE never reaches the catalog.
    // Named constraints added later via ALTER TABLE ADD CONSTRAINT do work, which is why the
    // existing WitSqlEngineAlterTableConstraintTests never caught this.
    //
    // Worth stating in the engine's favour: this fails loudly. EF Core's DropForeignKey and
    // DropCheckConstraint migrations will throw rather than silently leave the constraint in place,
    // so the consequence is a blocked migration, not corrupted data.
    // engine-schema-ddl, Statements/StatementExecutor.Ddl.Tables.cs:128

    // FIXED 2026-07-31 (phase 7). CREATE TABLE now records an inline constraint name, so all
    // three tests below pass; the reason string is kept as the record of what was measured.
    private const string NamedConstraintIgnoreHistory =
        "CONFIRMED 2026-07-27: ALTER TABLE DROP CONSTRAINT raises \"Constraint '<name>' not found\" " +
        "- an inline CREATE TABLE constraint name never reaches the catalog. " +
        "engine-schema-ddl, Statements/StatementExecutor.Ddl.Tables.cs:128";

    [Test]
    public void NamedCheckConstraintFromCreateTableCanBeDroppedTest()
    {
        m_engine.Execute(@"
            CREATE TABLE T (
                Id INT PRIMARY KEY,
                V INT,
                CONSTRAINT CK_V CHECK (V < 10))");

        m_engine.Execute("ALTER TABLE T DROP CONSTRAINT CK_V");

        // The drop must actually take effect, not merely be accepted.
        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 99)"),
            Throws.Nothing, "the CHECK was dropped, so a value of 99 must now be accepted");
    }

    [Test]
    public void NamedForeignKeyFromCreateTableCanBeDroppedTest()
    {
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY)");
        m_engine.Execute(@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                PId INT,
                CONSTRAINT FK_C_P FOREIGN KEY (PId) REFERENCES P(Id))");

        m_engine.Execute("ALTER TABLE C DROP CONSTRAINT FK_C_P");

        Assert.That(() => m_engine.Execute("INSERT INTO C (Id, PId) VALUES (1, 999)"),
            Throws.Nothing, "the foreign key was dropped, so an unmatched value must be accepted");
    }

    [Test]
    public void NamedUniqueConstraintFromCreateTableCanBeDroppedTest()
    {
        m_engine.Execute(@"
            CREATE TABLE T (
                Id INT PRIMARY KEY,
                Email VARCHAR(50),
                CONSTRAINT UQ_Email UNIQUE (Email))");

        m_engine.Execute("ALTER TABLE T DROP CONSTRAINT UQ_Email");

        m_engine.Execute("INSERT INTO T (Id, Email) VALUES (1, 'a@b.c')");
        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, Email) VALUES (2, 'a@b.c')"),
            Throws.Nothing, "the UNIQUE constraint was dropped, so a duplicate must be accepted");
    }

    #endregion

    #region ALTER TABLE ADD COLUMN discards column constraints

    // CONFIRMED 2026-07-27 for UNIQUE, CHECK and REFERENCES: every violating INSERT is accepted
    // without an exception. Unlike the named-constraint defect above, this one is silent - the
    // column looks constrained in the DDL the user wrote and is not constrained in the database.
    // engine-schema-ddl, Statements/StatementExecutor.Ddl.Tables.cs:283

    private const string AddColumnIgnore =
        "CONFIRMED 2026-07-27: the constraint written on the added column is silently discarded " +
        "and the violating INSERT is accepted. " +
        "engine-schema-ddl, Statements/StatementExecutor.Ddl.Tables.cs:283";

    [Test]
    [Ignore(AddColumnIgnore)]
    public void AddColumnKeepsItsUniqueConstraintTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE T ADD COLUMN Email VARCHAR(50) UNIQUE");

        m_engine.Execute("INSERT INTO T (Id, Email) VALUES (1, 'a@b.c')");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, Email) VALUES (2, 'a@b.c')"),
            Throws.Exception, "the column was declared UNIQUE, so the duplicate must be rejected");
    }

    [Test]
    [Ignore(AddColumnIgnore)]
    public void AddColumnKeepsItsCheckConstraintTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE T ADD COLUMN Age INT CHECK (Age >= 0)");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, Age) VALUES (1, -5)"),
            Throws.Exception, "-5 violates the CHECK declared on the added column");
    }

    [Test]
    [Ignore(AddColumnIgnore)]
    public void AddColumnKeepsItsForeignKeyTest()
    {
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO P (Id) VALUES (1)");
        m_engine.Execute("CREATE TABLE C (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE C ADD COLUMN PId INT REFERENCES P(Id)");

        Assert.That(() => m_engine.Execute("INSERT INTO C (Id, PId) VALUES (1, 999)"),
            Throws.Exception, "999 does not exist in P, so the reference must be rejected");
    }

    #endregion

    #region DROP COLUMN leaves the dropped column in the schema metadata

    // CONFIRMED IN PART 2026-07-27. The finding names four metadata kinds - PRIMARY KEY, UNIQUE,
    // FOREIGN KEY and index - and only two of them are affected:
    //
    //   index over the dropped column ......... cleaned up correctly (test passes)
    //   UNIQUE over the dropped column ........ cleaned up correctly (test passes)
    //   FOREIGN KEY over the dropped column ... BROKEN: KeyNotFoundException "Column 'PId' not found"
    //   PRIMARY KEY over the dropped column ... BROKEN: KeyNotFoundException "Column 'Id' not found"
    //
    // Dropping the column a foreign key points *at* is also handled (test passes). So the table is
    // left un-insertable in exactly two of the five shapes probed, and the drop is accepted rather
    // than refused in both.
    //
    // Unrelated but visible here: the failure surfaces as a raw KeyNotFoundException, which is the
    // cross-cutting "the engine never throws a DbException" finding showing through.
    //
    // Note this is NOT the DROP COLUMN defect fixed in 2.0.0 - that one re-serialised surviving rows
    // against the pre-drop column list. This is the metadata half, which that fix did not touch.
    // engine-schema-ddl, Schema/SchemaCatalog.Columns.cs:41

    [Test]
    public void TableStaysInsertableAfterDroppingAnIndexedColumnTest()
    {
        // Passes - index metadata is cleaned up. Pin for the half that already works.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, A INT, B INT)");
        m_engine.Execute("CREATE INDEX IxA ON T (A)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, B) VALUES (1, 5)"), Throws.Nothing,
            "the index over the dropped column must go with it");
    }

    [Test]
    public void TableStaysInsertableAfterDroppingAUniqueColumnTest()
    {
        // Passes - UNIQUE metadata is cleaned up.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, A INT UNIQUE, B INT)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, B) VALUES (1, 5)"), Throws.Nothing,
            "the UNIQUE constraint over the dropped column must go with it");
    }

    [Test]
    public void ParentStaysInsertableAfterDroppingTheReferencedColumnTest()
    {
        // Passes - dropping the column a foreign key points at leaves the parent usable.
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY, Code INT UNIQUE, B INT)");
        m_engine.Execute(@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                PCode INT,
                FOREIGN KEY (PCode) REFERENCES P(Code))");

        try
        {
            m_engine.Execute("ALTER TABLE P DROP COLUMN Code");
        }
        catch (Exception e)
        {
            TestContext.Out.WriteLine($"drop refused: {e.GetType().Name}: {e.Message}");
            return;
        }

        Assert.That(() => m_engine.Execute("INSERT INTO P (Id, B) VALUES (1, 5)"), Throws.Nothing,
            "the drop was accepted, so the parent must remain insertable");
    }

    [Test]
    public void TableStaysInsertableAfterDroppingAForeignKeyColumnTest()
    {
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO P (Id) VALUES (1)");
        m_engine.Execute(@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                PId INT,
                B INT,
                FOREIGN KEY (PId) REFERENCES P(Id))");

        m_engine.Execute("ALTER TABLE C DROP COLUMN PId");

        Assert.That(() => m_engine.Execute("INSERT INTO C (Id, B) VALUES (1, 5)"), Throws.Nothing,
            "the foreign key over the dropped column must go with it");
    }

    [Test]
    public void TableStaysInsertableAfterDroppingAPrimaryKeyColumnTest()
    {
        // Refusing the drop outright would also be a correct outcome - what must not happen is
        // accepting it and leaving the table broken.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, B INT)");

        try
        {
            m_engine.Execute("ALTER TABLE T DROP COLUMN Id");
        }
        catch (Exception e)
        {
            TestContext.Out.WriteLine($"drop refused: {e.GetType().Name}: {e.Message}");
            return;
        }

        Assert.That(() => m_engine.Execute("INSERT INTO T (B) VALUES (5)"), Throws.Nothing,
            "the drop was accepted, so the primary key over the dropped column must have gone too");
    }

    #endregion

    #region Cascade matches positionally against the parent PK instead of fk.ForeignColumns

    // CONFIRMED 2026-07-27, and this is the most damaging finding in the batch. Cascade matching
    // compares the child's FK values positionally against the parent's PRIMARY KEY, so whenever the
    // foreign key does not point at the primary key - or points at its columns in a different order
    // - it goes wrong in BOTH directions at once:
    //
    //   * it deletes a child whose referenced row is still there (silent data loss), and
    //   * it fails to delete a child whose referenced row is gone (silent orphan).
    //
    // Both are reproduced below on the same two-row fixture, so this is not an edge case that needs
    // contrived data - it needs only a foreign key to a UNIQUE column, which is ordinary schema.
    // engine-schema-ddl, Statements/StatementExecutor.Validation.cs:277

    [Test]
    public void CascadeFollowsTheReferencedColumnNotThePrimaryKeyTest()
    {
        // The FK below points at P.Code, which is UNIQUE but is not the primary key. The child row
        // legitimately references Code = 100, which belongs to the parent row Id = 2. Deleting the
        // *other* parent row - Id = 100, Code = 1 - must leave the child alone. A positional
        // comparison against the PK sees child.PCode (100) == deleted P.Id (100) and cascades.
        SeedNonPrimaryKeyReference();

        m_engine.Execute("DELETE FROM P WHERE Id = 100");

        Assert.That(Count("C"), Is.EqualTo(1),
            "the child references Code = 100, which still exists, so it must not be deleted");
    }

    [Test]
    public void CascadeRemovesTheChildWhenItsReferencedRowGoesTest()
    {
        SeedNonPrimaryKeyReference();

        m_engine.Execute("DELETE FROM P WHERE Id = 2");

        Assert.That(Count("C"), Is.EqualTo(0),
            "Code = 100 is gone, so the child row referencing it must cascade away");
    }

    [Test]
    public void CascadeHonoursCompositeForeignKeyColumnOrderTest()
    {
        m_engine.Execute("CREATE TABLE P (A INT, B INT, PRIMARY KEY (A, B))");
        m_engine.Execute(@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                X INT,
                Y INT,
                FOREIGN KEY (X, Y) REFERENCES P(B, A) ON DELETE CASCADE)");
        m_engine.Execute("INSERT INTO P (A, B) VALUES (1, 2)");
        // (X, Y) maps to (B, A), so X = 2, Y = 1 references P(A = 1, B = 2).
        m_engine.Execute("INSERT INTO C (Id, X, Y) VALUES (1, 2, 1)");

        m_engine.Execute("DELETE FROM P WHERE A = 1 AND B = 2");

        Assert.That(Count("C"), Is.EqualTo(0),
            "the child references the deleted parent row, so it must cascade away");
    }

    #endregion

    #region Scaffolding queries SQLite catalogs the engine does not implement

    // CONFIRMED 2026-07-27. WitDatabaseModelFactory.GetTables issues, verbatim,
    //   SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
    // and the engine answers "Table 'sqlite_master' not found". PRAGMA does not even parse - it is
    // not a keyword in the grammar. So `dotnet ef dbcontext scaffold` fails on its first query;
    // database-first is not merely incomplete, it is inoperative.
    //
    // This finding is listed identically under cross-cutting and ef-runtime, so verifying it here
    // clears all three entries.

    [Test]
    [Ignore("CONFIRMED 2026-07-27: raises \"InvalidOperationException: Table 'sqlite_master' not " +
            "found\". Scaffolding's first query cannot execute. " +
            "engine-schema-ddl / cross-cutting / ef-runtime, " +
            "EntityFramework/Design/Internal/WitDatabaseModelFactory.cs:92")]
    public void SqliteMasterCatalogIsQueryableTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");

        Assert.That(
            () => m_engine.Query("SELECT name FROM sqlite_master WHERE type = 'table'"),
            Throws.Nothing,
            "database-first scaffolding cannot work unless this catalog exists");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: raises WitSqlParsingException - PRAGMA is not in the grammar's " +
            "statement set at all, so the column/PK/index metadata reads cannot execute either.")]
    public void TableInfoPragmaIsSupportedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");

        Assert.That(() => m_engine.Query("PRAGMA table_info('T')"), Throws.Nothing,
            "scaffolding reads column metadata through this PRAGMA");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A foreign key pointing at a UNIQUE column that is not the primary key, with values chosen so
    /// that a positional comparison against the primary key produces a visibly wrong answer:
    /// the child's FK value (100) collides with a different parent row's PK value (100).
    /// </summary>
    private void SeedNonPrimaryKeyReference()
    {
        m_engine.Execute("CREATE TABLE P (Id INT PRIMARY KEY, Code INT UNIQUE)");
        m_engine.Execute(@"
            CREATE TABLE C (
                Id INT PRIMARY KEY,
                PCode INT,
                FOREIGN KEY (PCode) REFERENCES P(Code) ON DELETE CASCADE)");

        m_engine.Execute("INSERT INTO P (Id, Code) VALUES (100, 1)");
        m_engine.Execute("INSERT INTO P (Id, Code) VALUES (2, 100)");
        m_engine.Execute("INSERT INTO C (Id, PCode) VALUES (1, 100)");
    }

    private int Count(string table) =>
        (int)m_engine.Query($"SELECT COUNT(*) FROM {table}")[0][0].AsInt64();

    #endregion
}
