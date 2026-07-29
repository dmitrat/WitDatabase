using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// Does the LSM path ask for its SSTable to reach the media before it destroys the WAL copy?
/// </summary>
/// <remarks>
/// The audit recorded this as <b>mechanism confirmed, consequence not reproduced</b>: finalisation
/// ended at <c>m_writer.Flush()</c>, which pushes the <c>BinaryWriter</c>'s buffer into the
/// <c>FileStream</c> and no further, and there was no <c>flushToDisk</c> anywhere under
/// <c>Core/LSM/</c>. Showing the loss was said to need a real power cut - a clean process kill is not
/// enough, because the operating system writes its cache back.
///
/// It does not need a power cut. It needs the <b>count</b>: a store that never asks for durability
/// cannot have achieved it, and a count of zero is unambiguous in a way that a surviving-rows count
/// after a kill is not. That is the same move that settled "every commit scans the whole database" in
/// the MVCC batch - count, do not time - and it is why the seam this fixture uses exists.
///
/// The control matters as much as the subject: the WAL on the same path <i>does</i> sync, so a zero
/// on the SSTable is a property of the SSTable path rather than of a counter that cannot count.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class SstableFsyncTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-sstfsync-{Guid.NewGuid():N}");
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
    public void FinishedSstableIsSyncedTest()
    {
        var factory = new CountingSstableFileFactory();
        var path = Path.Combine(m_directory, "sst_000001.sst");

        using (var builder = new SSTableBuilder(path, targetBlockSize: 4096, encryptor: null, factory))
        {
            for (int i = 0; i < 200; i++)
                builder.Add(Key(i), Value(i));

            builder.Finish();
        }

        TestContext.Out.WriteLine(
            $"the builder asked for {factory.SyncCount} sync(s) while writing {factory.CreatedCount} file(s)");

        Assert.That(factory.SyncCount, Is.GreaterThan(0),
            "a store that never asks for its output to reach the media has not made it durable, and "
            + "the LSM path truncates the WAL holding the same data as soon as the SSTable is "
            + "written - so the window is not a narrow one");
    }

    /// <summary>
    /// The counter can see a sync when one happens - otherwise a zero above would prove nothing.
    /// </summary>
    [Test]
    public void ControlTheCounterSeesASyncTest()
    {
        var factory = new CountingSstableFileFactory();
        var path = Path.Combine(m_directory, "control.sst");

        using (var file = factory.Create(path))
        {
            file.Stream.WriteByte(0x01);
            file.Sync();
            file.Sync();
            file.Publish();
        }

        Assert.That(factory.SyncCount, Is.EqualTo(2),
            "the counter has to count a sync that happened, or a zero elsewhere is a broken counter "
            + "rather than a finding");
    }

    /// <summary>
    /// And the seam does not change what ends up in the file: an SSTable written through it reads
    /// back exactly as one written the ordinary way.
    /// </summary>
    [Test]
    public void ControlTheSeamDoesNotChangeTheFileTest()
    {
        var throughSeam = Path.Combine(m_directory, "seam.sst");
        var ordinary = Path.Combine(m_directory, "ordinary.sst");

        Build(throughSeam, new CountingSstableFileFactory());
        Build(ordinary, fileFactory: null);

        Assert.That(File.ReadAllBytes(throughSeam), Is.EqualTo(File.ReadAllBytes(ordinary)),
            "the substituted file must produce the same bytes - a seam that changes the format would "
            + "make every measurement taken through it a statement about the seam");
    }

    /// <summary>
    /// End to end: the store's own memtable flush syncs, not just a builder driven by hand.
    /// </summary>
    /// <remarks>
    /// This is the test that proves the seam is <i>wired</i>. Everything above drives
    /// <see cref="SSTableBuilder"/> directly, so it would stay green even if
    /// <c>LsmOptions.SstableFileFactory</c> never reached <c>StoreLsm</c> and the engine went on
    /// writing unsynced tables.
    ///
    /// It also states the consequence that made this worth fixing: <c>Flush()</c> <b>reduced</b>
    /// durability. It replaced a WAL the caller may have synced with an SSTable that was never
    /// synced, and truncated the WAL immediately afterwards - so asking for the data to be made safe
    /// is precisely what put it at risk.
    /// </remarks>
    [Test]
    public void MemTableFlushThroughTheStoreSyncsItsSstableTest()
    {
        var factory = new CountingSstableFileFactory();

        var options = new LsmOptions
        {
            SstableFileFactory = factory,
            BackgroundCompaction = false,
            EnableWal = true
        };

        using (var store = new StoreLsm(m_directory, options))
        {
            for (int i = 0; i < 100; i++)
                store.Put(Key(i), Value(i));

            store.Flush();
        }

        TestContext.Out.WriteLine(
            $"the store wrote {factory.CreatedCount} SSTable(s) and asked for {factory.SyncCount} sync(s)");

        Assert.Multiple(() =>
        {
            Assert.That(factory.CreatedCount, Is.GreaterThan(0),
                "the flush must have written an SSTable through the configured factory - if it wrote "
                + "none, the option never reached StoreLsm and this fixture is measuring nothing");

            Assert.That(factory.SyncCount, Is.GreaterThanOrEqualTo(factory.CreatedCount),
                "and every table it wrote must have been synced before the WAL holding the same data "
                + "was truncated");
        });
    }

    #endregion

    #region Tools

    private static void Build(string path, ISstableFileFactory? fileFactory)
    {
        using var builder = new SSTableBuilder(path, targetBlockSize: 4096, encryptor: null, fileFactory);

        for (int i = 0; i < 50; i++)
            builder.Add(Key(i), Value(i));

        builder.Finish();
    }

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D6}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i:D6}");

    #endregion
}

/// <summary>
/// An SSTable file that counts how many times it was asked to become durable.
/// </summary>
internal sealed class CountingSstableFileFactory : ISstableFileFactory
{
    public int CreatedCount { get; private set; }

    public int SyncCount { get; private set; }

    public ISstableFile Create(string path)
    {
        CreatedCount++;

        return new CountingSstableFile(this, path);
    }

    internal void RecordSync() => SyncCount++;

    // Wraps the real file rather than reimplementing it, so the counting double cannot drift away
    // from the publish-and-sync semantics it is supposed to be observing.
    private sealed class CountingSstableFile : ISstableFile
    {
        private readonly CountingSstableFileFactory m_owner;
        private readonly SstableFile m_inner;

        public CountingSstableFile(CountingSstableFileFactory owner, string path)
        {
            m_owner = owner;
            m_inner = new SstableFile(path);
        }

        public Stream Stream => m_inner.Stream;

        public void Sync()
        {
            m_owner.RecordSync();
            m_inner.Sync();
        }

        public void Publish() => m_inner.Publish();

        public void Dispose() => m_inner.Dispose();
    }
}
