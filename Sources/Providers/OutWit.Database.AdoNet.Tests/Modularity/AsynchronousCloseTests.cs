using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 follow-up - the asynchronous close must save what the synchronous one saves.
/// </summary>
/// <remarks>
/// <para>
/// The chain built for a storage with no synchronous operations - page cache, page manager, B+Tree
/// store, concurrency wrapper, MVCC store, transactional store, engine - is a second way to close a
/// database, and a second way to close a database is a second way to lose one. A close that quietly
/// skipped its flush would satisfy every "does not throw" assertion in the fixture that motivated it.
/// </para>
/// <para>
/// So this asks the only question that matters about it: <b>reopen the database and count the rows</b>.
/// The synchronous close is the control - it runs the identical workload, so a failure here is about
/// the asynchronous path rather than about the test - and the rows are read back by scanning, never by
/// <c>COUNT(*)</c>, which on this engine is a cached counter that has disagreed with the rows before.
/// </para>
/// <para>
/// <b>What this fixture can and cannot see, measured rather than assumed.</b> Its power was checked by
/// removing the flush from the asynchronous close, one link at a time and then all at once - the page
/// manager's, the engine's, the page cache's and the MVCC store's - and it stayed <b>green every
/// time</b>, under all four models including <c>Synchronous Commit=false</c>. The reason is that the
/// data is already on the media before anything is closed: each statement runs in an implicit
/// transaction, and the close path itself flushes in five places. So this fixture verifies that the new
/// close path does not <b>lose or corrupt</b> what was written - which is the risk a second way to close
/// a database introduces - and it is not a test of the flush. Saying so is the point: a green test
/// nobody has tried to break is a claim, not evidence.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class AsynchronousCloseTests
{
    #region Constants

    private const string EXPECTED = "1:row1|2:row2|3:row3|4:row4|5:row5|6:row6|7:row7|8:row8";

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_asyncclose_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The configurations

    public sealed record Model(string Label, Action<WitDatabaseBuilder> Configure)
    {
        public override string ToString() => Label;
    }

    private static IEnumerable<Model> Models()
    {
        yield return new Model("locks", b => b.WithBTree().WithTransactions());
        yield return new Model("mvcc", b => b.WithBTree().WithMvcc());
        yield return new Model("lru cache", b => b.WithBTree().WithTransactions().WithCacheKey("lru"));

        // Added while measuring this fixture's power, on the assumption that an asynchronous commit
        // would leave the rows unflushed until the close and so make the close load-bearing. It does
        // not: with the flush removed from all four places on the close path, this model kept its rows
        // like the others. Kept because it is a configuration worth covering, and recorded because the
        // assumption behind it was wrong - Synchronous Commit=false defers durability against a process
        // kill, which instrument E measures, and not the write itself.
        yield return new Model("mvcc, async commit", b => b.WithBTree().WithMvcc().WithAsynchronousCommit());
    }

    #endregion

    #region The probe

    [Test]
    [TestCaseSource(nameof(Models))]
    public async Task AnAsynchronousCloseKeepsEveryRowTest(Model model)
    {
        var path = NewPath();

        var builder = new WitDatabaseBuilder().WithFilePath(path);
        model.Configure(builder);

        var database = builder.Build();
        var engine = new WitSqlEngine(database, ownsStore: true);

        Write(engine);

        await engine.DisposeAsync();

        Assert.That(Reopen(path, model), Is.EqualTo(EXPECTED),
            $"{model.Label}: rows written before an asynchronous close did not come back. A close that " +
            "does not throw and does not save is worse than one that throws.");
    }

    /// <summary>
    /// Control: the synchronous close, same workload. If this fails, the workload or the configuration
    /// is at fault and the verdict above means nothing.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Models))]
    public void ControlASynchronousCloseKeepsEveryRowTest(Model model)
    {
        var path = NewPath();

        var builder = new WitDatabaseBuilder().WithFilePath(path);
        model.Configure(builder);

        using (var engine = new WitSqlEngine(builder.Build(), ownsStore: true))
        {
            Write(engine);
        }

        Assert.That(Reopen(path, model), Is.EqualTo(EXPECTED),
            $"{model.Label}: the synchronous close lost rows - the asynchronous verdict cannot be believed");
    }

    /// <summary>
    /// And the database's own asynchronous close, one layer below the engine, because that is the layer
    /// <c>WitDatabase.DisposeAsync</c> exposes to a caller who built a database without an engine.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Models))]
    public async Task TheDatabasesOwnAsynchronousCloseKeepsEveryRowTest(Model model)
    {
        var path = NewPath();

        var builder = new WitDatabaseBuilder().WithFilePath(path);
        model.Configure(builder);

        var database = builder.Build();
        var engine = new WitSqlEngine(database, ownsStore: false);

        Write(engine);

        engine.Dispose();
        await database.DisposeAsync();

        Assert.That(Reopen(path, model), Is.EqualTo(EXPECTED),
            $"{model.Label}: rows written before the database's asynchronous close did not come back");
    }

    #endregion

    #region Tools

    private static void Write(WitSqlEngine engine)
    {
        engine.Execute("CREATE TABLE Closed (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");

        for (var i = 1; i <= 8; i++)
            engine.Execute($"INSERT INTO Closed (Id, Name) VALUES ({i}, 'row{i}')");
    }

    private static string Reopen(string path, Model model)
    {
        var builder = new WitDatabaseBuilder().WithFilePath(path);
        model.Configure(builder);

        using var engine = new WitSqlEngine(builder.Build(), ownsStore: true);

        var rows = engine.Query("SELECT Id, Name FROM Closed ORDER BY Id");

        return string.Join("|", rows.Select(r => $"{r["Id"].AsInt64()}:{r["Name"].AsString()}"));
    }

    private string NewPath()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "close.witdb");
    }

    #endregion
}
