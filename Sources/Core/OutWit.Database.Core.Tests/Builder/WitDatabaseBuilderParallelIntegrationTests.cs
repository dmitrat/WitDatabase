using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Tests.Builder;

/// <summary>
/// Tests for WitDatabaseBuilder parallel mode integration.
/// </summary>
[TestFixture]
public class WitDatabaseBuilderParallelIntegrationTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"builder_parallel_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    #endregion

    #region BTree Parallel Mode Tests

    [Test]
    public void BuildBTreeWithParallelModeAutoTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_auto.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Auto)
            .WithoutTransactions()
            .Build();

        Assert.That(db, Is.Not.Null);
        
        // Verify it's a concurrent store
        var store = db.Store;
        Assert.That(store, Is.TypeOf<BTreeConcurrentStore>());
    }

    [Test]
    public void BuildBTreeWithParallelModeLatchedTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_latched.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Latched)
            .WithoutTransactions()
            .Build();

        var store = db.Store;
        Assert.That(store, Is.TypeOf<BTreeConcurrentStore>());
    }

    [Test]
    public void BuildBTreeWithParallelOptionsTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_options.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(new ParallelModeOptions
            {
                Mode = ParallelMode.Latched,
                TrackStatistics = true,
                LatchTimeout = TimeSpan.FromSeconds(10)
            })
            .WithoutTransactions()
            .Build();

        var store = (BTreeConcurrentStore)db.Store;
        Assert.That(store.Options.TrackStatistics, Is.True);
    }

    [Test]
    public void BuildBTreeWithMaxWritersTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_maxwriters.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Latched)
            .WithMaxWriters(8)
            .WithoutTransactions()
            .Build();

        Assert.That(db.Store, Is.TypeOf<BTreeConcurrentStore>());
    }

    [Test]
    public void BuildBTreeWithoutParallelWritesTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_no_parallel.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithoutParallelWrites()
            .WithoutTransactions()
            .Build();

        // INVERTED BY A FIX, 11.3.0, and the inversion is the point: this used to assert a bare
        // StoreBTree and it was pinning an exposure. StoreBTree has no locking of any kind, and this
        // configuration - no parallel mode AND no transaction layer - put nothing at all between two
        // writers and one leaf split. Measured in MainStoreConcurrencyProbeTests: a writer threw and an
        // entry was lost in five runs out of five. Serialising the B+Tree store is not a mode now, and
        // it costs a single thread nothing (median ratio 1.001 over five interleaved passes).
        var store = db.Store;
        Assert.That(store, Is.TypeOf<BTreeConcurrentStore>());
    }

    #endregion

    #region LSM Parallel Mode Tests

    [Test]
    public void BuildLsmWithParallelModeAutoTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_auto");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithParallelWrites(ParallelMode.Auto)
            .WithoutTransactions()
            .Build();

        Assert.That(db.Store, Is.TypeOf<LsmParallelStore>());
    }

    [Test]
    public void BuildLsmWithParallelModeBufferedTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_buffered");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithParallelWrites(ParallelMode.Buffered)
            .WithoutTransactions()
            .Build();

        Assert.That(db.Store, Is.TypeOf<LsmParallelStore>());
    }

    [Test]
    public void BuildLsmWithParallelOptionsTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_options");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithParallelWrites(opts =>
            {
                opts.Mode = ParallelMode.Buffered;
                opts.MaxWriters = 4;
                opts.BufferSizeThreshold = 32 * 1024;
                opts.TrackStatistics = true;
            })
            .WithoutTransactions()
            .Build();

        var store = (LsmParallelStore)db.Store;
        Assert.That(store.Options.MaxWriters, Is.EqualTo(4));
        Assert.That(store.Options.BufferSizeThreshold, Is.EqualTo(32 * 1024));
    }

    [Test]
    public void BuildLsmWithoutParallelWritesTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_no_parallel");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithoutParallelWrites()
            .WithoutTransactions()
            .Build();

        Assert.That(db.Store, Is.TypeOf<StoreLsm>());
    }

    #endregion

    #region Functional Tests

    [Test]
    public void ParallelBTreeStoreWorksCorrectlyTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_functional.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Latched)
            .WithoutTransactions()
            .Build();

        // Test basic operations
        db.Store.Put("key1"u8, "value1"u8);
        db.Store.Put("key2"u8, "value2"u8);

        var value1 = db.Store.Get("key1"u8);
        var value2 = db.Store.Get("key2"u8);

        Assert.That(value1, Is.Not.Null);
        Assert.That(value2, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(value1!), Is.EqualTo("value1"));
    }

    [Test]
    public void ParallelLsmStoreWorksCorrectlyTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_functional");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithParallelWrites(ParallelMode.Buffered)
            .WithoutTransactions()
            .Build();

        // Test basic operations
        db.Store.Put("key1"u8, "value1"u8);
        db.Store.Put("key2"u8, "value2"u8);
        db.Store.Flush();

        var value1 = db.Store.Get("key1"u8);
        var value2 = db.Store.Get("key2"u8);

        Assert.That(value1, Is.Not.Null);
        Assert.That(value2, Is.Not.Null);
    }

    [Test]
    public void ParallelBTreeConcurrentWritesTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_concurrent.witdb");

        using var db = new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Latched)
            .WithoutTransactions()
            .Build();

        const int threads = 4;
        const int entriesPerThread = 100;
        var errors = new List<Exception>();

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    var key = System.Text.Encoding.UTF8.GetBytes($"t{threadId}_key_{i:D5}");
                    var value = System.Text.Encoding.UTF8.GetBytes($"value_{i}");
                    db.Store.Put(key, value);
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ParallelLsmConcurrentWritesTest()
    {
        var dir = Path.Combine(m_testDir, "lsm_concurrent");

        using var db = new WitDatabaseBuilder()
            .WithLsmTree(dir)
            .WithParallelWrites(ParallelMode.Buffered)
            .WithoutTransactions()
            .Build();

        const int threads = 4;
        const int entriesPerThread = 100;
        var errors = new List<Exception>();

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    var key = System.Text.Encoding.UTF8.GetBytes($"t{threadId}_key_{i:D5}");
                    var value = System.Text.Encoding.UTF8.GetBytes($"value_{i}");
                    db.Store.Put(key, value);
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        db.Store.Flush();

        Assert.That(errors, Is.Empty);
    }

    #endregion

    #region Async Build Tests

    [Test]
    public async Task BuildAsyncWithParallelModeTest()
    {
        var filePath = Path.Combine(m_testDir, "btree_async.witdb");

        await using var db = await new WitDatabaseBuilder()
            .WithFilePath(filePath)
            .WithBTree()
            .WithParallelWrites(ParallelMode.Latched)
            .WithoutTransactions()
            .BuildAsync();

        Assert.That(db.Store, Is.TypeOf<BTreeConcurrentStore>());
    }

    #endregion
}
