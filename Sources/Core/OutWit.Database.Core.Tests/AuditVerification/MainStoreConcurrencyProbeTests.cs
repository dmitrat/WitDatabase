using System.Buffers.Binary;
using System.Diagnostics;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 11 instrument - what serialises the MAIN store when no parallel mode is asked for, and what
/// serialising it costs when nobody needed it.
/// </summary>
/// <remarks>
/// <para>
/// Phase 5 proved that a bare <see cref="StoreBTree"/> corrupts when two writers enter the same leaf
/// split, and 6.0.0 answered it by wrapping every SECONDARY INDEX store unconditionally - a second
/// <i>connection</i> is enough to reach the defect, so the wrapper is not conditional on a parallel
/// mode. <b>The main store was left conditional.</b> With the default <c>Parallel Mode=None</c> it is a
/// bare <see cref="StoreBTree"/>, which has no locking of any kind, and its own indexes are serialised
/// while it is not.
/// </para>
/// <para>
/// That asymmetry is either covered by the transaction layer above it or it is a defect, and reading
/// the code cannot settle it. This fixture settles it the way phase 5 did: park one writer inside a
/// leaf split of the main store, let a second writer in, release, and count what survived.
/// <see cref="SecondaryIndexConcurrencyProbeTests"/> is where the seam and the parking machinery were
/// first measured; this reuses the shape against a database built the ordinary way.
/// </para>
/// <para>
/// <b>Controls, in both directions.</b> The bare store is the positive control - the harness must be
/// able to see damage at all, and phase 5 established that a bare store is damaged. Parking with no
/// second writer is the negative control - the parking itself must destroy nothing. Both run in every
/// pass, because a probe that can only report "nothing was lost" cannot tell safety from blindness.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class MainStoreConcurrencyProbeTests
{
    #region Constants

    private const int PAGE_SIZE = 4096;

    private const int CACHE_SIZE = 1000;

    /// <summary>Enough entries to cross the first leaf split, where the parked writer stops.</summary>
    private const int PARKED_WRITER_ENTRIES = 400;

    /// <summary>The second writer's key, chosen to land in the same leaf as the first writer's.</summary>
    private const int SECOND_WRITER_KEY = 1_000_000;

    #endregion

    #region Types

    public enum Subject
    {
        /// <summary>No database at all - the positive control, known damaged since phase 5.</summary>
        BareStore,

        /// <summary>The wrapper the main store gets when a parallel mode is asked for.</summary>
        SerialisedStore,

        /// <summary>A database built the default way: transactions, MVCC, no parallel mode.</summary>
        DatabaseMvccNoParallelMode,

        /// <summary>The same with the lock-based transactional store.</summary>
        DatabaseLocksNoParallelMode,

        /// <summary>The same with no transaction layer at all.</summary>
        DatabaseNoTransactionsNoParallelMode,

        /// <summary>The default plus a parallel mode - what the keyword is supposed to buy.</summary>
        DatabaseMvccParallelMode
    }

    private sealed record Outcome(
        bool ParkedInsideASplit,
        Exception? FirstWriterError,
        Exception? SecondWriterError,
        IReadOnlyList<int> MissingKeys)
    {
        public bool Damaged => FirstWriterError != null || SecondWriterError != null || MissingKeys.Count > 0;

        public string Describe() =>
            $"damaged={Damaged}, missing={MissingKeys.Count}, " +
            $"firstError={FirstWriterError?.GetType().Name ?? "none"}, " +
            $"secondError={SecondWriterError?.GetType().Name ?? "none"}";
    }

    #endregion

    #region Controls

    /// <summary>
    /// Positive control: the harness can see damage. A bare store is what phase 5 measured, and if this
    /// comes back undamaged the experiment is blind and no "safe" verdict below may be believed.
    /// </summary>
    [Test]
    public void ControlABareStoreIsDamagedTest()
    {
        var outcome = Run(Subject.BareStore, withSecondWriter: true);

        Report("a second writer let into a bare StoreBTree mid-split", outcome);

        Assert.That(outcome.ParkedInsideASplit, Is.True, "nothing parked, so nothing was measured");
        Assert.That(outcome.Damaged, Is.True,
            "two writers were inside the same leaf split of an unserialised store and nothing went " +
            "wrong - the harness cannot see damage, so every other verdict in this fixture is void");
    }

    /// <summary>
    /// Negative control: the parking destroys nothing on its own.
    /// </summary>
    [Test]
    public void ControlParkingAloneLosesNothingTest()
    {
        var outcome = Run(Subject.DatabaseMvccNoParallelMode, withSecondWriter: false);

        Report("one writer parked inside its split, with no second writer", outcome);

        Assert.That(outcome.ParkedInsideASplit, Is.True, "nothing parked, so nothing was measured");
        Assert.That(outcome.Damaged, Is.False,
            "parking a writer inside its split lost entries on its own - the harness is broken, not " +
            "the subject");
    }

    /// <summary>
    /// Control: the wrapper does what it is for. If this is damaged, the wrapper is not the answer and
    /// the question below is a different one.
    /// </summary>
    [Test]
    public void ControlTheWrapperKeepsTheSecondWriterOutTest()
    {
        var outcome = Run(Subject.SerialisedStore, withSecondWriter: true);

        Report("the same over BTreeConcurrentStore", outcome);

        Assert.That(outcome.ParkedInsideASplit, Is.True, "nothing parked, so nothing was measured");
        Assert.That(outcome.Damaged, Is.False, "the serialised store was damaged");
    }

    #endregion

    #region The question

    /// <summary>
    /// The question this fixture exists for: with the default <c>Parallel Mode=None</c>, is the main
    /// store protected by the transaction layer above it, or is it exposed exactly as a secondary index
    /// store was before 6.0.0?
    /// </summary>
    /// <remarks>
    /// Asked once per transaction model, because they are three different amounts of machinery between
    /// a caller and the tree, and the answer decides whether <c>Parallel Mode</c> can be a setting at
    /// all. A mode that switches correctness on is not a setting.
    /// </remarks>
    [Test]
    [TestCase(Subject.DatabaseMvccNoParallelMode)]
    [TestCase(Subject.DatabaseLocksNoParallelMode)]
    [TestCase(Subject.DatabaseNoTransactionsNoParallelMode)]
    [TestCase(Subject.DatabaseMvccParallelMode)]
    public void ProbeTwoWritersIntoTheMainStoreTest(Subject subject)
    {
        var outcome = Run(subject, withSecondWriter: true);

        Report($"two writers into the main store <{subject}>", outcome);

        Assert.That(outcome.ParkedInsideASplit, Is.True, "nothing parked, so nothing was measured");

        // Reported and asserted as an OBSERVATION of the current build, not as a claim that this is
        // correct. If a case here is damaged, that case is a defect and the assertion below is the
        // thing to invert once it is fixed - the fixture's controls establish that the harness can see
        // damage and that the wrapper prevents it.
        Assert.That(outcome.Damaged, Is.False,
            $"{subject} lost data or threw when two writers met inside a leaf split of the MAIN store. " +
            "The transaction layer above it does not serialise this path, and Parallel Mode=None - the " +
            "default - leaves it exposed. See ControlTheWrapperKeepsTheSecondWriterOutTest for the fix " +
            "expressed as a measurement.");
    }

    #endregion

    #region The cost

    /// <summary>
    /// The other half of the decision: what the wrapper costs when nobody needed it. Excluded from CI
    /// by category, because a timing assertion in a shared runner is a flaky test, not a measurement.
    /// </summary>
    /// <remarks>
    /// Interleaved A/B/A/B over several rounds and reported as a spread rather than a single number -
    /// one timing run on this project has lied more than once. The work is single-threaded, which is
    /// the only case where the wrapper could be pure overhead.
    /// </remarks>
    [Test]
    [Category("Performance")]
    public void MeasureWhatSerialisingCostsASingleThreadTest()
    {
        const int ROUNDS = 5;
        const int OPERATIONS = 20_000;

        var bare = new List<double>();
        var wrapped = new List<double>();

        for (var round = 0; round < ROUNDS; round++)
        {
            // Interleaved, so that a machine that warms up or throttles does it to both.
            bare.Add(TimeSingleThreaded(serialised: false, OPERATIONS));
            wrapped.Add(TimeSingleThreaded(serialised: true, OPERATIONS));
        }

        var bareMedian = Median(bare);
        var wrappedMedian = Median(wrapped);

        TestContext.Out.WriteLine(
            $"COST   bare StoreBTree      {OPERATIONS} put+get: " +
            $"median {bareMedian:F1} ms, all [{string.Join(", ", bare.Select(v => v.ToString("F1")))}]");
        TestContext.Out.WriteLine(
            $"COST   BTreeConcurrentStore {OPERATIONS} put+get: " +
            $"median {wrappedMedian:F1} ms, all [{string.Join(", ", wrapped.Select(v => v.ToString("F1")))}]");
        TestContext.Out.WriteLine(
            $"COST   ratio (wrapped / bare) = {wrappedMedian / bareMedian:F3}");
    }

    private static double TimeSingleThreaded(bool serialised, int operations)
    {
        var storage = new StorageMemory(PAGE_SIZE);
        var inner = new StoreBTree(storage, CACHE_SIZE, ownsStorage: true);

        using IKeyValueStore store = serialised
            ? new BTreeConcurrentStore(inner, options: null, ownsStore: true)
            : inner;

        var value = new byte[64];

        // Not timed: the tree has to exist before the measurement, or page allocation dominates.
        for (var i = 0; i < 1_000; i++)
            store.Put(Key(i), value);

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < operations; i++)
        {
            store.Put(Key(i), value);
            _ = store.Get(Key(i));
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    #endregion

    #region Tools

    /// <summary>
    /// Parks one writer inside a leaf split of the main store, lets a second in, then counts.
    /// </summary>
    private static Outcome Run(Subject subject, bool withSecondWriter)
    {
        var parked = new ParkingStorage(new StorageMemory(PAGE_SIZE));

        var (writer, disposable) = Build(subject, parked);

        try
        {
            // Warm the cache: with a warm cache the only storage call an insert makes is the page
            // allocation a split does, which is what makes "parked on the first call" mean "parked
            // inside a split". Measured in SecondaryIndexConcurrencyProbeTests.
            writer.Put(Key(-1), Value(-1));

            parked.Record = true;

            var first = RunOnBackgroundThread(() =>
            {
                parked.ArmForThisThread();

                for (var i = 0; i < PARKED_WRITER_ENTRIES; i++)
                    writer.Put(Key(i), Value(i));
            });

            var parkedInside = parked.WaitUntilParked(TimeSpan.FromSeconds(30));

            BackgroundRun? second = null;

            if (withSecondWriter)
            {
                second = RunOnBackgroundThread(() => writer.Put(Key(SECOND_WRITER_KEY), Value(SECOND_WRITER_KEY)));

                Assert.That(second.Started.Wait(TimeSpan.FromSeconds(30)), Is.True,
                    "the second writer's thread never started, so nothing was measured");

                // Long enough for an unserialised store to have walked into the leaf; a serialised one
                // simply blocks here and finishes after the release.
                second.Wait(TimeSpan.FromSeconds(2));
            }

            parked.Release();

            first.Wait(TimeSpan.FromSeconds(60));
            second?.Wait(TimeSpan.FromSeconds(60));

            var missing = new List<int>();

            for (var i = 0; i < PARKED_WRITER_ENTRIES; i++)
            {
                if (!SafeHas(writer, i))
                    missing.Add(i);
            }

            if (withSecondWriter && !SafeHas(writer, SECOND_WRITER_KEY))
                missing.Add(SECOND_WRITER_KEY);

            return new Outcome(parkedInside && parked.ParkedOn == StorageCallKind.SetSize,
                first.Error, second?.Error, missing);
        }
        finally
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // A store this experiment has corrupted may throw on the way out; that is the subject,
                // not the harness.
            }
        }
    }

    /// <summary>
    /// Builds the subject and returns the surface a writer uses, plus what has to be disposed. The
    /// database cases go through <see cref="WitDatabase"/> so that every layer the product puts between
    /// a caller and the tree is in the experiment.
    /// </summary>
    private static (IWriter Writer, IDisposable Disposable) Build(Subject subject, IStorage storage)
    {
        switch (subject)
        {
            case Subject.BareStore:
            {
                var store = new StoreBTree(storage, CACHE_SIZE, ownsStorage: true);
                return (new StoreWriter(store), store);
            }

            case Subject.SerialisedStore:
            {
                var store = new BTreeConcurrentStore(
                    new StoreBTree(storage, CACHE_SIZE, ownsStorage: true), options: null, ownsStore: true);
                return (new StoreWriter(store), store);
            }

            default:
            {
                var builder = new WitDatabaseBuilder().WithStorage(storage).WithBTree();

                builder = subject switch
                {
                    Subject.DatabaseMvccNoParallelMode => builder.WithMvcc(),
                    Subject.DatabaseLocksNoParallelMode => builder.WithTransactions(),
                    Subject.DatabaseNoTransactionsNoParallelMode => builder.WithoutTransactions(),
                    Subject.DatabaseMvccParallelMode => builder.WithMvcc().WithParallelWrites(ParallelMode.Auto),
                    _ => throw new ArgumentOutOfRangeException(nameof(subject))
                };

                var database = builder.Build();
                return (new DatabaseWriter(database), database);
            }
        }
    }

    private interface IWriter
    {
        void Put(byte[] key, byte[] value);

        byte[]? Get(byte[] key);
    }

    private sealed class StoreWriter(IKeyValueStore store) : IWriter
    {
        public void Put(byte[] key, byte[] value) => store.Put(key, value);

        public byte[]? Get(byte[] key) => store.Get(key);
    }

    private sealed class DatabaseWriter(WitDatabase database) : IWriter
    {
        public void Put(byte[] key, byte[] value) => database.Put(key, value);

        public byte[]? Get(byte[] key) => database.Get(key);
    }

    private static bool SafeHas(IWriter writer, int key)
    {
        try
        {
            return writer.Get(Key(key)) != null;
        }
        catch (Exception e)
        {
            TestContext.Out.WriteLine($"PROBE    lookup of {key} threw {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    private static byte[] Key(int value)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(key, value);
        return key;
    }

    private static byte[] Value(int value)
    {
        var buffer = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    private static void Report(string question, Outcome outcome) =>
        TestContext.Out.WriteLine($"PROBE  {question}  ->  {outcome.Describe()}");

    private static BackgroundRun RunOnBackgroundThread(Action action)
    {
        var run = new BackgroundRun();

        var thread = new Thread(() =>
        {
            run.Started.Set();

            try
            {
                action();
            }
            catch (Exception e)
            {
                run.Error = e;
            }
            finally
            {
                run.Finished.Set();
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        return run;
    }

    private sealed class BackgroundRun
    {
        public ManualResetEventSlim Started { get; } = new(false);

        public ManualResetEventSlim Finished { get; } = new(false);

        public Exception? Error { get; set; }

        public bool Wait(TimeSpan timeout) => Finished.Wait(timeout);
    }

    private enum StorageCallKind
    {
        ReadPage,
        WritePage,
        SetSize,
        Flush
    }

    /// <summary>
    /// Storage decorator that parks one designated thread on its next call and holds it there. Same
    /// mechanism as <see cref="SecondaryIndexConcurrencyProbeTests"/>, kept separate because that
    /// fixture's copy is private to it and the two experiments have to be able to move apart.
    /// </summary>
    private sealed class ParkingStorage(IStorage inner) : IStorage
    {
        private readonly ManualResetEventSlim m_release = new(false);
        private readonly ManualResetEventSlim m_parked = new(false);

        private int m_armedThreadId;

        /// <summary>Parking is off until the probe turns it on, so construction cannot park.</summary>
        public bool Record { get; set; }

        public StorageCallKind? ParkedOn { get; private set; }

        public void ArmForThisThread() => Volatile.Write(ref m_armedThreadId, Environment.CurrentManagedThreadId);

        public bool WaitUntilParked(TimeSpan timeout) => m_parked.Wait(timeout);

        public void Release() => m_release.Set();

        private void OnCall(StorageCallKind kind)
        {
            if (!Record)
                return;

            if (Volatile.Read(ref m_armedThreadId) != Environment.CurrentManagedThreadId)
                return;

            Volatile.Write(ref m_armedThreadId, 0);

            ParkedOn = kind;
            m_parked.Set();
            m_release.Wait(TimeSpan.FromSeconds(60));
        }

        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            OnCall(StorageCallKind.ReadPage);
            inner.ReadPage(pageNumber, buffer);
        }

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.ReadPage);
            return inner.ReadPageAsync(pageNumber, buffer, cancellationToken);
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            OnCall(StorageCallKind.WritePage);
            inner.WritePage(pageNumber, buffer);
        }

        public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.WritePage);
            return inner.WritePageAsync(pageNumber, buffer, cancellationToken);
        }

        public void Flush()
        {
            OnCall(StorageCallKind.Flush);
            inner.Flush();
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.Flush);
            return inner.FlushAsync(cancellationToken);
        }

        public void SetSize(long pageCount)
        {
            OnCall(StorageCallKind.SetSize);
            inner.SetSize(pageCount);
        }

        public int PageSize => inner.PageSize;

        public long PageCount => inner.PageCount;

        public bool IsReadOnly => inner.IsReadOnly;

        public string ProviderKey => inner.ProviderKey;

        public void Dispose() => inner.Dispose();
    }

    #endregion
}
