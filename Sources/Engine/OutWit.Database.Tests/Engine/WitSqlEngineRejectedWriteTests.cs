using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests: a rejected INSERT or UPDATE must leave nothing behind.
/// </summary>
/// <remarks>
/// The row was written first and the secondary indexes updated afterwards, so a unique-index
/// violation was raised with the row already in the store. Nothing removed it. The damage was
/// invisible to any test that checks <c>COUNT(*)</c> - that reads the catalog's row counter, which
/// the failed insert never incremented - and only showed up when the rows were actually read, most
/// obviously after reopening the file.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineRejectedWriteTests : WitSqlEngineTestsBase
{
    #region Rejected Insert

    [Test]
    public void RejectedInsertDoesNotLeaveTheRowBehindTest()
    {
        CreateTableWithUniqueIndex();
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 7)");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 7)"));

        Assert.Multiple(() =>
        {
            Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id"), Is.EqualTo(new long[] { 1 }),
                "The rejected row must not be readable");
            Assert.That(Scalar("SELECT COUNT(*) FROM T").AsInt64(), Is.EqualTo(1));
        });
    }

    [Test]
    public void RejectedInsertDoesNotSurviveAReopenTest()
    {
        var database = WitDatabase.CreateInMemory();

        using (var engine = new WitSqlEngine(database, ownsStore: false))
        {
            engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)");
            engine.Execute("CREATE UNIQUE INDEX UX_T_V ON T (V)");
            engine.Execute("INSERT INTO T (Id, V) VALUES (1, 7)");

            try { engine.Execute("INSERT INTO T (Id, V) VALUES (2, 7)"); }
            catch (InvalidOperationException) { }
        }

        using (var reopened = new WitSqlEngine(database, ownsStore: true))
        {
            var ids = reopened.Query("SELECT Id FROM T ORDER BY Id")
                .Select(row => row[0].AsInt64()).ToArray();

            Assert.That(ids, Is.EqualTo(new long[] { 1 }),
                "The rejected row used to appear only after a reopen, because COUNT(*) hid it");
        }
    }

    [Test]
    public void RejectedInsertLeavesNoIndexEntryTest()
    {
        CreateTableWithUniqueIndex();
        m_engine.Execute("CREATE INDEX IX_T_W ON T (W)");
        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (1, 7, 100)");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (2, 7, 200)"));

        // W=200 only ever existed on the rejected row, so the non-unique index must not resolve it.
        Assert.That(SelectIds("SELECT Id FROM T WHERE W = 200"), Is.Empty);
    }

    [Test]
    public void TheOriginalRowIsUntouchedByARejectedInsertTest()
    {
        CreateTableWithUniqueIndex();
        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (1, 7, 100)");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (2, 7, 200)"));

        Assert.Multiple(() =>
        {
            Assert.That(SelectIds("SELECT Id FROM T WHERE V = 7"), Is.EqualTo(new long[] { 1 }),
                "Compensation must not remove the conflicting row's own index entry");
            Assert.That(Scalar("SELECT W FROM T WHERE Id = 1").AsInt64(), Is.EqualTo(100));
        });
    }

    [Test]
    public void InsertAfterARejectedOneStillSucceedsTest()
    {
        CreateTableWithUniqueIndex();
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 7)");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 7)"));

        m_engine.Execute("INSERT INTO T (Id, V) VALUES (3, 8)");

        Assert.That(SelectIds("SELECT Id FROM T ORDER BY Id"), Is.EqualTo(new long[] { 1, 3 }));
    }

    #endregion

    #region Rejected Update

    [Test]
    public void RejectedUpdateRestoresThePreviousValuesTest()
    {
        CreateTableWithUniqueIndex();
        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (1, 7, 100)");
        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (2, 8, 200)");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("UPDATE T SET V = 7 WHERE Id = 2"));

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT V FROM T WHERE Id = 2").AsInt64(), Is.EqualTo(8),
                "A rejected UPDATE must not leave the new value in the store");
            Assert.That(Scalar("SELECT W FROM T WHERE Id = 2").AsInt64(), Is.EqualTo(200));
        });
    }

    #endregion

    #region Helper Methods

    private void CreateTableWithUniqueIndex()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL, W INT NULL)");
        m_engine.Execute("CREATE UNIQUE INDEX UX_T_V ON T (V)");
    }

    private long[] SelectIds(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();
    }

    private WitSqlValue Scalar(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0]).First();
    }

    #endregion
}
