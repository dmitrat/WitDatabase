using OutWit.Database.AdoNet.Maintenance;

namespace OutWit.Database.AdoNet.Tests.Maintenance;

/// <summary>
/// WS-57: maintenance and reporting through the surface a consumer actually holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three things the design asked the engine for</b> - storage maintenance, a read-only
/// statistics snapshot, and whether the file is in use - and every one of them existed in the engine
/// already. What did not exist was a way to reach them: a database hands out its outermost wrapper,
/// and <c>Compact</c> lived four layers down.
/// </para>
/// <para>
/// <b>Every case here asserts what CHANGED, not that a call returned.</b> The measurement that
/// started this work is the reason: <c>StoreLsm.Compact()</c> was a <c>void</c> that declined
/// silently whenever the store sat below its automatic trigger, which was almost always. A test that
/// called it and checked for no exception would have passed against that.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StorageMaintenanceTests
{
    #region Setup

    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"ws57_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_root, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region The report

    [Test]
    public void TheSnapshotNamesTheStoreAndTheLayersAboveItTest()
    {
        using var connection = OpenLsm();

        var snapshot = connection.GetStorageSnapshot();

        TestContext.Out.WriteLine(
            $"chain: {string.Join(" -> ", snapshot.Chain)}, store={snapshot.StoreProviderKey}, " +
            $"size={snapshot.ApproximateSizeInBytes}");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.StoreProviderKey, Is.EqualTo("lsm"));

            // CONTROL: a chain of one would mean the walk found nothing and reported the outermost
            // store as the storing one - which is exactly the state this work started from.
            Assert.That(snapshot.Chain, Has.Count.GreaterThan(1),
                "CONTROL: the chain has one link, so nothing was reached through anything");

            Assert.That(snapshot.Chain[0], Is.Not.EqualTo("lsm"),
                "CONTROL: the outermost layer IS the store, so this says nothing about reaching down");

            Assert.That(snapshot.ApproximateSizeInBytes, Is.Not.Null,
                "no layer answered for the size, so a report has nothing to show");

            Assert.That(snapshot.Lsm, Is.Not.Null);
        });
    }

    [Test]
    public void ABTreeHasNoLsmHalfTest()
    {
        using var connection = OpenBTree();

        var snapshot = connection.GetStorageSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.StoreProviderKey, Is.EqualTo("btree"));

            Assert.That(snapshot.Lsm, Is.Null,
                "a B+Tree reported LSM facts, so the panel would draw memtables and SSTables that do "
                + "not exist");
        });
    }

    /// <summary>
    /// The counters are this connection's, and the snapshot has to be able to show it.
    /// </summary>
    [Test]
    public void TheCountersMoveWithThisConnectionsWorkTest()
    {
        using var connection = OpenLsm();

        var before = connection.GetStorageSnapshot().Lsm!.CountersSinceOpened.Puts;

        Write(connection, rows: 50);

        var after = connection.GetStorageSnapshot().Lsm!.CountersSinceOpened.Puts;

        Assert.That(after, Is.GreaterThan(before),
            $"the store's put counter did not move for 50 inserts ({before} -> {after}), so the "
            + "snapshot is reading counters that are not connected to the work");
    }

    #endregion

    #region Maintenance

    [Test]
    public void ACheckpointTurnsTheMemTableIntoAFileTest()
    {
        using var connection = OpenLsm();

        Write(connection, rows: 200);

        // CONTROL: nothing has asked for a checkpoint, so there is nothing on disk to merge yet.
        Assert.That(connection.GetStorageSnapshot().Lsm!.SstableCount, Is.Zero,
            "CONTROL: an SSTable exists before the checkpoint, so the one below cannot be attributed "
            + "to it");

        var result = connection.Checkpoint();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.Completed));
            Assert.That(result.SstablesBefore, Is.Zero);
            Assert.That(result.SstablesAfter, Is.EqualTo(1));
        });
    }

    [Test]
    public void ACheckpointWithAnEmptyMemTableSaysNothingToDoTest()
    {
        using var connection = OpenLsm();

        Write(connection, rows: 10);
        connection.Checkpoint();

        var second = connection.Checkpoint();

        Assert.That(second.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.NothingToDo),
            "a checkpoint with nothing left in the memtable reported that it had done something");
    }

    [Test]
    public void CompactMergesTheFilesAndSaysSoTest()
    {
        using var connection = OpenLsm();

        // Four checkpoints, four SSTables - and the connection string raises the automatic trigger
        // above them, so nothing merges by itself and the call below is what is being measured.
        for (var i = 0; i < 4; i++)
        {
            Write(connection, rows: 20, offset: i * 20);
            connection.Checkpoint();
        }

        var before = connection.GetStorageSnapshot().Lsm!.SstableCount;

        var result = connection.Compact();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(4),
                "CONTROL: the automatic trigger merged the files before the explicit call, so this "
                + "case is not measuring Compact()");

            Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.Completed));
            Assert.That(result.SstablesBefore, Is.EqualTo(4));
            Assert.That(result.SstablesAfter, Is.EqualTo(1));

            Assert.That(connection.GetStorageSnapshot().Lsm!.SstableCount, Is.EqualTo(1),
                "the result claimed a merge the storage does not show");

            Assert.That(Rows(connection), Is.EqualTo(80), "the compaction lost rows");
        });
    }

    [Test]
    public void CompactOnASingleFileSaysNothingToDoTest()
    {
        using var connection = OpenLsm();

        Write(connection, rows: 20);
        connection.Checkpoint();

        var result = connection.Compact();

        Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.NothingToDo),
            "one SSTable was 'merged', which means the result is reporting the call rather than "
            + "what happened");
    }

    [Test]
    public void ABTreeCannotBeCompactedAndSaysSoTest()
    {
        using var connection = OpenBTree();

        Write(connection, rows: 20);

        var result = connection.Compact();

        Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.NotSupported),
            "a B+Tree reported a compaction outcome other than 'this store has no such operation', so "
            + "an interface cannot tell an absent capability from an idle one");
    }

    #endregion

    #region Is it in use

    [Test]
    public void AnOpenDatabaseIsReportedInUseTest()
    {
        var path = Path.Combine(m_root, "busy.witdb");

        Assert.That(WitDbConnection.IsDatabaseInUse(path), Is.False,
            "CONTROL: a database that has never been opened is reported busy, so the probe answers "
            + "'busy' to everything");

        using (var connection = OpenBTree(path))
        {
            Write(connection, rows: 5);

            Assert.That(WitDbConnection.IsDatabaseInUse(path), Is.True,
                "a database held open by this very process is reported free");
        }

        Assert.That(WitDbConnection.IsDatabaseInUse(path), Is.False,
            "the database is still reported busy after the connection closed, so the probe is taking "
            + "a lock it does not give back");
    }

    #endregion

    #region Tools

    /// <summary>
    /// The automatic trigger is raised out of the way, so every merge in this fixture is one the test
    /// asked for.
    /// </summary>
    private WitDbConnection OpenLsm()
    {
        var path = Path.Combine(m_root, "lsm");

        var connection = new WitDbConnection($"Data Source={path};Store=lsm;CompactionTrigger=100");
        connection.Open();

        return connection;
    }

    private WitDbConnection OpenBTree(string? path = null)
    {
        var connection = new WitDbConnection(
            $"Data Source={path ?? Path.Combine(m_root, "btree.witdb")}");

        connection.Open();

        return connection;
    }

    private static void Write(WitDbConnection connection, int rows, int offset = 0)
    {
        if (offset == 0)
            Execute(connection, "CREATE TABLE IF NOT EXISTS T (Id BIGINT PRIMARY KEY, V INT)");

        for (var i = 0; i < rows; i++)
            Execute(connection, $"INSERT INTO T (Id, V) VALUES ({offset + i}, {i})");
    }

    private static int Rows(WitDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM T";

        using var reader = command.ExecuteReader();

        var rows = 0;
        while (reader.Read())
            rows++;

        return rows;
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    #endregion
}
