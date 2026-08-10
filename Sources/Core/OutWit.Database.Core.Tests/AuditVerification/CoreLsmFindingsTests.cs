using NUnit.Framework;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>core-lsm</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The crash these findings describe needs no process kill: the directory is the durable media, so
/// restoring a file the compaction should have deleted reproduces "crashed between publishing the
/// output and deleting the inputs" exactly.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class CoreLsmFindingsTests
{
    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private string m_directory = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), "witdb-lsm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #region The manifest

    /// <summary>
    /// A row deleted before a crashed compaction stays deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The history is the useful part.</b> This was recorded as "MECHANISM CONFIRMED, CONSEQUENCE
    /// NOT REPRODUCED", and the consequence reproduced the moment the instrument was fixed:
    /// <c>Compact()</c> applied the automatic <c>Level0CompactionTrigger</c> to an explicit call, and
    /// this case has two SSTables against a default trigger of four - so the compaction it is named
    /// after never ran. On 2026-08-07 an explicit compaction was made to compact and this case went
    /// red on its own, with the deleted row readable again.
    /// </para>
    /// <para>
    /// <b>The old reasoning also leaned on a proxy.</b> It said the output RETAINS the tombstone,
    /// "verified, not assumed, because Get(k0) returned null after compaction". Null was ambiguous:
    /// <c>Compactor.Compact</c> <b>drops</b> tombstones - "they've done their job" - so after a full
    /// merge k0 read null because it was ABSENT. Absence and a tombstone are identical through
    /// <c>Get</c>, and that difference was the whole defect.
    /// </para>
    /// <para>
    /// <b>Fixed by <see cref="LsmManifest"/>, and by the ORDER it is written in.</b> A compaction
    /// publishes a manifest naming only its output and only then deletes the inputs, so an input that
    /// survives is no longer named and nobody reads it. What this case measures is exactly that: the
    /// survivor is still on disk afterwards - asserted, because "the row is gone" would otherwise be
    /// equally consistent with the file having been deleted after all.
    /// </para>
    /// </remarks>
    [Test]
    public void ACrashedCompactionDoesNotResurrectDeletedRowsTest()
    {
        var holding = Path.Combine(m_directory, "..", Path.GetFileName(m_directory) + "-holding");
        Directory.CreateDirectory(holding);

        try
        {
            using (var store = new StoreLsm(m_directory))
            {
                for (int i = 0; i < 5; i++)
                    store.Put(Key($"k{i}"), Value($"v{i}"));
                store.Checkpoint();

                store.Delete(Key("k0"));
                store.Checkpoint();

                // Keep only the OLDEST input - the one holding k0 as live data. Restoring the
                // tombstone file as well would mask k0 again and prove nothing; the dangerous crash
                // is the one where the tombstone's file is deleted and the data file is not, which
                // is a plain interleaving of two File.Delete calls.
                var oldestInput = Directory.GetFiles(m_directory, "sst_*.sst")
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .First();
                File.Copy(oldestInput, Path.Combine(holding, Path.GetFileName(oldestInput)), overwrite: true);

                store.Compact();
                store.WaitForCompaction();

                // CONTROL, and the one that makes the pin below mean something: with the compaction's
                // output as the only file, the deleted row is gone. If this failed, the resurrection
                // below would be "compaction never ran" rather than "a survivor readmitted it".
                Assert.That(Directory.GetFiles(m_directory, "sst_*.sst"), Has.Length.EqualTo(1),
                    "CONTROL: the compaction did not merge the inputs, so nothing below is about a "
                    + "crashed compaction");

                Assert.That(store.Get(Key("k0")), Is.Null,
                    "the delete did not survive the compaction");
            }

            // The crash: compaction published its output, deleted the tombstone's input, and died
            // before deleting the data input.
            foreach (var file in Directory.GetFiles(holding, "sst_*.sst"))
                File.Copy(file, Path.Combine(m_directory, Path.GetFileName(file)), overwrite: true);

            var restored = Directory.GetFiles(m_directory, "sst_*.sst").Length;

            using var reopened = new StoreLsm(m_directory);

            Assert.Multiple(() =>
            {
                // CONTROL: the survivor must still be lying there. Without this, "the row is gone"
                // is equally consistent with the file having been deleted, and the case would pass
                // on an engine with no manifest at all.
                Assert.That(restored, Is.EqualTo(2),
                    "CONTROL: the crashed compaction's surviving input is not on disk, so nothing is "
                    + "being ignored and this case is not about the manifest");

                Assert.That(reopened.Get(Key("k0")), Is.Null,
                    "a row deleted before the crash came back because an input file survived. The "
                    + "full merge dropped its tombstone, so an unnamed survivor has nothing left to "
                    + "mask it - the live set is being read from the directory rather than from the "
                    + "manifest.");

                // And the rest of the data is still there, so "nothing came back" is not "nothing
                // is readable".
                Assert.That(reopened.Get(Key("k1")), Is.Not.Null,
                    "the surviving rows went with it");
            });

            TestContext.Out.WriteLine(
                $"manifest: {restored} sst files on disk after the crash, k0 stays deleted, k1 readable");
        }
        finally
        {
            try { Directory.Delete(holding, recursive: true); } catch { /* best effort */ }
        }
    }

    #endregion

    #region LsmParallelWriter.Dispose flushes what it buffered

    // FIXED by phase 5's second half, released as 6.0.0: Dispose drains the thread-local buffers rather
    // than discarding them, which is the tail of every workload. Marker lifted by the 2026-08-10 ledger
    // census, which ran it rather than reading it.

    [Test]
    public void DisposingTheParallelWriterFlushesWhatItBufferedTest()
    {
        // Finding: LsmParallelWriter.cs:497 - Dispose discards thread-local buffers that were never
        // submitted instead of flushing them. A caller who writes and then disposes - the ordinary
        // `using` shape - loses everything that had not yet crossed the buffer threshold.
        using var store = new StoreLsm(m_directory);

        var writer = new LsmParallelWriter(store);
        for (int i = 0; i < 5; i++)
            writer.Put(Key($"k{i}"), Value($"v{i}"));

        writer.Dispose();
        store.Checkpoint();

        var missing = Enumerable.Range(0, 5)
            .Where(i => store.Get(Key($"k{i}")) == null)
            .Select(i => $"k{i}")
            .ToList();

        Assert.That(missing, Is.Empty,
            "disposing a writer must not throw away what the caller already wrote");
    }

    #endregion

    #region SSTable durability

    // FIXED 2026-07-29 in phase 4 - "the SSTable is never fsynced but the WAL is truncated
    // immediately after" (SSTableBuilder.cs:184). It did not need the real power cut this note
    // originally called for: it needed the COUNT. A store that never asks for durability cannot have
    // achieved it, and zero is unambiguous where a surviving-row count after a process kill is not.
    // Measured through a seam at 0 syncs per SSTable, then fixed - SSTableBuilder.Finish now syncs
    // before returning, so the WAL copy is only destroyed after the SSTable is on the media.
    // See Durability/SstableFsyncTests.cs, which also pins that Flush() no longer REDUCES
    // durability: it used to replace a synced WAL with an unsynced SSTable and then truncate the WAL.
    //
    // FIXED 2026-07-29 - "a failed flush leaves m_immutableMemTable populated forever"
    // (StoreLsm.cs:550). This note said reproducing it needs an injected I/O failure the StoreLsm
    // surface offered no way to arrange; LsmOptions.SstableFileFactory, cut for the fsync work above,
    // is that way. Measured with it: after a failed flush and a successful one, 5 of 10 accepted rows
    // were still readable - the next flush overwrote the only pointer holding the first batch. The
    // failure path now puts those entries back into the active memtable, with anything written since
    // winning. See Durability/FailedFlushTests.cs.

    #endregion
}
