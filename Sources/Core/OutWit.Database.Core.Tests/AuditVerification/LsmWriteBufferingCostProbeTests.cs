using System.Buffers.Binary;
using System.Diagnostics;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 11 instrument - does the LSM store's write buffering pay for itself?
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LsmParallelStore"/> is the only thing a non-<c>None</c> <c>Parallel Mode</c> still does,
/// now that serialising the B+Tree store is unconditional: it gives the LSM store thread-local write
/// buffers and a background merge. <see cref="StoreLsm"/> locks internally and is safe without it, so
/// this is a <b>throughput</b> device and nothing else - and <b>nobody has ever measured whether it
/// delivers any</b>. It has been kept because it was filed under "parallel".
/// </para>
/// <para>
/// Excluded from CI by category: a timing assertion on a shared runner is a flaky test, not a
/// measurement. Interleaved A/B/A/B over several rounds and reported as a spread, because one timing
/// run on this project has lied more than once.
/// </para>
/// <para>
/// <b>The control is the engine's own counters.</b> Each buffered round asserts that
/// <c>BuffersSubmitted</c> and <c>EntriesMerged</c> actually moved - a store that buffered nothing
/// would time identically to the bare one and look like a null result, which is exactly how phase 10
/// nearly published "LSM does not win" from a run where the LSM store performed no flushes and no
/// compactions.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
[Category("Performance")]
public class LsmWriteBufferingCostProbeTests
{
    #region Constants

    private const int ROWS = 100_000;

    private const int ROUNDS = 3;

    private const int VALUE_SIZE = 64;

    /// <summary>Rows per transaction in the batched shape - what phase 10's ingest benchmark used.</summary>
    private const int BATCH = 1_000;

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_lsmbuf_{Guid.NewGuid():N}");
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

    #region The measurement

    /// <summary>
    /// Sustained ingest with and without the write buffer, at one and at four writer threads.
    /// </summary>
    /// <remarks>
    /// Both thread counts, because they are different questions. One thread asks whether the buffer is
    /// a batching win on its own; four ask whether it is worth anything when writers actually contend,
    /// which is the case its name claims. The engine's model is one writer at a time across
    /// connections, so four threads is the generous reading rather than the typical one.
    /// </remarks>
    [Test]
    [TestCase(1)]
    [TestCase(4)]
    public void MeasureWhatLsmWriteBufferingBuysTest(int writers)
    {
        var direct = new List<double>();
        var buffered = new List<double>();

        for (var round = 0; round < ROUNDS; round++)
        {
            // Interleaved, so that a machine that warms up or throttles does it to both.
            direct.Add(Ingest(buffering: false, writers, out _));
            buffered.Add(Ingest(buffering: true, writers, out var statistics));

            Assert.That(statistics.BuffersSubmitted, Is.GreaterThan(0),
                "the buffered store submitted no buffers, so nothing was buffered and the comparison " +
                "is measuring the same thing twice");
            Assert.That(statistics.EntriesMerged, Is.GreaterThanOrEqualTo(ROWS),
                $"the buffered store merged {statistics.EntriesMerged} entries of {ROWS} written - " +
                "the mechanism did not carry the workload it is being credited with");
        }

        var directMedian = Median(direct);
        var bufferedMedian = Median(buffered);

        TestContext.Out.WriteLine(
            $"COST   {writers} writer(s), {ROWS} rows, StoreLsm direct    : " +
            $"median {directMedian:F0} ms, all [{string.Join(", ", direct.Select(v => v.ToString("F0")))}]");
        TestContext.Out.WriteLine(
            $"COST   {writers} writer(s), {ROWS} rows, LsmParallelStore  : " +
            $"median {bufferedMedian:F0} ms, all [{string.Join(", ", buffered.Select(v => v.ToString("F0")))}]");
        TestContext.Out.WriteLine(
            $"COST   {writers} writer(s), ratio (buffered / direct) = {bufferedMedian / directMedian:F3} " +
            $"({(bufferedMedian < directMedian ? "buffering wins" : "buffering loses")})");
    }

    #endregion

    #region Tools

    private readonly record struct Statistics(long BuffersSubmitted, long EntriesMerged);

    private double Ingest(bool buffering, int writers, out Statistics statistics)
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);

        var lsm = new StoreLsm(directory, new LsmOptions());

        LsmParallelStore? parallel = buffering
            ? new LsmParallelStore(lsm, new LsmParallelStoreOptions { TrackStatistics = true }, ownsStore: true)
            : null;

        IKeyValueStore store = parallel ?? (IKeyValueStore)lsm;

        try
        {
            var stopwatch = Stopwatch.StartNew();

            RunWriters(store, writers);

            // The buffer is only honest about its cost if the writes it accepted are in the store when
            // the clock stops. Flush is what a commit calls, and it is what drains the buffers.
            store.Flush();

            stopwatch.Stop();

            statistics = parallel != null
                ? new Statistics(parallel.BuffersSubmitted, parallel.EntriesMerged)
                : default;

            return stopwatch.Elapsed.TotalMilliseconds;
        }
        finally
        {
            store.Dispose();
        }
    }

    private static void RunWriters(IKeyValueStore store, int writers)
    {
        var perWriter = ROWS / writers;

        var threads = Enumerable.Range(0, writers).Select(index => new Thread(() =>
        {
            var value = new byte[VALUE_SIZE];

            // Deterministic and not in key order: a sequential key stream is the LSM store's best case
            // and would flatter both sides unevenly. Each writer has its own seed so the streams do not
            // collide.
            var random = new Random(9973 + index);

            for (var i = 0; i < perWriter; i++)
                store.Put(Key(random.Next(int.MaxValue)), value);
        })
        {
            IsBackground = true
        }).ToList();

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join(TimeSpan.FromMinutes(5));
    }

    private static byte[] Key(int value)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(key, value);
        return key;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    #endregion
}
