using OutWit.Database.Core.Concurrency;

namespace OutWit.Database.Core.Tests.Concurrency;

/// <summary>
/// <see cref="AsyncReaderWriterLock"/> - the lock that may be held across an <c>await</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is <b>deterministic</b>: a collaborator is parked on a
/// <see cref="TaskCompletionSource"/> and the OTHER party is timed against a bounded wait, so no case
/// depends on a scheduler racing the way it did when the test was written. See the project's rule on
/// verifying concurrency deterministically.
/// </para>
/// <para>
/// <b>The control is built in.</b> Several cases are run twice - once against this lock and once
/// against <see cref="ReaderWriterLockSlim"/>, the type it replaces - because the property that
/// matters (a hold released on a different thread than took it) is precisely the one
/// <c>ReaderWriterLockSlim</c> does not have. A test that only passed against the new type would not
/// distinguish "this lock works" from "this scenario never crossed a thread".
/// </para>
/// </remarks>
[TestFixture]
public class AsyncReaderWriterLockTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromMilliseconds(500);

    #region Shared mode

    [Test]
    public void TwoReadersHoldTheLockAtTheSameTimeTest()
    {
        using var sut = new AsyncReaderWriterLock();

        sut.EnterRead();
        sut.EnterRead();

        // Asserted on the STATE rather than on timing: both holds are in, neither has been released.
        Assert.That(sut.CurrentReaderCount, Is.EqualTo(2),
            "shared mode must admit several holders at once, or every read serialises");

        sut.ExitRead();
        sut.ExitRead();

        Assert.That(sut.CurrentReaderCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AReaderDoesNotWaitForAnotherReaderTest()
    {
        using var sut = new AsyncReaderWriterLock();

        sut.EnterRead();

        var second = Task.Run(async () =>
        {
            await sut.EnterReadAsync();
            sut.ExitRead();
        });

        Assert.That(await Task.WhenAny(second, Task.Delay(Bounded)), Is.SameAs(second),
            "a second reader waited for the first, so this is not a shared mode at all");

        sut.ExitRead();
    }

    #endregion

    #region Exclusion

    [Test]
    public async Task AWriterWaitsForAnOpenReaderTest()
    {
        using var sut = new AsyncReaderWriterLock();

        sut.EnterRead();

        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await sut.EnterWriteAsync();
            writerEntered.SetResult();
            sut.ExitWrite();
        });

        // The reader is parked - deliberately still holding - so the writer MUST NOT get in.
        Assert.That(await Task.WhenAny(writerEntered.Task, Task.Delay(Bounded)), Is.Not.SameAs(writerEntered.Task),
            "a writer entered while a reader was still holding the lock");

        sut.ExitRead();

        // And once the reader leaves, the writer must get in - otherwise this case would pass for a
        // lock that simply never admits writers.
        Assert.That(await Task.WhenAny(writerEntered.Task, Task.Delay(Bounded)), Is.SameAs(writerEntered.Task),
            "the writer never entered after the reader left");

        await writer;
    }

    [Test]
    public async Task AReaderWaitsForAnOpenWriterTest()
    {
        using var sut = new AsyncReaderWriterLock();

        sut.EnterWrite();

        var readerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            await sut.EnterReadAsync();
            readerEntered.SetResult();
            sut.ExitRead();
        });

        Assert.That(await Task.WhenAny(readerEntered.Task, Task.Delay(Bounded)), Is.Not.SameAs(readerEntered.Task),
            "a reader entered while a writer was holding the lock");

        sut.ExitWrite();

        Assert.That(await Task.WhenAny(readerEntered.Task, Task.Delay(Bounded)), Is.SameAs(readerEntered.Task),
            "the reader never entered after the writer left");

        await reader;
    }

    [Test]
    public async Task TwoWritersDoNotOverlapTest()
    {
        using var sut = new AsyncReaderWriterLock();

        await sut.EnterWriteAsync();

        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Run(async () =>
        {
            await sut.EnterWriteAsync();
            secondEntered.SetResult();
            sut.ExitWrite();
        });

        Assert.That(await Task.WhenAny(secondEntered.Task, Task.Delay(Bounded)), Is.Not.SameAs(secondEntered.Task),
            "two writers held the lock at once");

        sut.ExitWrite();

        Assert.That(await Task.WhenAny(secondEntered.Task, Task.Delay(Bounded)), Is.SameAs(secondEntered.Task));

        await second;
    }

    #endregion

    #region The property this type exists for

    /// <summary>
    /// The whole reason the type exists: a hold taken before an <c>await</c> and released after it,
    /// where the continuation resumes on a DIFFERENT thread.
    /// </summary>
    /// <remarks>
    /// The control is <see cref="ReaderWriterLockSlim"/> in the sibling case below. Without it this
    /// case proves nothing, because a continuation that happens to resume on the same thread would
    /// pass against the thread-affine lock too.
    /// </remarks>
    [Test]
    public async Task AWriteHoldSurvivesAThreadChangeAcrossAnAwaitTest()
    {
        using var sut = new AsyncReaderWriterLock();

        var takenOn = Environment.CurrentManagedThreadId;

        await sut.EnterWriteAsync();

        var releasedOn = await ForceAThreadChange(takenOn);

        Assert.That(releasedOn, Is.Not.EqualTo(takenOn),
            "the continuation stayed on the same thread, so this case did not exercise the property "
            + "it was written for - it would pass against a thread-affine lock");

        Assert.That(() => sut.ExitWrite(), Throws.Nothing,
            "releasing from another thread must work - this is the defect the type exists to fix");

        // And the lock must really be free afterwards, not merely quiet about the release.
        Assert.That(async () => await sut.EnterWriteAsync().AsTask().WaitAsync(Bounded), Throws.Nothing,
            "the lock was silently left held by the thread that moved on");

        sut.ExitWrite();
    }

    [Test]
    public async Task AReadHoldSurvivesAThreadChangeAcrossAnAwaitTest()
    {
        using var sut = new AsyncReaderWriterLock();

        var takenOn = Environment.CurrentManagedThreadId;

        await sut.EnterReadAsync();

        var releasedOn = await ForceAThreadChange(takenOn);

        Assert.That(releasedOn, Is.Not.EqualTo(takenOn), "the continuation stayed on the same thread");
        Assert.That(() => sut.ExitRead(), Throws.Nothing);
        Assert.That(sut.CurrentReaderCount, Is.EqualTo(0));

        Assert.That(async () => await sut.EnterWriteAsync().AsTask().WaitAsync(Bounded), Throws.Nothing,
            "a reader's hold outlived its release, so writers wait for ever");

        sut.ExitWrite();
    }

    /// <summary>
    /// THE CONTROL, and it is the reason the two cases above mean anything: the very same shape
    /// against <see cref="ReaderWriterLockSlim"/> - the type being replaced - fails.
    /// </summary>
    /// <remarks>
    /// This is not a test of the .NET framework. It is a test of the SCENARIO: it says that the
    /// arrangement above genuinely crosses a thread boundary, so a lock that gets it right is being
    /// distinguished from a lock that does not. If .NET ever made <c>ReaderWriterLockSlim</c>
    /// thread-agnostic this case would go red and the honest response would be to delete
    /// <see cref="AsyncReaderWriterLock"/>, not to weaken the case.
    /// </remarks>
    [Test]
    public async Task ControlTheThreadAffineLockFailsTheSameScenarioTest()
    {
        using var control = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        var takenOn = Environment.CurrentManagedThreadId;

        control.EnterWriteLock();

        var releasedOn = await ForceAThreadChange(takenOn);

        Assert.That(releasedOn, Is.Not.EqualTo(takenOn), "the scenario did not cross a thread");

        Assert.That(() => control.ExitWriteLock(), Throws.InstanceOf<SynchronizationLockException>(),
            "if this stops throwing, ReaderWriterLockSlim has become thread-agnostic and "
            + "AsyncReaderWriterLock has no reason to exist");
    }

    #endregion

    #region Writer preference

    /// <summary>
    /// A reader arriving AFTER a writer is already waiting queues behind it, so a steady stream of
    /// readers cannot starve a writer.
    /// </summary>
    [Test]
    public async Task AReaderArrivingBehindAWaitingWriterDoesNotOvertakeItTest()
    {
        using var sut = new AsyncReaderWriterLock();

        // A reader is inside, so the writer must wait.
        sut.EnterRead();

        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(async () =>
        {
            await sut.EnterWriteAsync();
            writerEntered.SetResult();
            await writerReleased.Task;
            sut.ExitWrite();
        });

        // Let the writer reach the turnstile. Nothing is asserted on this delay - it only makes the
        // ARRIVAL ORDER the thing under test rather than the thing being raced.
        await Task.Delay(100);

        var lateReaderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateReader = Task.Run(async () =>
        {
            await sut.EnterReadAsync();
            lateReaderEntered.SetResult();
            sut.ExitRead();
        });

        Assert.That(await Task.WhenAny(lateReaderEntered.Task, Task.Delay(Bounded)), Is.Not.SameAs(lateReaderEntered.Task),
            "a reader arriving behind a waiting writer joined the reader group and extended it, which "
            + "is how a writer starves");

        // Release the first reader: the WRITER must be the one that gets in, not the late reader.
        sut.ExitRead();

        Assert.That(await Task.WhenAny(writerEntered.Task, Task.Delay(Bounded)), Is.SameAs(writerEntered.Task),
            "the writer did not get its turn once the original reader left");

        Assert.That(lateReaderEntered.Task.IsCompleted, Is.False,
            "the late reader got in alongside the writer");

        writerReleased.SetResult();

        Assert.That(await Task.WhenAny(lateReaderEntered.Task, Task.Delay(Bounded)), Is.SameAs(lateReaderEntered.Task),
            "the late reader never got in at all");

        await Task.WhenAll(writer, lateReader);
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task ACancelledWaiterDoesNotLeaveTheLockHeldTest()
    {
        using var sut = new AsyncReaderWriterLock();

        await sut.EnterWriteAsync();

        using var cancellation = new CancellationTokenSource();

        var waiter = Task.Run(async () => await sut.EnterWriteAsync(cancellation.Token));

        await Task.Delay(50);
        cancellation.Cancel();

        Assert.That(async () => await waiter, Throws.InstanceOf<OperationCanceledException>());

        sut.ExitWrite();

        // The cancelled waiter must not have taken the turnstile with it.
        Assert.That(async () => await sut.EnterWriteAsync().AsTask().WaitAsync(Bounded), Throws.Nothing,
            "a cancelled waiter left the turnstile held, so the lock is unusable afterwards");

        sut.ExitWrite();
    }

    #endregion

    #region Tools

    /// <summary>
    /// Resumes on a different thread than <paramref name="takenOn"/> and returns the thread THE
    /// CONTINUATION landed on - which is the thread that will call the release, and therefore the only
    /// one this scenario is about. Retries rather than assuming, because the thread pool may hand the
    /// same thread back.
    /// </summary>
    private static async Task<int> ForceAThreadChange(int takenOn)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Run(() => Thread.Sleep(1)).ConfigureAwait(false);

            var landedOn = Environment.CurrentManagedThreadId;
            if (landedOn != takenOn)
                return landedOn;
        }

        return takenOn;
    }

    #endregion
}
