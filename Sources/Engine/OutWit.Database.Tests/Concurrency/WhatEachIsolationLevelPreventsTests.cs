using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Concurrency;

/// <summary>
/// What the isolation levels actually prevent, measured through SQL rather than taken from the
/// standard's table.
/// </summary>
/// <remarks>
/// <para>
/// <b>These cases pass by construction and that is not the point of them.</b> Each pins one
/// OUTCOME with the reason written into its message, so that a change to the MVCC validation shows
/// up here - named - rather than as a surprise in an application. They were written on 2026-08-15
/// from four measurements taken while checking what the documentation claims, and two of the four
/// contradicted what was written down.
/// </para>
/// <para>
/// <b>The one to read is write skew.</b> Two transactions read the same rows, each writes a
/// different one, both commit, and an invariant that held for each separately is gone - at
/// <c>Serializable</c> as well as at <c>Snapshot</c>. It is permitted at every level this engine
/// offers, and an application whose correctness rests on an invariant ACROSS rows has to enforce it
/// itself.
/// </para>
/// <para>
/// Two engines over one <see cref="WitDatabase"/> is the shape a session takes here: the engine
/// holds the current transaction, so two of them are two sessions.
/// </para>
/// </remarks>
[TestFixture]
public class WhatEachIsolationLevelPreventsTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_isolation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Serializable

    /// <summary>
    /// A transaction that acts on a range it found EMPTY is refused when another transaction fills
    /// that range - the phantom the level exists to prevent.
    /// </summary>
    [TestCase("SERIALIZABLE")]
    [TestCase("REPEATABLE READ")]
    public void ActingOnAnEmptyRangeAnotherTransactionFilledIsRefusedTest(string level)
    {
        using var database = Database();
        using var setup = new WitSqlEngine(database);

        setup.Execute("CREATE TABLE R (Id BIGINT PRIMARY KEY, X BIGINT, Tag TEXT)");
        setup.Execute("INSERT INTO R (Id, X, Tag) VALUES (1, 5, 'base')");

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        Begin(first, level);
        Assert.That(Rows(first, "SELECT Id FROM R WHERE X BETWEEN 10 AND 20"), Is.Zero,
            "the range has to start empty, or this measures nothing");

        Begin(second, level);
        second.Execute("INSERT INTO R (Id, X, Tag) VALUES (2, 15, 'other')");
        second.Execute("COMMIT");

        // The first now writes on the strength of "that range was empty".
        first.Execute("INSERT INTO R (Id, X, Tag) VALUES (3, 15, 'mine-because-empty')");

        var error = Observe(() => first.Execute("COMMIT"));

        Assert.That(error, Is.Not.Null.And.Contain("serialization failure"),
            $"at {level} a transaction that acted on what it read must not commit after another "
            + "transaction changed it");
    }

    /// <summary>
    /// CONTROL, and it is not a defect: a transaction that only READ commits cleanly. There is
    /// nothing to serialise - a read-only transaction can always be ordered before the writer.
    /// </summary>
    [Test]
    public void AReadOnlyTransactionOverTheSameRangeCommitsTest()
    {
        using var database = Database();
        using var setup = new WitSqlEngine(database);

        setup.Execute("CREATE TABLE R (Id BIGINT PRIMARY KEY, X BIGINT)");
        setup.Execute("INSERT INTO R (Id, X) VALUES (1, 5)");

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        Begin(first, "SERIALIZABLE");
        Rows(first, "SELECT Id FROM R WHERE X BETWEEN 10 AND 20");

        Begin(second, "SERIALIZABLE");
        second.Execute("INSERT INTO R (Id, X) VALUES (2, 15)");
        second.Execute("COMMIT");

        Assert.Multiple(() =>
        {
            Assert.That(Rows(first, "SELECT Id FROM R WHERE X BETWEEN 10 AND 20"), Is.Zero,
                "the re-read comes from the snapshot, so the row that arrived is not seen");
            Assert.That(Observe(() => first.Execute("COMMIT")), Is.Null,
                "a transaction that only read has nothing to conflict with");
        });
    }

    /// <summary>
    /// Two transactions writing the SAME row: the second is refused. This is the conflict the read
    /// set can see, and the one applications are usually written around.
    /// </summary>
    [Test]
    public void TwoTransactionsWritingOneRowRefuseTheSecondTest()
    {
        using var database = Database();
        using var setup = new WitSqlEngine(database);

        setup.Execute("CREATE TABLE Doctors (Id BIGINT PRIMARY KEY, OnCall BIGINT)");
        setup.Execute("INSERT INTO Doctors (Id, OnCall) VALUES (1, 1)");

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        Begin(first, "SERIALIZABLE");
        Begin(second, "SERIALIZABLE");

        Rows(first, "SELECT OnCall FROM Doctors WHERE Id = 1");
        Rows(second, "SELECT OnCall FROM Doctors WHERE Id = 1");

        first.Execute("UPDATE Doctors SET OnCall = 7 WHERE Id = 1");
        second.Execute("UPDATE Doctors SET OnCall = 9 WHERE Id = 1");

        Assert.Multiple(() =>
        {
            Assert.That(Observe(() => first.Execute("COMMIT")), Is.Null, "the first one wins");
            Assert.That(Observe(() => second.Execute("COMMIT")),
                Is.Not.Null.And.Contain("serialization failure"),
                "and the second is refused rather than overwriting it");
        });
    }

    #endregion

    #region Write skew - permitted at every level here

    /// <summary>
    /// <b>Write skew, and it commits.</b> Two transactions read the same two rows, each takes a
    /// DIFFERENT one off call, both commit, and the ward ends with nobody on call - an invariant
    /// that held for each transaction separately.
    /// </summary>
    /// <remarks>
    /// PINS BEHAVIOUR THAT IS PERMITTED, NOT A DEFECT TO FIX SILENTLY. Preventing it needs predicate
    /// locking or serializable snapshot isolation, which this engine does not have; what it needs
    /// meanwhile is for the behaviour to be written down and asserted, so that an application can be
    /// built around it. If a future change starts refusing this, these cases go red and the decision
    /// is visible.
    /// </remarks>
    [TestCase("SERIALIZABLE")]
    [TestCase("SNAPSHOT")]
    public void WriteSkewIsPermittedTest(string level)
    {
        using var database = Database();
        using var setup = new WitSqlEngine(database);

        setup.Execute("CREATE TABLE Doctors (Id BIGINT PRIMARY KEY, OnCall BIGINT)");
        setup.Execute("INSERT INTO Doctors (Id, OnCall) VALUES (1, 1)");
        setup.Execute("INSERT INTO Doctors (Id, OnCall) VALUES (2, 1)");

        using var first = new WitSqlEngine(database);
        using var second = new WitSqlEngine(database);

        Begin(first, level);
        Begin(second, level);

        // Each checks the invariant - "somebody else is on call" - and both see two.
        Assert.That(Rows(first, "SELECT Id FROM Doctors WHERE OnCall = 1"), Is.EqualTo(2));
        Assert.That(Rows(second, "SELECT Id FROM Doctors WHERE OnCall = 1"), Is.EqualTo(2));

        first.Execute("UPDATE Doctors SET OnCall = 0 WHERE Id = 1");
        second.Execute("UPDATE Doctors SET OnCall = 0 WHERE Id = 2");

        Assert.Multiple(() =>
        {
            Assert.That(Observe(() => first.Execute("COMMIT")), Is.Null);
            Assert.That(Observe(() => second.Execute("COMMIT")), Is.Null,
                $"at {level} the two wrote different rows, so nothing conflicts");
        });

        using var after = new WitSqlEngine(database);

        Assert.That(Rows(after, "SELECT Id FROM Doctors WHERE OnCall = 1"), Is.Zero,
            "and the invariant both transactions checked is gone - this is write skew, and it is "
            + "permitted at every level this engine offers");
    }

    #endregion

    #region Tools

    private WitDatabase Database() =>
        new WitDatabaseBuilder()
            .WithFilePath(Path.Combine(m_testDir, "isolation.witdb"))
            .WithBTree()
            .WithTransactions()
            .WithMvcc()
            .Build();

    /// <summary>
    /// The level is set BEFORE the transaction begins, which is the order the engine documents:
    /// SET TRANSACTION records a level for the next transaction and BEGIN consumes it.
    /// </summary>
    private static void Begin(WitSqlEngine engine, string level)
    {
        engine.Execute($"SET TRANSACTION ISOLATION LEVEL {level}");
        engine.Execute("BEGIN TRANSACTION");
    }

    /// <summary>Counts rows by reading them - never through COUNT(*), which is separate state.</summary>
    private static int Rows(WitSqlEngine engine, string sql)
    {
        using var result = engine.Execute(sql);
        return result.ReadAll().Count;
    }

    /// <summary>The message of whatever the statement threw, or null when it did not.</summary>
    private static string? Observe(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    #endregion
}
