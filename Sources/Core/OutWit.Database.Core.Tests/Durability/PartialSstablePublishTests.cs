using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// What a crash <i>while writing</i> an SSTable leaves behind, and what the next open makes of it.
/// </summary>
/// <remarks>
/// The audit's compaction finding looked at the other half of this window - a crash <i>after</i> the
/// output was published, with an input still on disk - and that half is pinned by
/// <c>CoreLsmFindingsTests.CrashedCompactionDoesNotResurrectDeletedRowsTest</c>: the survivor is
/// readmitted but loses, because the output sorts newer and keeps its tombstones.
///
/// This is the half nobody looked at, and it was worse. Both the memtable flush and the compactor
/// wrote straight to the final name - <c>sst_NNNNNN.sst</c> - so a crash part-way through left a
/// truncated file already carrying the name recovery looks for, with the highest id, which made it
/// the newest table in the store. Measured before the fix: the next open failed outright with
/// <c>InvalidDataException: Invalid SSTable magic</c>. <b>One crash at the wrong moment and the
/// database could not be opened at all.</b>
///
/// Two separate questions live here and they have different answers:
/// <list type="number">
/// <item>a table that was never finished must never appear - fixed, by writing under a name the
/// store ignores and renaming into place;</item>
/// <item>a table that <i>is</i> damaged must be reported rather than silently skipped - already true,
/// and pinned below, because silently dropping a table is how a database loses data quietly.</item>
/// </list>
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class PartialSstablePublishTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region A table that was never finished never appears

    /// <summary>
    /// The process dies part-way through writing a table: nothing is finished, and - this is the
    /// point - <b>nothing is disposed either</b>.
    /// </summary>
    /// <remarks>
    /// The first version of this test used an injected write failure and a <c>using</c>, and it
    /// passed <i>with the fix reverted</i> - because disposing an unfinished builder deletes the
    /// fragment, which masked whether the rename did anything at all. A crash runs no cleanup, so the
    /// builder is deliberately abandoned here rather than disposed. That is the only shape in which
    /// the rename is what saves the store.
    /// </remarks>
    [Test]
    public void CrashWhileWritingLeavesNoTableForTheStoreToReadTest()
    {
        WriteOneTable();

        var beforeCrash = Tables();

        AbandonAWriteInProgress(Path.Combine(m_directory, "sst_009999.sst"));

        // Release the handle the abandoned builder still holds, the way a dead process would. Its
        // Dispose never runs - only the stream's finalizer - so no cleanup happens.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.Multiple(() =>
        {
            Assert.That(Tables(), Is.EqualTo(beforeCrash),
                "the store's table list must be exactly what it was before the crash - an unfinished "
                + "table is not a table, and writing it under the name recovery looks for is what "
                + "made a crash mid-write everyone's problem");

            using var reopened = new StoreLsm(m_directory, NoBackgroundCompaction());

            Assert.That(
                Enumerable.Range(0, 20).Count(i => reopened.Get(Key(i)) != null), Is.EqualTo(20),
                "and everything that was durable before the crash is still readable");
        });
    }

    /// <summary>
    /// A write that fails and unwinds cleanly takes its fragment with it, rather than leaving it to
    /// accumulate. Separate from the crash above, and measuring a different mechanism.
    /// </summary>
    [Test]
    public void FailedWriteCleansUpAfterItselfTest()
    {
        var path = Path.Combine(m_directory, "sst_009999.sst");

        Assert.That(() =>
        {
            using var builder = new SSTableBuilder(path, 4096, null, new FailingSstableFileFactory());

            for (int i = 100; i < 200; i++)
                builder.Add(Key(i), Value(i));

            builder.Finish();
        }, Throws.InstanceOf<IOException>(), "the injected failure has to actually fail the write");

        Assert.That(Directory.GetFiles(m_directory), Is.Empty,
            "neither a table nor the fragment it was being written as may be left behind");
    }

    /// <summary>
    /// The control: a finished table <i>is</i> published. Without it the test above would pass
    /// simply because nothing is ever published.
    /// </summary>
    [Test]
    public void ControlFinishedTableIsPublishedTest()
    {
        var path = Path.Combine(m_directory, "sst_000001.sst");

        using (var builder = new SSTableBuilder(path, 4096))
        {
            for (int i = 0; i < 20; i++)
                builder.Add(Key(i), Value(i));

            builder.Finish();
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True, "a finished table appears under its final name");

            Assert.That(Directory.GetFiles(m_directory, SstableFile.BUILDING_PREFIX + "*"), Is.Empty,
                "and nothing is left behind under the name it was written as");
        });
    }

    #endregion

    #region A damaged table is reported, not skipped

    /// <summary>
    /// A LIVE table that is damaged - by a pre-fix version, by hardware, by anything - must stop the
    /// open loudly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same principle the WAL fix settled: a database may lose data, but it must say so. Silently
    /// skipping an unreadable table would turn a hardware fault into missing rows nobody was told
    /// about.
    /// </para>
    /// <para>
    /// <b>The damage now has to be done to a table the manifest NAMES</b>, and the difference is the
    /// point rather than a detail. This case used to drop a truncated file into the directory under a
    /// name nothing had written, and the open threw because the live set was whatever the directory
    /// held. Since <see cref="LsmManifest"/> the live set is stated, and a file nobody named is not a
    /// damaged table of ours - it is an orphan, which is exactly what a crashed compaction leaves and
    /// what the manifest exists to ignore. The guarantee is unchanged for every file that is actually
    /// part of the database; see the case below for the other half.
    /// </para>
    /// </remarks>
    [Test]
    public void DamagedTableIsReportedRatherThanSilentlySkippedTest()
    {
        WriteOneTable();

        var live = Tables().Single();
        var bytes = File.ReadAllBytes(live);

        // Truncated in place, so the file the manifest names is the file that is damaged.
        File.WriteAllBytes(live, bytes.AsSpan(0, bytes.Length / 2).ToArray());

        Assert.That(
            () => new StoreLsm(m_directory, NoBackgroundCompaction()),
            Throws.InstanceOf<InvalidDataException>(),
            "an unreadable table must be reported - returning a store that quietly holds fewer rows "
            + "than its own manifest says is the failure mode this project treats as most serious");
    }

    /// <summary>
    /// And a file the manifest does not name does not stop the open, however damaged it is.
    /// </summary>
    /// <remarks>
    /// The other half of the decision above, and it is deliberate rather than tolerated: the orphan a
    /// crashed compaction leaves behind is unreadable often enough - it may be a half-written output -
    /// and refusing to open the database because of a file that is not part of it would turn the
    /// manifest from a repair into a new way to lose access to the data.
    /// </remarks>
    [Test]
    public void AnUnnamedDamagedFileDoesNotStopTheOpenTest()
    {
        WriteOneTable();

        var live = Tables().Single();
        var bytes = File.ReadAllBytes(live);

        File.WriteAllBytes(
            Path.Combine(m_directory, "sst_009999.sst"),
            bytes.AsSpan(0, bytes.Length / 2).ToArray());

        using var store = new StoreLsm(m_directory, NoBackgroundCompaction());

        Assert.That(store.Scan(null, null).Count(), Is.EqualTo(20),
            "the database did not open on its own tables with a stray damaged file beside them");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Starts writing a table and walks away from it, the way a process that died would.
    /// </summary>
    /// <remarks>
    /// Deliberately in its own method with no <c>using</c> and no dispose: the builder must become
    /// unreachable with nothing having run on it, so the collection in the caller can release the
    /// file handle without any of the builder's cleanup happening.
    /// </remarks>
    private void AbandonAWriteInProgress(string path)
    {
        var builder = new SSTableBuilder(path, 4096);

        for (int i = 100; i < 200; i++)
            builder.Add(Key(i), Value(i));
    }

    private void WriteOneTable()
    {
        using var store = new StoreLsm(m_directory, NoBackgroundCompaction());

        for (int i = 0; i < 20; i++)
            store.Put(Key(i), Value(i));

        store.Checkpoint();
    }

    private string[] Tables() =>
        Directory.GetFiles(m_directory, "sst_*.sst").OrderBy(f => f, StringComparer.Ordinal).ToArray();

    private static LsmOptions NoBackgroundCompaction() => new()
    {
        BackgroundCompaction = false
    };

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D4}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i:D4}");

    #endregion
}

/// <summary>
/// An SSTable file whose sync fails, standing in for the media giving out part-way through a write.
/// </summary>
internal sealed class FailingSstableFileFactory : ISstableFileFactory
{
    public ISstableFile Create(string path) => new FailingSstableFile(path);

    private sealed class FailingSstableFile : ISstableFile
    {
        private readonly SstableFile m_inner;

        public FailingSstableFile(string path) => m_inner = new SstableFile(path);

        public Stream Stream => m_inner.Stream;

        public void Sync() => throw new IOException("injected: the device gave out");

        public void Publish() => m_inner.Publish();

        public void Dispose() => m_inner.Dispose();
    }
}
