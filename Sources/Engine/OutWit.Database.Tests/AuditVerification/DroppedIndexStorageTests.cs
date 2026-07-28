using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Dropping an index left its entries in storage, so an index later created under the same name
/// adopted them. On a persistent store that made a recreated table reject rows it did not contain.
///
/// Found by EF Core's specification suite, not by the audit: the shared-store fixtures delete and
/// recreate their database between fixtures, and every one of them failed on the first seeded row.
/// The tests are file-backed on purpose - an in-memory database builds a fresh store per index, so
/// it cannot show this at all.
/// </summary>
[TestFixture]
public sealed class DroppedIndexStorageTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_dropped_index_{Guid.NewGuid():N}");

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

    #region Tests

    [Test]
    public void RecreatedTableAcceptsTheKeyTheDroppedTableHeldTest()
    {
        m_engine.Execute("CREATE TABLE T (A INT NOT NULL PRIMARY KEY)");
        m_engine.Execute("INSERT INTO T (A) VALUES (1)");

        m_engine.Execute("DROP TABLE T");
        m_engine.Execute("CREATE TABLE T (A INT NOT NULL PRIMARY KEY)");

        Assert.That(CountOf("T"), Is.EqualTo(0), "the recreated table must start empty");

        Assert.DoesNotThrow(
            () => m_engine.Execute("INSERT INTO T (A) VALUES (1)"),
            "the recreated table holds no rows, so key 1 is free - it was rejected because the "
            + "dropped table's primary key index kept its entries in storage");

        Assert.That(CountOf("T"), Is.EqualTo(1));
    }

    [Test]
    public void RecreatedTableWithCompositeKeyAcceptsTheKeyTheDroppedTableHeldTest()
    {
        m_engine.Execute("CREATE TABLE T (A INT NOT NULL, B TEXT NOT NULL, PRIMARY KEY (A, B))");
        m_engine.Execute("INSERT INTO T (A, B) VALUES (1, 'x')");

        m_engine.Execute("DROP TABLE T");
        m_engine.Execute("CREATE TABLE T (A INT NOT NULL, B TEXT NOT NULL, PRIMARY KEY (A, B))");

        Assert.DoesNotThrow(
            () => m_engine.Execute("INSERT INTO T (A, B) VALUES (1, 'x')"),
            "a composite primary key is affected the same way as a single-column one");
    }

    [Test]
    public void RecreatedUniqueIndexDoesNotAdoptTheDroppedIndexEntriesTest()
    {
        m_engine.Execute("CREATE TABLE U (A INT NOT NULL)");
        m_engine.Execute("CREATE UNIQUE INDEX IX_U ON U (A)");
        m_engine.Execute("INSERT INTO U (A) VALUES (1)");

        m_engine.Execute("DROP INDEX IX_U");
        m_engine.Execute("DELETE FROM U");
        m_engine.Execute("CREATE UNIQUE INDEX IX_U ON U (A)");

        Assert.That(CountOf("U"), Is.EqualTo(0), "the table was emptied");

        Assert.DoesNotThrow(
            () => m_engine.Execute("INSERT INTO U (A) VALUES (1)"),
            "an explicitly declared unique index is affected the same way as an implicit one");
    }

    /// <summary>
    /// The dropped index must not go on answering lookups either. Uniqueness is the loud symptom;
    /// a non-unique index that keeps its entries makes an indexed read return rows that were
    /// deleted, which is silent.
    /// </summary>
    [Test]
    public void RecreatedNonUniqueIndexDoesNotServeTheDroppedIndexEntriesTest()
    {
        m_engine.Execute("CREATE TABLE N (A INT NOT NULL, B INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_N ON N (A)");
        m_engine.Execute("INSERT INTO N (A, B) VALUES (7, 70)");

        m_engine.Execute("DROP INDEX IX_N");
        m_engine.Execute("DELETE FROM N");
        m_engine.Execute("CREATE INDEX IX_N ON N (A)");

        var rows = m_engine.Query("SELECT B FROM N WHERE A = 7");

        Assert.That(rows, Is.Empty,
            "every row was deleted, so an indexed read must return nothing");
    }

    #endregion

    #region Helpers

    private long CountOf(string tableName)
    {
        var rows = m_engine.Query($"SELECT COUNT(*) AS N FROM {tableName}");

        return rows[0]["N"].AsInt64();
    }

    #endregion
}
