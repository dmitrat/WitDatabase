using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 5 instrument - what a foreign <c>FlushAllAsync</c> does to a thread that is still writing.
/// </summary>
/// <remarks>
/// <para>
/// The marker this replaces was recorded as NOT REPRODUCED, then confirmed by CI on both PR runs,
/// which is why it was left suppressed as timing-dependent. Its scenario is the reason it needed CI:
/// the producer is <b>parked</b> while the foreign flush runs, so the writes never overlap the flush
/// at all. Measured here, that scenario fails 0 times in 20 rounds.
/// </para>
/// <para>
/// <see cref="ProbeAContendedFlushLosesNothingTest"/> makes the writes overlap, and the defect then
/// reproduces on an ordinary development machine: of the first three rounds, two lost runs of eight
/// and nine <b>consecutive</b> entries, and the third died inside <c>LsmWriteBuffer.Drain</c> with
/// <c>ArgumentException: Destination array was not long enough</c> - a <c>List</c> copied while
/// another thread was adding to it. The loss being a contiguous run is the signature CI reported:
/// the tail of a batch, the entries appended after the merge loop had already taken its copy.
/// </para>
/// <para>
/// A stress-shaped test cannot prove a fix, and this one is not asked to. It is the disproof that was
/// missing: it was red here before the buffers were exchanged under a gate and is green after, and it
/// stays in the suite so that undoing the exchange cannot be silent.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class LsmParallelWriterFlushProbeTests
{
    #region Constants

    /// <summary>Entries the single-threaded control writes.</summary>
    private const int CONTROL_ENTRIES = 2000;

    /// <summary>
    /// Flushes one round of the contended probe performs. The round ends when they are done, so the
    /// overlap is a property of the experiment rather than of which thread this machine happens to
    /// schedule first.
    /// </summary>
    private const int FLUSHES_PER_ROUND = 20;

    /// <summary>
    /// How far the producer may run ahead of the last flush. Without a limit the two do not converge:
    /// 2000 buffered writes take about a millisecond and the flush that merges them takes a hundred,
    /// so an unpaced producer either finishes before the first flush - one overlap for the whole
    /// round, which measured too weak to catch the defect every time - or outruns the merges until
    /// each flush hands over a larger buffer than the last and the round takes minutes.
    /// </summary>
    private const int PRODUCER_CREDIT = 200;

    /// <summary>Ceiling on a round, so a flusher that dies cannot leave the producer running.</summary>
    private const int PRODUCER_CEILING = 200_000;

    /// <summary>
    /// Rounds of the contended probe. The defect showed in the first three rounds when it was
    /// present, so ten is well past the point of being evidence and still costs a couple of seconds.
    /// </summary>
    private const int CONTENDED_ROUNDS = 10;

    #endregion

    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_lsm_flush_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: the probe below can only mean something if a flush that is NOT contended keeps
    /// everything. One thread, writing and flushing itself, with nobody else involved.
    /// </summary>
    [Test]
    public void ControlAnUncontendedFlushKeepsEveryEntryTest()
    {
        var directory = Path.Combine(m_testDir, "uncontended");
        Directory.CreateDirectory(directory);

        using var store = new StoreLsm(directory);
        using var writer = new LsmParallelWriter(store);

        for (int i = 0; i < CONTROL_ENTRIES; i++)
        {
            writer.Put(Key(i), Value(i));

            if (i % 100 == 0)
                writer.FlushAllAsync().GetAwaiter().GetResult();
        }

        writer.FlushAllAsync().GetAwaiter().GetResult();
        store.Flush();

        Assert.That(MissingKeys(store, CONTROL_ENTRIES), Is.Empty,
            "a single thread lost entries flushing its own buffer - the probe measures nothing");
    }

    /// <summary>
    /// Control: the producer must actually get far enough for the flusher to collide with it. A round
    /// in which the producer finished before the first flush would be a green that measured nothing.
    /// </summary>
    [Test]
    public void ControlTheFlusherRunsWhileTheProducerIsWritingTest()
    {
        var outcome = RunContendedRound(0);

        TestContext.Out.WriteLine(
            $"PROBE  flushes issued while the producer was writing  ->  {outcome.FlushesDuringWriting}, "
            + $"entries the producer got through  ->  {outcome.EntriesWritten}");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.FlushesDuringWriting, Is.EqualTo(FLUSHES_PER_ROUND),
                "the flusher stopped early, so the round did not contend for as long as it claims");
            Assert.That(outcome.EntriesWritten, Is.GreaterThan(PRODUCER_CREDIT),
                "the producer never got past its first credit window, so there was little to collide with");
        });
    }

    #endregion

    #region Probes

    /// <summary>
    /// Probe: a producer writing while another thread flushes everything, repeatedly. Every entry the
    /// producer was told was buffered has to be in the store at the end.
    /// </summary>
    [Test]
    public void ProbeAContendedFlushLosesNothingTest()
    {
        var lostRounds = 0;
        var threwRounds = 0;
        var firstFailure = "";

        for (int round = 0; round < CONTENDED_ROUNDS; round++)
        {
            var outcome = RunContendedRound(round);

            if (outcome.Error != null)
            {
                threwRounds++;
                if (firstFailure.Length == 0)
                    firstFailure = $"round {round} threw {outcome.Error.GetType().Name}: {outcome.Error.Message}";
            }

            if (outcome.Missing.Count > 0)
            {
                lostRounds++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = $"round {round} lost {outcome.Missing.Count} of {outcome.EntriesWritten}, "
                                 + $"starting at {outcome.Missing[0]}";
                }
            }
        }

        TestContext.Out.WriteLine(
            $"PROBE  contended rounds  ->  {lostRounds} lost entries, {threwRounds} threw, of {CONTENDED_ROUNDS}");

        Assert.Multiple(() =>
        {
            // INVERTED BY THE FIX, and the inversion is the proof it landed. Before the buffers were
            // exchanged under their slot's gate, two of the first three rounds lost runs of eight and
            // nine consecutive entries and the third threw out of List.ToList inside Drain.
            Assert.That(threwRounds, Is.Zero, firstFailure);
            Assert.That(lostRounds, Is.Zero, firstFailure);
        });
    }

    #endregion

    #region Scenarios

    private sealed class RoundOutcome
    {
        public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

        public Exception? Error { get; init; }

        public int FlushesDuringWriting { get; init; }

        public int EntriesWritten { get; init; }
    }

    /// <summary>
    /// One round: a producer buffering entries without pause while this thread flushes every
    /// thread's buffer a fixed number of times. The producer stops when the flushes are done, so the
    /// overlap is guaranteed rather than hoped for, and what has to survive is every entry the
    /// producer reports having written.
    /// </summary>
    private RoundOutcome RunContendedRound(int round)
    {
        var directory = Path.Combine(m_testDir, $"contended_{round}");
        Directory.CreateDirectory(directory);

        using var store = new StoreLsm(directory);
        using var writer = new LsmParallelWriter(store);

        Exception? producerError = null;
        var producerStarted = new ManualResetEventSlim(false);
        var producerDone = new ManualResetEventSlim(false);
        var stop = false;
        var written = 0;
        var flushes = 0;
        var flushedUpTo = 0;

        var producer = new Thread(() =>
        {
            producerStarted.Set();

            try
            {
                for (int i = 0; i < PRODUCER_CEILING && !Volatile.Read(ref stop); i++)
                {
                    // Stay within a window of the last flush, so the flusher always has something
                    // fresh to take and never a hundred thousand entries at once.
                    while (i - Volatile.Read(ref flushedUpTo) >= PRODUCER_CREDIT && !Volatile.Read(ref stop))
                        Thread.Yield();

                    if (Volatile.Read(ref stop))
                        break;

                    writer.Put(Key(i), Value(i));

                    // Published after the write, so the count never claims an entry that was not
                    // handed over, and so the flusher can tell there is something new to take.
                    Volatile.Write(ref written, i + 1);
                }
            }
            catch (Exception e)
            {
                // An unhandled exception on a background thread ends the process and takes every
                // other test with it, so it is captured and reported instead.
                producerError = e;
            }
            finally
            {
                producerDone.Set();
            }
        })
        {
            IsBackground = true
        };

        producer.Start();
        producerStarted.Wait(TimeSpan.FromSeconds(30));

        Exception? flusherError = null;

        try
        {
            while (Volatile.Read(ref flushes) < FLUSHES_PER_ROUND && producerError == null)
            {
                // Wait for the producer to buffer something new, by yielding rather than sleeping -
                // a millisecond of sleep is long enough for it to run out its whole credit.
                while (Volatile.Read(ref written) <= Volatile.Read(ref flushedUpTo) && producerError == null)
                    Thread.Yield();

                if (producerError != null)
                    break;

                var upTo = Volatile.Read(ref written);

                writer.FlushAllAsync().GetAwaiter().GetResult();

                Volatile.Write(ref flushedUpTo, upTo);
                Volatile.Write(ref flushes, Volatile.Read(ref flushes) + 1);
            }
        }
        catch (Exception e)
        {
            // The flush itself used to die on a buffer another thread was appending to.
            flusherError = e;

            // Let the producer out of its credit window, or it waits for a flush that will not come.
            Volatile.Write(ref flushedUpTo, int.MaxValue / 2);
        }

        Volatile.Write(ref stop, true);
        producer.Join(TimeSpan.FromSeconds(30));

        var count = Volatile.Read(ref written);

        if (flusherError == null)
        {
            writer.FlushAllAsync().GetAwaiter().GetResult();
            store.Flush();
        }

        return new RoundOutcome
        {
            Missing = flusherError == null ? MissingKeys(store, count) : Array.Empty<string>(),
            Error = producerError ?? flusherError,
            FlushesDuringWriting = Volatile.Read(ref flushes),
            EntriesWritten = count
        };
    }

    #endregion

    #region Tools

    private static List<string> MissingKeys(StoreLsm store, int count) =>
        Enumerable.Range(0, count)
            .Where(i => store.Get(Key(i)) == null)
            .Select(KeyName)
            .ToList();

    private static string KeyName(int i) => $"k{i:D4}";

    private static byte[] Key(int i) => TextEncoding.UTF8.GetBytes(KeyName(i));

    private static byte[] Value(int i) => TextEncoding.UTF8.GetBytes($"v{i:D4}");

    #endregion
}
