using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// What happens to the rows of a memtable flush that failed.
/// </summary>
/// <remarks>
/// The last of the <c>core-lsm</c> findings, and the one the audit recorded as <b>mechanism only</b>:
/// <i>"a failed flush leaves m_immutableMemTable populated forever, and the next flush loses the
/// data ... reproducing it needs an injected I/O failure part-way through a flush, and the current
/// StoreLsm surface offers no way to arrange one."</i>
///
/// The surface exists now. `LsmOptions.SstableFileFactory` - cut for the SSTable fsync work - hands
/// out the file the flush writes into, so a test can hand out one that fails.
///
/// The defect is quiet rather than loud. The WAL is not truncated when a flush fails, so a restart
/// replays the rows and nothing is lost for good; but a <i>running</i> process went on answering reads
/// without them, because the next flush overwrote the pointer that was the only thing still holding
/// them.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class FailedFlushTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-failedflush-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    [Test]
    public void RowsOfAFailedFlushAreStillReadableAfterTheNextOneTest()
    {
        var factory = new FailsOnceSstableFileFactory();

        using var store = new StoreLsm(m_directory, Options(factory));

        for (int i = 0; i < 5; i++)
            store.Put(Key(i), Value(i));

        Assert.That(() => store.Checkpoint(), Throws.InstanceOf<IOException>(),
            "the injected failure has to actually fail the flush");

        // A second batch, and a flush that succeeds - this is the one that used to overwrite the only
        // pointer still holding the first batch.
        for (int i = 5; i < 10; i++)
            store.Put(Key(i), Value(i));

        store.Checkpoint();

        var readable = Enumerable.Range(0, 10).Count(i => store.Get(Key(i)) != null);

        TestContext.Out.WriteLine($"after a failed flush and a successful one: {readable} of 10 readable");

        Assert.That(readable, Is.EqualTo(10),
            "the rows of the failed flush must still be readable - they were accepted, and a store "
            + "that keeps answering while quietly no longer holding them is the worst shape this "
            + "can take");
    }

    /// <summary>
    /// The same rows must come back through a scan, not only through a point read. The two take
    /// different paths through the memtables, and a fix that repaired one would leave the other.
    /// </summary>
    [Test]
    public void RowsOfAFailedFlushAreStillScannableTest()
    {
        var factory = new FailsOnceSstableFileFactory();

        using var store = new StoreLsm(m_directory, Options(factory));

        for (int i = 0; i < 5; i++)
            store.Put(Key(i), Value(i));

        Assert.That(() => store.Checkpoint(), Throws.InstanceOf<IOException>());

        for (int i = 5; i < 10; i++)
            store.Put(Key(i), Value(i));

        store.Checkpoint();

        var scanned = store.Scan(null, null).Select(e => System.Text.Encoding.UTF8.GetString(e.Key)).ToList();

        Assert.That(scanned, Has.Count.EqualTo(10),
            "a scan must see every row the store accepted");
    }

    /// <summary>
    /// A write made after the failed flush is newer and must not be undone by the rows coming back.
    /// </summary>
    [Test]
    public void ANewerWriteWinsOverTheRowsComingBackTest()
    {
        var factory = new FailsOnceSstableFileFactory();

        using var store = new StoreLsm(m_directory, Options(factory));

        store.Put(Key(1), Value(1));

        Assert.That(() => store.Checkpoint(), Throws.InstanceOf<IOException>());

        // Overwrite one key and delete another, after the flush failed.
        store.Put(Key(1), System.Text.Encoding.UTF8.GetBytes("newer"));

        store.Checkpoint();

        Assert.That(store.Get(Key(1)), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("newer")),
            "putting the failed flush's rows back must not resurrect a value that was overwritten "
            + "afterwards");
    }

    /// <summary>
    /// The control: with no injected failure the same workload reads back completely. Without it a
    /// green result above could be about the fixture rather than about the failure path.
    /// </summary>
    [Test]
    public void ControlWithoutAFailureEverythingIsReadableTest()
    {
        using var store = new StoreLsm(m_directory, Options(fileFactory: null));

        for (int i = 0; i < 10; i++)
            store.Put(Key(i), Value(i));

        store.Checkpoint();

        Assert.That(Enumerable.Range(0, 10).Count(i => store.Get(Key(i)) != null), Is.EqualTo(10));
    }

    #endregion

    #region Tools

    private static LsmOptions Options(ISstableFileFactory? fileFactory) => new()
    {
        BackgroundCompaction = false,
        SstableFileFactory = fileFactory,
        MemTableSizeLimit = 64 * 1024
    };

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D4}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i:D4}");

    #endregion
}

/// <summary>
/// Fails the first table it is asked for and behaves normally afterwards, so one flush fails and the
/// next succeeds.
/// </summary>
internal sealed class FailsOnceSstableFileFactory : ISstableFileFactory
{
    private bool m_failed;

    public ISstableFile Create(string path)
    {
        if (m_failed)
            return new SstableFile(path);

        m_failed = true;

        return new FailingOnSync(path);
    }

    private sealed class FailingOnSync : ISstableFile
    {
        private readonly SstableFile m_inner;

        public FailingOnSync(string path) => m_inner = new SstableFile(path);

        public Stream Stream => m_inner.Stream;

        public void Sync() => throw new IOException("injected: the device gave out mid-flush");

        public void Publish() => m_inner.Publish();

        public void Dispose() => m_inner.Dispose();
    }
}
