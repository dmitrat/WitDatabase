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

    #region Compaction has no manifest

    [Test]
    public void CrashedCompactionDoesNotResurrectDeletedRowsTest()
    {
        // MECHANISM CONFIRMED, CONSEQUENCE NOT REPRODUCED - and the reason is worth keeping. The
        // live set really is a directory listing, so a surviving input IS readmitted. But it loses:
        // Recover() sorts by filename and the compaction output carries a higher id, so it is
        // treated as newest; and the output RETAINS the tombstone - verified, not assumed, because
        // Get(k0) returned null after compaction when the output was the only file left. A
        // resurrected row would need the output to drop the tombstone or to sort behind a survivor,
        // neither of which happens here. This test stays active as the pin for both properties.
        //
        // Finding: StoreLsm.cs:519 - compaction keeps no manifest. The live SSTable set is literally
        // `Directory.GetFiles(m_directory, "sst_*.sst")` (StoreLsm.cs:601), so any input file the
        // compaction failed to delete is silently readmitted as live data on the next open - and the
        // rows it holds come back from the dead.
        var holding = Path.Combine(m_directory, "..", Path.GetFileName(m_directory) + "-holding");
        Directory.CreateDirectory(holding);

        try
        {
            using (var store = new StoreLsm(m_directory))
            {
                for (int i = 0; i < 5; i++)
                    store.Put(Key($"k{i}"), Value($"v{i}"));
                store.Flush();

                store.Delete(Key("k0"));
                store.Flush();

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

                Assert.That(store.Get(Key("k0")), Is.Null,
                    "the delete must have survived compaction");
            }

            // The crash: compaction published its output, deleted the tombstone's input, and died
            // before deleting the data input.
            foreach (var file in Directory.GetFiles(holding, "sst_*.sst"))
                File.Copy(file, Path.Combine(m_directory, Path.GetFileName(file)), overwrite: true);

            using var reopened = new StoreLsm(m_directory);

            Assert.That(reopened.Get(Key("k0")), Is.Null,
                "a row deleted before the crash must not come back because an input file survived");
        }
        finally
        {
            try { Directory.Delete(holding, recursive: true); } catch { /* best effort */ }
        }
    }

    #endregion

    #region LsmParallelWriter.Dispose discards unsubmitted buffers

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and totally: all five entries were lost - k0..k4 all missing "
            + "after Dispose() followed by store.Flush(). The ordinary `using` shape throws away "
            + "everything the caller wrote that had not yet crossed the buffer threshold. "
            + "core-lsm, Core/LSM/LsmParallelWriter.cs:497")]
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
        store.Flush();

        var missing = Enumerable.Range(0, 5)
            .Where(i => store.Get(Key($"k{i}")) == null)
            .Select(i => $"k{i}")
            .ToList();

        Assert.That(missing, Is.Empty,
            "disposing a writer must not throw away what the caller already wrote");
    }

    #endregion

    #region SSTable durability

    // CONFIRMED BY INSPECTION, consequence not reproduced - "the SSTable is never fsynced but the
    // WAL is truncated immediately after" (SSTableBuilder.cs:184). The finalisation path ends with
    // `m_writer.Flush()`, which pushes the BinaryWriter's buffer into the FileStream and no further:
    // there is no `m_stream.Flush(flushToDisk: true)` anywhere in the file, so the SSTable is only
    // in the OS page cache when the WAL that still holds the same data is truncated. Demonstrating
    // the loss needs a real power cut - a clean process kill is not enough, because the OS still
    // writes its cache back. Recorded rather than faked.
    //
    // Same for "a failed flush leaves m_immutableMemTable populated forever" (StoreLsm.cs:550):
    // reproducing it needs an injected I/O failure part-way through a flush, which the current
    // StoreLsm surface offers no way to arrange.

    #endregion
}
