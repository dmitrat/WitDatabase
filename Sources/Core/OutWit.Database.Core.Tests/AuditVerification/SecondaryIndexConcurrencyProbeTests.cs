using System.Buffers.Binary;
using System.Reflection;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 5 instrument - what serialises the store behind a SECONDARY INDEX, measured
/// deterministically rather than by stress.
/// </summary>
/// <remarks>
/// <para>
/// The subject is the defect <c>HighConcurrencyStressTest</c> exposes: concurrent connections corrupt
/// a B+Tree leaf split inside a secondary index, because <c>WitDatabaseBuilder</c> wraps the MAIN
/// store for concurrent access while <c>CreateBTreeIndexFactory</c> hands every index a bare
/// <see cref="StoreBTree"/>, which has no locking of any kind. That stress test cannot prove a fix -
/// it fails about 3 times in 27 whole-fixture runs, so "it passed twenty times" is not evidence. This
/// fixture replaces it with an exact experiment.
/// </para>
/// <para>
/// <b>The seam, chosen from a measurement rather than guessed.</b> With a warm page cache an
/// <c>Add</c> that fits in the leaf makes <b>no storage call at all</b>; the only storage traffic
/// during an insert is the <c>SetSize</c> that <c>SplitLeaf</c> makes when it allocates the new leaf.
/// So parking a thread on its first storage call parks it <i>inside</i> the split - after
/// <c>CollectLeafEntries</c> has snapshotted the leaf and before <c>node.Clear()</c> rewrites it.
/// <see cref="ControlAnAddThatDoesNotSplitTouchesNoStorageTest"/> is that measurement, kept as a
/// control, and the parking storage additionally records which call it parked on so the probes can
/// assert it was the allocation.
/// </para>
/// <para>
/// <b>Controls, in both directions.</b> A probe that can only report "entries were lost" cannot tell
/// a defect from a broken harness. <see cref="ControlTheParkedWriterAloneLosesNothingTest"/> runs the
/// same parking with no second writer and keeps every entry;
/// <see cref="ProbeConcurrentAddOverASerialisedIndexStoreTest"/> runs the same body over the wrapper
/// the main store already gets and keeps every entry; and
/// <see cref="ProbeConcurrentAddOverAnInMemoryIndexStoreTest"/> shows the other stores the index
/// factory can hand out lock internally, which brackets the gap to <see cref="StoreBTree"/>.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class SecondaryIndexConcurrencyProbeTests
{
    #region Constants

    /// <summary>Page size the index factory uses.</summary>
    private const int PAGE_SIZE = 4096;

    /// <summary>Cache size the index factory gives an index - the builder's default 1000 / 4.</summary>
    private const int CACHE_SIZE = 250;

    /// <summary>
    /// Entries the parked writer inserts. Enough to cross the first leaf split, which is where it
    /// parks: with these key sizes the leaf fills at about 190 entries.
    /// </summary>
    private const int PARKED_WRITER_ENTRIES = 400;

    /// <summary>The second writer's key, chosen to land in the same leaf as the first writer's.</summary>
    private const int SECOND_WRITER_KEY = 1_000_000;

    #endregion

    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_index_race_{Guid.NewGuid():N}");
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
    /// Control, and the measurement the parking seam rests on: with a warm cache an <c>Add</c> that
    /// fits in the leaf makes no storage call, and the ones that do are page allocations made by the
    /// split. Without this, "parked on the first storage call" would be an assumption.
    /// </summary>
    [Test]
    public void ControlAnAddThatDoesNotSplitTouchesNoStorageTest()
    {
        var storage = new RecordingStorage(new StorageMemory(PAGE_SIZE));
        using var store = new StoreBTree(storage, CACHE_SIZE, ownsStorage: true);
        using var index = new SecondaryIndexKeyValueStore("ix", store, isUnique: false, ownsStore: false);

        storage.Record = true;

        for (int i = 0; i < PARKED_WRITER_ENTRIES; i++)
            index.Add(IndexKey(i), PrimaryKey(i));

        var kinds = storage.Calls.Select(call => call.Kind).Distinct().ToArray();

        Report("storage calls made by 400 index Adds on a warm cache", "calls",
            $"{storage.Calls.Count} [{string.Join(", ", kinds)}]");

        Assert.Multiple(() =>
        {
            Assert.That(storage.Calls, Is.Not.Empty,
                "no storage call at all - nothing could ever park, so the probes measure nothing");
            Assert.That(kinds, Is.EqualTo(new[] { StorageCallKind.SetSize }),
                "an insert made a storage call that is not a page allocation, so parking on the "
                + "first call would no longer mean 'parked inside a split'");
        });
    }

    /// <summary>
    /// Control: the parking itself destroys nothing. One writer, parked inside its first split and
    /// then released, with no second writer - every entry survives.
    /// </summary>
    [Test]
    public void ControlTheParkedWriterAloneLosesNothingTest()
    {
        var outcome = RunConcurrentAdd(Subject.BareStoreBTree, withSecondWriter: false);

        Report("entries after parking one writer inside its split, with no second writer",
            "missing", outcome.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ParkedOn, Is.EqualTo(StorageCallKind.SetSize),
                "the writer did not park inside a page allocation, so it was not inside a split");
            Assert.That(outcome.FirstWriterError, Is.Null, $"the parked writer threw: {outcome.FirstWriterError}");
            Assert.That(outcome.MissingKeys, Is.Empty,
                "parking a writer inside its split lost entries on its own - the harness is broken, "
                + "not the subject");
        });
    }

    #endregion

    #region The defect - a bare StoreBTree behind a secondary index

    /// <summary>
    /// Probe: one writer parked inside a leaf split, a second writer let into the same leaf, and then
    /// the first released. This is what two connections inserting rows did to a shared index before
    /// index stores were serialised, and the test is kept so that removing the wrapper cannot be a
    /// silent change - it is the standing evidence for why the wrapper exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape of the damage varies and is deliberately not pinned: over ten runs the second writer
    /// threw <c>ArgumentOutOfRangeException</c> or <c>IndexOutOfRangeException</c> nine times, and
    /// once nothing threw at all and three entries were simply gone - two of them the FIRST writer's,
    /// already inserted and acknowledged. The exception is the lucky outcome.
    /// </para>
    /// <para>
    /// <b>It asserted one of those shapes anyway until 2026-08-15</b> - that the SECOND writer's key
    /// was among the missing ones - which contradicted the paragraph above. It reddened CI on a branch
    /// that touches no engine code and passed on a re-run of the same commit: on a loaded runner the
    /// second writer did not finish inside the two seconds
    /// <see cref="Outcome.SecondWriterFinishedWhileFirstWasParked"/> waits, so it landed after the
    /// release and its own entry survived - while <b>207 of the first writer's were lost</b>. The
    /// damage was real and larger than usual, and the case failed for having said in advance which
    /// entry it would be.
    /// </para>
    /// <para>
    /// So it asserts what it can measure on a machine whose scheduling it does not control - that two
    /// writers in one leaf split damage the index - and REPORTS the shape rather than pinning it.
    /// </para>
    /// </remarks>
    [Test]
    public void ProbeConcurrentAddOverABareIndexStoreTest()
    {
        var outcome = RunConcurrentAdd(Subject.BareStoreBTree, withSecondWriter: true);

        Report("a second writer let into a bare index store while the first is inside its split",
            "outcome", outcome.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ParkedOn, Is.EqualTo(StorageCallKind.SetSize),
                "the first writer did not park inside a page allocation, so it was not inside a split");

            // PINS A DEFECT, NOT CORRECT BEHAVIOUR. A bare StoreBTree is what CreateBTreeIndexFactory
            // hands every secondary index, and it has no locking of any kind: the second writer walks
            // straight into the leaf the first one is halfway through splitting, snapshots it, and
            // the two then rewrite it from two different snapshots. Invert this when index stores are
            // serialised - nothing may be lost and no writer may throw.
            Assert.That(outcome.Damage, Is.Not.EqualTo(Damage.None),
                "two writers were inside the same leaf split and nothing went wrong - re-measure "
                + "before believing it");

            // WHOSE entries went is the weather; that acknowledged work was lost, or that a writer
            // threw, is the finding. Both are already what Damage classifies, so this says out loud
            // what the value has to have come from.
            Assert.That(outcome.MissingKeys.Count > 0
                        || outcome.FirstWriterError != null
                        || outcome.SecondWriterError != null, Is.True,
                "nothing is missing and nobody threw, so Damage was classified from something this "
                + "probe does not measure: " + outcome.Describe());
        });
    }

    /// <summary>
    /// Probe: the identical body over the wrapper the MAIN store already gets. This is the fix
    /// expressed as a measurement - if the wrapper is what keeps the second writer out, then the
    /// defect is the absence of the wrapper and nothing else.
    /// </summary>
    [Test]
    public void ProbeConcurrentAddOverASerialisedIndexStoreTest()
    {
        var outcome = RunConcurrentAdd(Subject.SerialisedStoreBTree, withSecondWriter: true);

        Report("the same, over a store wrapped in BTreeConcurrentStore", "outcome", outcome.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ParkedOn, Is.EqualTo(StorageCallKind.SetSize),
                "the first writer did not park inside a page allocation, so it was not inside a split");
            Assert.That(outcome.FirstWriterError, Is.Null, $"the first writer threw: {outcome.FirstWriterError}");
            Assert.That(outcome.SecondWriterError, Is.Null, $"the second writer threw: {outcome.SecondWriterError}");
            Assert.That(outcome.MissingKeys, Is.Empty, "a serialised index store still lost entries");
        });
    }

    /// <summary>
    /// Probe: the same body over <see cref="StoreInMemory"/>, the store the index factory hands out
    /// for memory databases. It locks internally, so it survives - which brackets the defect to
    /// <see cref="StoreBTree"/> rather than to secondary indexes in general.
    /// </summary>
    [Test]
    public void ProbeConcurrentAddOverAnInMemoryIndexStoreTest()
    {
        var outcome = RunConcurrentAdd(Subject.InMemory, withSecondWriter: true);

        Report("the same, over the in-memory index store", "outcome", outcome.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.FirstWriterError, Is.Null, $"the first writer threw: {outcome.FirstWriterError}");
            Assert.That(outcome.MissingKeys, Is.Empty,
                "the in-memory index store lost entries too - the gap is wider than the B+Tree store");
        });
    }

    #endregion

    #region What the builder actually hands a secondary index

    /// <summary>
    /// Probe: build a database the ordinary way, create an index on it, and ask what store is behind
    /// it. This is the wiring half - the probes above show the wrapper is what matters, and this one
    /// shows whether the product ever applies it.
    /// </summary>
    /// <remarks>
    /// The store behind an index is private, and deliberately so; a probe reads it by reflection
    /// because the alternative is inferring the wiring from a race.
    /// </remarks>
    [Test]
    public void ProbeWhatTheBuilderHandsASecondaryIndexTest()
    {
        var path = Path.Combine(m_testDir, "index_wiring.witdb");

        var builder = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithTransactions();

        using var database = builder.Build();

        var index = database.CreateIndex("ix_probe", isUnique: false);
        var store = StoreBehind(index);

        Report("the store behind a secondary index", "provider", store.ProviderKey);

        // INVERTED BY THE FIX, and the inversion is the proof it landed. This used to read
        // StoreBTree.PROVIDER_KEY and pass: the builder wrapped the MAIN store for concurrent access
        // and handed every secondary index a raw StoreBTree with no locking at all. Note it holds for
        // BOTH cases - a second connection is enough to reach the defect, so the wrapper is not
        // conditional on a parallel mode.
        Assert.That(store.ProviderKey, Is.EqualTo(BTreeConcurrentStore.PROVIDER_KEY),
            "the index store is unserialised again - see ProbeConcurrentAddOverABareIndexStoreTest "
            + "for what that costs");
    }

    #endregion

    #region Tools

    private enum Subject
    {
        /// <summary>What <c>CreateBTreeIndexFactory</c> builds today.</summary>
        BareStoreBTree,

        /// <summary>The same store behind the wrapper the main store gets.</summary>
        SerialisedStoreBTree,

        /// <summary>What the factory builds for a memory database.</summary>
        InMemory
    }

    private enum Damage
    {
        None,
        EntriesLost,
        WriterThrew
    }

    private sealed class Outcome
    {
        public StorageCallKind? ParkedOn { get; init; }

        /// <summary>
        /// Reported, deliberately NOT asserted on. It is false in both directions, and the reason is
        /// itself a finding: <c>PageManager.AllocatePage</c> holds its lock across the
        /// <c>SetSize</c> that the parked writer is stopped inside, so the second writer blocks when
        /// it asks for a page of its own - which is long AFTER it has walked into the leaf and
        /// snapshotted it. The allocator's lock delays the corruption instead of preventing it, so
        /// "the second writer had to wait" says nothing about whether the store is serialised. The
        /// discriminator is the damage.
        /// </summary>
        public bool SecondWriterFinishedWhileFirstWasParked { get; init; }

        public Exception? FirstWriterError { get; init; }

        public Exception? SecondWriterError { get; init; }

        public IReadOnlyList<int> MissingKeys { get; init; } = Array.Empty<int>();

        public Damage Damage =>
            FirstWriterError != null || SecondWriterError != null ? Damage.WriterThrew
            : MissingKeys.Count > 0 ? Damage.EntriesLost
            : Damage.None;

        public string Describe() =>
            $"damage={Damage}, missing={MissingKeys.Count} {Preview()}, "
            + $"secondWriterGotIn={SecondWriterFinishedWhileFirstWasParked}, "
            + $"firstError={FirstWriterError?.GetType().Name ?? "none"}, "
            + $"secondError={SecondWriterError?.GetType().Name ?? "none"}, "
            + $"parkedOn={ParkedOn?.ToString() ?? "nothing"}";

        private string Preview() =>
            MissingKeys.Count == 0
                ? ""
                : $"[{string.Join(", ", MissingKeys.Take(8))}{(MissingKeys.Count > 8 ? ", ..." : "")}]";
    }

    /// <summary>
    /// Drives the experiment: one writer parked inside its first leaf split, optionally a second
    /// writer let into the same leaf, then release and count what survived.
    /// </summary>
    private static Outcome RunConcurrentAdd(Subject subject, bool withSecondWriter)
    {
        var parked = new ParkingStorage(new StorageMemory(PAGE_SIZE));

        IKeyValueStore store = subject switch
        {
            Subject.BareStoreBTree => new StoreBTree(parked, CACHE_SIZE, ownsStorage: true),
            Subject.SerialisedStoreBTree => new BTreeConcurrentStore(
                new StoreBTree(parked, CACHE_SIZE, ownsStorage: true), options: null, ownsStore: true),
            Subject.InMemory => new StoreInMemory(),
            _ => throw new ArgumentOutOfRangeException(nameof(subject))
        };

        using var index = new SecondaryIndexKeyValueStore("ix", store, isUnique: false, ownsStore: true);

        var first = RunOnBackgroundThread(() =>
        {
            // Park this thread on its first storage call, which the control above establishes can
            // only happen inside a split.
            parked.ArmForThisThread();

            for (int i = 0; i < PARKED_WRITER_ENTRIES; i++)
                index.Add(IndexKey(i), PrimaryKey(i));
        });

        var parkedInside = subject != Subject.InMemory
            ? parked.WaitUntilParked(TimeSpan.FromSeconds(30))
            : WaitForInMemoryWriter(first);

        var secondWriterGotIn = false;
        BackgroundRun? second = null;

        if (withSecondWriter)
        {
            second = RunOnBackgroundThread(() =>
                index.Add(IndexKey(SECOND_WRITER_KEY), PrimaryKey(SECOND_WRITER_KEY)));

            // Thread-start latency must not be part of the measurement, or a store that does not
            // serialise would look as if it did.
            Assert.That(second.Started.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the second writer's thread never started, so nothing was measured");

            // Recorded, not asserted on - see Outcome.SecondWriterFinishedWhileFirstWasParked for
            // why the allocator's lock makes this observation say less than it appears to.
            secondWriterGotIn = second.Wait(TimeSpan.FromSeconds(2));
        }

        parked.Release();

        first.Wait(TimeSpan.FromSeconds(60));
        second?.Wait(TimeSpan.FromSeconds(60));

        var missing = new List<int>();

        // Read the index back through its own API. A writer that threw is damage too, but the
        // surviving entries are still counted, because "how much was lost" is the interesting half.
        for (int i = 0; i < PARKED_WRITER_ENTRIES; i++)
        {
            if (!SafeContains(index, i, i))
                missing.Add(i);
        }

        if (withSecondWriter && !SafeContains(index, SECOND_WRITER_KEY, SECOND_WRITER_KEY))
            missing.Add(SECOND_WRITER_KEY);

        if (!parkedInside && subject != Subject.InMemory)
            Assert.Fail("the first writer never parked inside a split, so nothing was measured");

        return new Outcome
        {
            ParkedOn = parked.ParkedOn,
            SecondWriterFinishedWhileFirstWasParked = secondWriterGotIn,
            FirstWriterError = first.Error,
            SecondWriterError = second?.Error,
            MissingKeys = missing
        };
    }

    /// <summary>
    /// The in-memory store makes no storage calls, so there is nothing to park it on; the probe over
    /// it is a survival check rather than an interleaving one and only needs the writer running.
    /// </summary>
    private static bool WaitForInMemoryWriter(BackgroundRun first) =>
        first.Started.Wait(TimeSpan.FromSeconds(30));

    private static bool SafeContains(ISecondaryIndex index, int indexKey, long rowId)
    {
        try
        {
            return index.ContainsEntry(IndexKey(indexKey), PrimaryKey(rowId));
        }
        catch (Exception e)
        {
            // A corrupted tree can throw on the way out as well as on the way in; that is a missing
            // entry as far as a consumer is concerned.
            TestContext.Out.WriteLine($"PROBE    lookup of {indexKey} threw {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    private static IKeyValueStore StoreBehind(ISecondaryIndex index)
    {
        var field = typeof(SecondaryIndexKeyValueStore)
            .GetField("m_store", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null, "SecondaryIndexKeyValueStore.m_store was renamed - update the probe");

        var store = field!.GetValue(index) as IKeyValueStore;
        Assert.That(store, Is.Not.Null, "the index has no key-value store behind it");

        return store!;
    }

    private static byte[] IndexKey(int value)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(key, value);
        return key;
    }

    private static byte[] PrimaryKey(long rowId)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(key, rowId);
        return key;
    }

    private static void Report(string question, string subject, object observed) =>
        TestContext.Out.WriteLine($"PROBE  {question}  ->  {subject} = {observed}");

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
                // An unhandled exception on a background thread ends the process and takes every
                // other test with it, so it is captured and asserted on instead.
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

    private readonly record struct StorageCall(StorageCallKind Kind, long Page);

    /// <summary>
    /// Storage decorator that records the calls made through it.
    /// </summary>
    private class RecordingStorage : IStorage
    {
        private readonly IStorage m_inner;
        private readonly Lock m_lock = new();

        public RecordingStorage(IStorage inner) => m_inner = inner;

        /// <summary>Recording is off until a probe turns it on, so construction is not counted.</summary>
        public bool Record { get; set; }

        public List<StorageCall> Calls { get; } = new();

        protected virtual void OnCall(StorageCallKind kind, long page)
        {
            if (!Record)
                return;

            lock (m_lock)
                Calls.Add(new StorageCall(kind, page));
        }

        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            OnCall(StorageCallKind.ReadPage, pageNumber);
            m_inner.ReadPage(pageNumber, buffer);
        }

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.ReadPage, pageNumber);
            return m_inner.ReadPageAsync(pageNumber, buffer, cancellationToken);
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            OnCall(StorageCallKind.WritePage, pageNumber);
            m_inner.WritePage(pageNumber, buffer);
        }

        public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.WritePage, pageNumber);
            return m_inner.WritePageAsync(pageNumber, buffer, cancellationToken);
        }

        public void Flush()
        {
            OnCall(StorageCallKind.Flush, -1);
            m_inner.Flush();
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            OnCall(StorageCallKind.Flush, -1);
            return m_inner.FlushAsync(cancellationToken);
        }

        public void SetSize(long pageCount)
        {
            OnCall(StorageCallKind.SetSize, pageCount);
            m_inner.SetSize(pageCount);
        }

        public int PageSize => m_inner.PageSize;

        public long PageCount => m_inner.PageCount;

        public bool IsReadOnly => m_inner.IsReadOnly;

        public string ProviderKey => m_inner.ProviderKey;

        public void Dispose() => m_inner.Dispose();
    }

    /// <summary>
    /// Storage decorator that parks one designated thread on its next call and holds it there until
    /// released, so "a second writer entered while the first was mid-split" is an exact observation
    /// rather than a lucky interleaving.
    /// </summary>
    private sealed class ParkingStorage : RecordingStorage
    {
        private readonly ManualResetEventSlim m_release = new(false);
        private readonly ManualResetEventSlim m_parked = new(false);

        private int m_armedThreadId;

        public ParkingStorage(IStorage inner) : base(inner)
        {
        }

        /// <summary>What the parked thread was doing when it parked - the control on the seam.</summary>
        public StorageCallKind? ParkedOn { get; private set; }

        /// <summary>Parks the calling thread on the next storage call it makes.</summary>
        public void ArmForThisThread() => Volatile.Write(ref m_armedThreadId, Environment.CurrentManagedThreadId);

        public bool WaitUntilParked(TimeSpan timeout) => m_parked.Wait(timeout);

        public void Release() => m_release.Set();

        protected override void OnCall(StorageCallKind kind, long page)
        {
            base.OnCall(kind, page);

            if (Volatile.Read(ref m_armedThreadId) != Environment.CurrentManagedThreadId)
                return;

            // Disarm first: the parked thread will make more calls after it is released, and the
            // probe is about one interleaving, not about stopping the writer for ever.
            Volatile.Write(ref m_armedThreadId, 0);

            ParkedOn = kind;
            m_parked.Set();
            m_release.Wait(TimeSpan.FromSeconds(60));
        }
    }

    #endregion
}
