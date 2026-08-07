using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.LSM;

/// <summary>
/// The live SSTable set is stated in a file, and every way of interrupting the statement is safe.
/// </summary>
/// <remarks>
/// <para>
/// A compaction merges every SSTable into one and <b>drops the tombstones</b> - which is legitimate
/// only because the output is meant to replace the whole live set - and then deletes the inputs. The
/// safety is entirely in the ORDER: build the output, publish a manifest naming only it, and only
/// then delete. Before the publish the inputs are the truth and the output is an orphan; after it the
/// output is the truth and the inputs are orphans. This fixture interrupts at each of those points
/// and asks what the next open reads.
/// </para>
/// <para>
/// <b>Interrupted by hand rather than by killing a process</b>, because the two points are one rename
/// apart and no kill can be aimed that finely. Each case arranges the on-disk state a crash at that
/// moment would leave - the same technique the power-cut model uses, and with the same limit: it
/// proves what the engine does with that state, not that a real crash produces exactly it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LsmManifestTests
{
    #region Constants

    /// <summary>
    /// A key written in the FIRST checkpoint and deleted in a later one, so its value and its
    /// tombstone live in different files.
    /// </summary>
    /// <remarks>
    /// This is what makes the interrupted cases able to fail. A scan deduplicates by key, so a file
    /// readmitted by mistake does not change the total - the first version of those cases asserted
    /// the total and passed with the manifest ignored entirely. A row whose tombstone is in one file
    /// and whose value is in another is the only thing that tells "the manifest was obeyed" from "the
    /// directory was read".
    /// </remarks>
    private const string DELETED_KEY = "k00_000";

    #endregion

    #region Setup

    private string m_directory = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"lsm_manifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region What it says

    [Test]
    public void TheManifestNamesTheLiveSetTest()
    {
        using (var store = Open())
        {
            WriteCheckpoints(store, tables: 3);
        }

        var manifest = LsmManifest.Read(m_directory);

        Assert.Multiple(() =>
        {
            Assert.That(manifest, Is.Not.Null, "no manifest was written at all");

            Assert.That(manifest!.Sstables, Has.Count.EqualTo(3),
                "the manifest does not name the files that are actually live");

            Assert.That(manifest.Sstables, Is.EqualTo(OnDisk()).AsCollection,
                "the manifest and the directory disagree about a store nothing has interrupted");

            Assert.That(manifest.NextSstableId, Is.GreaterThanOrEqualTo(3),
                "the next id would hand out a name a file already has");
        });
    }

    [Test]
    public void ACompactionLeavesTheManifestNamingOnlyItsOutputTest()
    {
        using var store = Open();

        WriteCheckpoints(store, tables: 3);
        store.Compact();

        var manifest = LsmManifest.Read(m_directory);

        Assert.Multiple(() =>
        {
            Assert.That(manifest!.Sstables, Has.Count.EqualTo(1));
            Assert.That(OnDisk(), Has.Count.EqualTo(1), "the inputs were not cleaned up");
        });
    }

    #endregion

    #region Interrupted

    /// <summary>
    /// A crash BEFORE the manifest is published loses nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This case cannot fail on the manifest, and saying so is the point.</b> It was written to
    /// show that an unnamed output is ignored, and it passes with the manifest ignored entirely -
    /// measured, by reverting the store to the directory listing. The reason is worth keeping: at
    /// this crash point the output is a faithful merge of exactly the files the old manifest names,
    /// so reading it as live gives the same answers. There is nothing to distinguish.
    /// </para>
    /// <para>
    /// What it does measure is that the earlier crash point is <b>benign</b> - the rows are all there
    /// and the deleted one is still deleted - which is worth a case of its own, because the other
    /// direction of this fix would be an engine that refuses to open a store whose manifest and
    /// directory disagree. The case that CAN fail is <c>AnUnnamedSurvivorIsIgnoredTest</c>, and it
    /// does.
    /// </para>
    /// </remarks>
    [Test]
    public void ACrashBeforeThePublishLosesNothingTest()
    {
        using (var store = Open())
        {
            WriteCheckpoints(store, tables: 3);
            Delete(store, DELETED_KEY);
        }

        // Everything as it stands before the compaction: the three inputs and the manifest naming
        // them. This is what a crash before the rename leaves behind, and it has to be captured
        // rather than reconstructed, because the compaction deletes the inputs.
        var stash = Stash();

        using (var store = Open())
        {
            store.Compact();
        }

        var output = OnDisk().Single();

        Restore(stash);

        Assert.Multiple(() =>
        {
            // CONTROL: the state under test is "the output exists AND the old manifest is in place".
            // If the output were gone there would be nothing to ignore.
            Assert.That(OnDisk(), Contains.Item(output),
                "CONTROL: the compaction's output is not on disk, so nothing is being ignored");

            Assert.That(LsmManifest.Read(m_directory)!.Sstables, Does.Not.Contain(output),
                "CONTROL: the restored manifest names the output, so this is not a crash before the "
                + "publish");

            using var reopened = Open();

            Assert.That(reopened.Get(Bytes(DELETED_KEY)), Is.Null,
                "the deleted row is readable after a crash before the publish");

            Assert.That(reopened.Scan(null, null).Count(), Is.EqualTo(149),
                "rows went missing at a crash point where both readings of the directory contain "
                + "them - the store opened on neither the manifest's files nor the directory's");
        });
    }

    /// <summary>
    /// A crash AFTER the manifest is published: an input the deletes did not reach.
    /// </summary>
    [Test]
    public void AnUnnamedSurvivorIsIgnoredTest()
    {
        using (var store = Open())
        {
            WriteCheckpoints(store, tables: 3);
        }

        // The survivor is the file holding the row LIVE; the tombstone goes into a later one, so
        // readmitting the survivor alone is what resurrects it.
        var stash = Stash();
        var survivor = OnDisk()[0];

        using (var store = Open())
        {
            Delete(store, DELETED_KEY);
        }

        using (var store = Open())
        {
            store.Compact();
        }

        // The crash: the manifest already names the output, and one input was never deleted. Only
        // that one file comes back - the manifest stays as the compaction left it.
        File.Copy(Path.Combine(stash, survivor), Path.Combine(m_directory, survivor), overwrite: true);

        Assert.Multiple(() =>
        {
            Assert.That(OnDisk(), Has.Count.EqualTo(2),
                "CONTROL: the survivor is not on disk, so nothing is being ignored");

            Assert.That(LsmManifest.Read(m_directory)!.Sstables, Does.Not.Contain(survivor),
                "CONTROL: the manifest names the survivor, so reading it is correct and this case "
                + "measures nothing");

            using var reopened = Open();

            Assert.That(reopened.Get(Bytes(DELETED_KEY)), Is.Null,
                "the survivor was readmitted and the deleted row came back with it - the merge "
                + "dropped the tombstone, so an unnamed file has nothing left to mask it");

            Assert.That(reopened.Scan(null, null).Count(), Is.EqualTo(149),
                "the live rows did not survive the compaction");
        });
    }

    #endregion

    #region No manifest, and a broken one

    /// <summary>
    /// A database written before manifests existed still opens, from the directory.
    /// </summary>
    [Test]
    public void AStoreWithNoManifestFallsBackToTheDirectoryTest()
    {
        using (var store = Open())
        {
            WriteCheckpoints(store, tables: 3);
        }

        File.Delete(Path.Combine(m_directory, LsmManifest.FILE_NAME));

        using var reopened = Open();

        Assert.That(reopened.Scan(null, null).Count(), Is.EqualTo(150),
            "a store with no manifest read nothing, so an existing database would open empty");
    }

    /// <summary>
    /// A manifest that cannot be trusted is refused whole rather than half-read.
    /// </summary>
    /// <remarks>
    /// The count line is what makes this detectable: a file that lost its last line would otherwise
    /// read as a shorter live set, which is a silently older database. Refused, the caller falls back
    /// to the directory - a worse answer than a manifest and a better one than a guess.
    /// </remarks>
    [TestCase("", TestName = "empty")]
    [TestCase("WitDbLsmManifest 1\nnext 3\ncount 3\nsst_000000.sst\n", TestName = "truncated")]
    [TestCase("nonsense", TestName = "not a manifest")]
    public void AnUntrustworthyManifestIsRefused(string contents)
    {
        using (var store = Open())
        {
            WriteCheckpoints(store, tables: 3);
        }

        File.WriteAllText(Path.Combine(m_directory, LsmManifest.FILE_NAME), contents);

        Assert.Multiple(() =>
        {
            Assert.That(LsmManifest.Read(m_directory), Is.Null,
                "a manifest that cannot be trusted was read anyway");

            using var reopened = Open();

            Assert.That(reopened.Scan(null, null).Count(), Is.EqualTo(150),
                "the store did not fall back to the directory, so a damaged manifest costs the data "
                + "rather than the manifest");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Foreground compaction and a trigger out of the way, so every merge here is one the test asked
    /// for.
    /// </summary>
    private StoreLsm Open() => new(m_directory, new LsmOptions
    {
        BackgroundCompaction = false,
        Level0CompactionTrigger = 100
    });

    /// <summary>
    /// Deletes a key into a checkpoint of its own, so the tombstone lands in a later file than the
    /// value.
    /// </summary>
    private static void Delete(StoreLsm store, string key)
    {
        store.Delete(Bytes(key));
        store.Checkpoint();
    }

    private static void WriteCheckpoints(StoreLsm store, int tables)
    {
        for (var table = 0; table < tables; table++)
        {
            for (var i = 0; i < 50; i++)
                store.Put(Bytes($"k{table:D2}_{i:D3}"), Bytes($"value {table} {i}"));

            store.Checkpoint();
        }
    }

    /// <summary>
    /// Copies the store's whole on-disk state aside, and returns where it went.
    /// </summary>
    /// <remarks>
    /// A crash state has to be CAPTURED rather than reconstructed: the compaction deletes its inputs,
    /// so by the time a test wants to restore one it is gone. The first version of these cases tried
    /// to copy files back from where they no longer were, and failed for that reason rather than for
    /// anything about the engine.
    /// </remarks>
    private string Stash()
    {
        var stash = Path.Combine(Path.GetTempPath(), $"lsm_stash_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stash);

        foreach (var file in Directory.GetFiles(m_directory))
            File.Copy(file, Path.Combine(stash, Path.GetFileName(file)), overwrite: true);

        return stash;
    }

    private void Restore(string stash)
    {
        foreach (var file in Directory.GetFiles(stash))
            File.Copy(file, Path.Combine(m_directory, Path.GetFileName(file)), overwrite: true);
    }

    private IReadOnlyList<string> OnDisk() =>
        Directory.GetFiles(m_directory, "sst_*.sst")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList()!;

    private static byte[] Bytes(string text) => TextEncoding.UTF8.GetBytes(text);

    #endregion
}
