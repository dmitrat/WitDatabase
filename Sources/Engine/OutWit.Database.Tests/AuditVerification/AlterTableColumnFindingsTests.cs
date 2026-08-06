using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Two ways ALTER TABLE left a database in a state nothing could fix, both found on 2026-08-06 while
/// building Studio's schema designer.
///
/// <b>ADD COLUMN … NOT NULL with no DEFAULT was accepted on a table that already had rows.</b> Every
/// existing row got NULL in a column declared NOT NULL, and from then on the engine refused every
/// write to that table - including an UPDATE of an unrelated column - because the row it was asked to
/// write violated the constraint. Giving the column a default afterwards repairs new rows and leaves
/// the NULLs; there is no way back short of rebuilding the table.
///
/// <b>DROP COLUMN left the index on that column behind</b>, in the catalogue, naming a column that no
/// longer exists, and it survived a reopen. The foreign key on the column did go with it.
/// </summary>
[TestFixture]
public sealed class AlterTableColumnFindingsTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_alter_column_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;

        if (!Directory.Exists(m_databasePath))
            return;

        try
        {
            Directory.Delete(m_databasePath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only - a locked file must not fail the run.
        }
    }

    #endregion

    #region NOT NULL with no default

    [Test]
    public void ANotNullColumnWithNoDefaultIsRefusedOnATableWithRowsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, A) VALUES (1, 'one')");

        var error = Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("ALTER TABLE T ADD COLUMN B INT NOT NULL"),
            "the rows cannot satisfy it, and accepting it closes the table for writing");

        Assert.That(error!.Message, Does.Contain("DEFAULT"), "and the message says what would work");
    }

    [Test]
    public void TheTableIsUntouchedByTheRefusalTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, A) VALUES (1, 'one')");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("ALTER TABLE T ADD COLUMN B INT NOT NULL"));

        Assert.DoesNotThrow(
            () => m_engine.Execute("UPDATE T SET A = 'still writable' WHERE Id = 1"),
            "a refused ALTER must leave the table exactly as it was - the defect this replaces made "
            + "every later write fail");

        var columns = m_engine.Query("SELECT * FROM T")[0].ColumnNames;

        Assert.That(columns, Does.Not.Contain("B"), "and the column was not added");
    }

    [Test]
    public void ANotNullColumnWithADefaultIsAcceptedOnATableWithRowsTest()
    {
        // CONTROL: it is the missing DEFAULT that is refused, not NOT NULL.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, A) VALUES (1, 'one')");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ADD COLUMN B INT NOT NULL DEFAULT 0"));

        Assert.That(m_engine.Query("SELECT B FROM T")[0]["B"].AsInt64(), Is.EqualTo(0),
            "and the existing row got the default rather than NULL");
    }

    [Test]
    public void ANotNullColumnWithNoDefaultIsAcceptedOnAnEmptyTableTest()
    {
        // CONTROL: with no rows there is nothing that could violate it, and refusing here would be a
        // rule the engine invented.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A TEXT NOT NULL)");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ADD COLUMN B INT NOT NULL"));

        Assert.DoesNotThrow(() => m_engine.Execute("INSERT INTO T (Id, A, B) VALUES (1, 'one', 5)"));
    }

    [Test]
    public void ANullableColumnIsAcceptedOnATableWithRowsTest()
    {
        // CONTROL: the ordinary case must keep working.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, A) VALUES (1, 'one')");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ADD COLUMN B INT"));

        Assert.That(m_engine.Query("SELECT B FROM T")[0]["B"].IsNull, Is.True);
    }

    #endregion

    #region The index over a dropped column

    [Test]
    public void DroppingAColumnDropsTheIndexOnItTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_A ON T (A)");
        m_engine.Execute("INSERT INTO T (Id, A, B) VALUES (1, 10, 100)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");

        var indexes = m_engine.Query(
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'T'")
            .Select(row => row["INDEX_NAME"].AsString())
            .ToArray();

        Assert.That(indexes, Does.Not.Contain("IX_T_A"),
            "an index over a column that no longer exists is a catalogue entry nothing can use, and it "
            + "used to survive a reopen");
    }

    [Test]
    public void TheIndexOnAnotherColumnSurvivesTest()
    {
        // CONTROL: only the indexes that name the dropped column go.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_A ON T (A)");
        m_engine.Execute("CREATE INDEX IX_T_B ON T (B)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");

        var indexes = m_engine.Query(
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'T'")
            .Select(row => row["INDEX_NAME"].AsString())
            .ToArray();

        Assert.That(indexes, Does.Contain("IX_T_B"));
        Assert.That(indexes, Does.Not.Contain("IX_T_A"));
    }

    [Test]
    public void ACompositeIndexThatMentionsTheDroppedColumnGoesTooTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_AB ON T (B, A)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");

        var indexes = m_engine.Query(
            "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'T'")
            .Select(row => row["INDEX_NAME"].AsString())
            .ToArray();

        Assert.That(indexes, Does.Not.Contain("IX_T_AB"),
            "a composite index cannot stand on a column that is gone either");
    }

    [Test]
    public void TheTableStillWorksAfterTheDropTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, A INT NOT NULL, B INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_A ON T (A)");
        m_engine.Execute("INSERT INTO T (Id, A, B) VALUES (1, 10, 100)");

        m_engine.Execute("ALTER TABLE T DROP COLUMN A");
        m_engine.Execute("INSERT INTO T (Id, B) VALUES (2, 200)");

        var rows = m_engine.Query("SELECT Id, B FROM T")
            .Select(row => $"{row["Id"].AsInt64()}|{row["B"].AsInt64()}")
            .OrderBy(text => text)
            .ToArray();

        Assert.That(rows, Is.EqualTo(new[] { "1|100", "2|200" }));
    }

    #endregion
}
