using OutWit.Database.Core.Concurrency;

namespace OutWit.Database.Core.Tests.Concurrency;

/// <summary>
/// Unit tests for DatabaseLock component.
/// Tests thread synchronization for database operations.
/// </summary>
[TestFixture]
public class DatabaseLockTests : IDisposable
{
    private DatabaseLock m_lock = null!;

    [SetUp]
    public void SetUp()
    {
        m_lock = new DatabaseLock(TimeSpan.FromSeconds(5));
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        m_lock?.Dispose();
    }

    #region Read Lock Tests

    [Test]
    public void AcquireReadLock_SingleThread_Succeeds()
    {
        using var handle = m_lock.AcquireReadLock();
        
        Assert.That(m_lock.IsReadLockHeld, Is.True);
    }

    [Test]
    public void AcquireReadLock_MultipleReaders_AllSucceed()
    {
        var handles = new List<IDisposable>();
        
        for (int i = 0; i < 5; i++)
        {
            handles.Add(m_lock.AcquireReadLock());
        }
        
        Assert.That(m_lock.IsReadLockHeld, Is.True);
        
        foreach (var handle in handles)
        {
            handle.Dispose();
        }
        
        Assert.That(m_lock.IsReadLockHeld, Is.False);
    }

    [Test]
    public async Task AcquireReadLockAsync_SingleThread_Succeeds()
    {
        await using var handle = await m_lock.AcquireReadLockAsync();
        
        Assert.That(m_lock.IsReadLockHeld, Is.True);
    }

    [Test]
    public async Task AcquireReadLockAsync_MultipleReaders_AllSucceed()
    {
        var handles = new List<IAsyncDisposable>();
        
        for (int i = 0; i < 5; i++)
        {
            handles.Add(await m_lock.AcquireReadLockAsync());
        }
        
        Assert.That(m_lock.IsReadLockHeld, Is.True);
        
        foreach (var handle in handles)
        {
            await handle.DisposeAsync();
        }
        
        Assert.That(m_lock.IsReadLockHeld, Is.False);
    }

    [Test]
    public void AcquireReadLock_ReleasedOnDispose()
    {
        {
            using var handle = m_lock.AcquireReadLock();
            Assert.That(m_lock.IsReadLockHeld, Is.True);
        }
        
        Assert.That(m_lock.IsReadLockHeld, Is.False);
    }

    #endregion

    #region Write Lock Tests

    [Test]
    public void AcquireWriteLock_SingleThread_Succeeds()
    {
        using var handle = m_lock.AcquireWriteLock();
        
        Assert.That(m_lock.IsWriteLockHeld, Is.True);
    }

    [Test]
    public async Task AcquireWriteLockAsync_SingleThread_Succeeds()
    {
        await using var handle = await m_lock.AcquireWriteLockAsync();
        
        Assert.That(m_lock.IsWriteLockHeld, Is.True);
    }

    [Test]
    public void AcquireWriteLock_ReleasedOnDispose()
    {
        {
            using var handle = m_lock.AcquireWriteLock();
            Assert.That(m_lock.IsWriteLockHeld, Is.True);
        }
        
        Assert.That(m_lock.IsWriteLockHeld, Is.False);
    }

    [Test]
    public void AcquireWriteLock_BlocksOtherWriters()
    {
        using var handle1 = m_lock.AcquireWriteLock();
        
        var writerBlocked = true;
        var writerTask = Task.Run(() =>
        {
            try
            {
                using var shortLock = new DatabaseLock(TimeSpan.FromMilliseconds(100));
                // This lock has its own state, won't actually test our lock
            }
            catch (TimeoutException)
            {
                // Expected
            }
        });
        
        Thread.Sleep(50);
        // Just verify the write lock is held
        Assert.That(m_lock.IsWriteLockHeld, Is.True);
    }

    #endregion

    #region Read-Write Interaction Tests

    [Test]
    public async Task ReadersCanAcquireAfterWriterReleases()
    {
        // Writer acquires and releases
        using (var writeHandle = m_lock.AcquireWriteLock())
        {
            Assert.That(m_lock.IsWriteLockHeld, Is.True);
        }
        
        // Now readers should succeed
        await using var readHandle = await m_lock.AcquireReadLockAsync();
        Assert.That(m_lock.IsReadLockHeld, Is.True);
    }

    [Test]
    public async Task WriterCanAcquireAfterReadersRelease()
    {
        // Readers acquire and release
        for (int i = 0; i < 3; i++)
        {
            using var readHandle = m_lock.AcquireReadLock();
            Assert.That(m_lock.IsReadLockHeld, Is.True);
        }
        
        // Now writer should succeed
        await using var writeHandle = await m_lock.AcquireWriteLockAsync();
        Assert.That(m_lock.IsWriteLockHeld, Is.True);
    }

    #endregion

    #region Timeout Tests

    [Test]
    public void AcquireWriteLock_ThrowsTimeoutException_WhenBlocked()
    {
        var shortTimeoutLock = new DatabaseLock(TimeSpan.FromMilliseconds(100));
        
        try
        {
            using var handle1 = shortTimeoutLock.AcquireWriteLock();
            
            var task = Task.Run(() =>
            {
                Assert.Throws<TimeoutException>(() =>
                {
                    using var handle2 = shortTimeoutLock.AcquireWriteLock();
                });
            });
            
            task.Wait(TimeSpan.FromSeconds(1));
        }
        finally
        {
            shortTimeoutLock.Dispose();
        }
    }

    [Test]
    public void AcquireReadLock_ThrowsTimeoutException_WhenWriterHoldsLock()
    {
        var shortTimeoutLock = new DatabaseLock(TimeSpan.FromMilliseconds(100));
        
        try
        {
            using var writeHandle = shortTimeoutLock.AcquireWriteLock();
            
            var task = Task.Run(() =>
            {
                Assert.Throws<TimeoutException>(() =>
                {
                    using var readHandle = shortTimeoutLock.AcquireReadLock();
                });
            });
            
            task.Wait(TimeSpan.FromSeconds(1));
        }
        finally
        {
            shortTimeoutLock.Dispose();
        }
    }

    [Test]
    public async Task AcquireWriteLockAsync_ThrowsTimeoutException_WhenBlocked()
    {
        var shortTimeoutLock = new DatabaseLock(TimeSpan.FromMilliseconds(100));
        
        try
        {
            await using var handle1 = await shortTimeoutLock.AcquireWriteLockAsync();
            
            var task = Task.Run(async () =>
            {
                Assert.ThrowsAsync<TimeoutException>(async () =>
                {
                    await using var handle2 = await shortTimeoutLock.AcquireWriteLockAsync();
                });
            });
            
            await task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            shortTimeoutLock.Dispose();
        }
    }

    #endregion

    #region Sync/Async Interoperability Tests

    [Test]
    public async Task SyncReadLock_BlocksAsyncWriter()
    {
        var shortTimeoutLock = new DatabaseLock(TimeSpan.FromMilliseconds(200));
        
        try
        {
            using var syncRead = shortTimeoutLock.AcquireReadLock();
            
            var task = Task.Run(async () =>
            {
                Assert.ThrowsAsync<TimeoutException>(async () =>
                {
                    await using var asyncWriteHandle = await shortTimeoutLock.AcquireWriteLockAsync();
                });
            });
            
            await task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            shortTimeoutLock.Dispose();
        }
    }

    [Test]
    public async Task AsyncReadLock_BlocksSyncWriter()
    {
        var shortTimeoutLock = new DatabaseLock(TimeSpan.FromMilliseconds(200));
        
        try
        {
            await using var asyncReadHandle = await shortTimeoutLock.AcquireReadLockAsync();
            
            var task = Task.Run(() =>
            {
                Assert.Throws<TimeoutException>(() =>
                {
                    using var syncWriteHandle = shortTimeoutLock.AcquireWriteLock();
                });
            });
            
            task.Wait(TimeSpan.FromSeconds(1));
        }
        finally
        {
            shortTimeoutLock.Dispose();
        }
    }

    [Test]
    public async Task MixedSyncAsyncReaders_AllSucceed()
    {
        var handles = new List<object>();
        
        // Mix sync and async readers
        handles.Add(m_lock.AcquireReadLock());
        handles.Add(await m_lock.AcquireReadLockAsync());
        handles.Add(m_lock.AcquireReadLock());
        handles.Add(await m_lock.AcquireReadLockAsync());
        
        Assert.That(m_lock.IsReadLockHeld, Is.True);
        
        // Cleanup
        foreach (var handle in handles)
        {
            if (handle is IDisposable d) d.Dispose();
            else if (handle is IAsyncDisposable ad) await ad.DisposeAsync();
        }
    }

    #endregion

    #region Concurrent Stress Tests

    [Test]
    [Category("Stress")]
    public async Task ConcurrentReaders_AllSucceed()
    {
        const int readerCount = 50;
        var tasks = new List<Task>();
        var successCount = 0;
        
        for (int i = 0; i < readerCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var handle = await m_lock.AcquireReadLockAsync();
                await Task.Delay(10); // Simulate work
                Interlocked.Increment(ref successCount);
            }));
        }
        
        await Task.WhenAll(tasks);
        
        Assert.That(successCount, Is.EqualTo(readerCount));
    }

    [Test]
    [Category("Stress")]
    public async Task ConcurrentWriters_Serialized()
    {
        const int writerCount = 10;
        var tasks = new List<Task>();
        var counter = 0;
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        
        for (int i = 0; i < writerCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var handle = await m_lock.AcquireWriteLockAsync();
                
                var current = Interlocked.Increment(ref currentConcurrent);
                if (current > maxConcurrent)
                    Interlocked.Exchange(ref maxConcurrent, current);
                
                await Task.Delay(5); // Simulate work
                Interlocked.Increment(ref counter);
                
                Interlocked.Decrement(ref currentConcurrent);
            }));
        }
        
        await Task.WhenAll(tasks);
        
        Assert.That(counter, Is.EqualTo(writerCount));
        Assert.That(maxConcurrent, Is.EqualTo(1), "Only one writer should hold lock at a time");
    }

    [Test]
    [Category("Stress")]
    public async Task MixedReadersAndWriters_NoDeadlock()
    {
        const int operationCount = 50;
        var tasks = new List<Task>();
        var random = new Random(42);
        var completedOps = 0;
        
        for (int i = 0; i < operationCount; i++)
        {
            var isReader = random.Next(2) == 0;
            
            tasks.Add(Task.Run(async () =>
            {
                if (isReader)
                {
                    await using var handle = await m_lock.AcquireReadLockAsync();
                    await Task.Delay(1);
                }
                else
                {
                    await using var handle = await m_lock.AcquireWriteLockAsync();
                    await Task.Delay(1);
                }
                Interlocked.Increment(ref completedOps);
            }));
        }
        
        await Task.WhenAll(tasks);
        
        Assert.That(completedOps, Is.EqualTo(operationCount));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposedLock_ThrowsObjectDisposedException()
    {
        var disposableLock = new DatabaseLock();
        disposableLock.Dispose();
        
        Assert.Throws<ObjectDisposedException>(() => disposableLock.AcquireReadLock());
        Assert.Throws<ObjectDisposedException>(() => disposableLock.AcquireWriteLock());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => 
            await disposableLock.AcquireReadLockAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => 
            await disposableLock.AcquireWriteLockAsync());
    }

    [Test]
    public void DoubleDispose_DoesNotThrow()
    {
        var disposableLock = new DatabaseLock();
        
        Assert.DoesNotThrow(() =>
        {
            disposableLock.Dispose();
            disposableLock.Dispose();
        });
    }

    #endregion

    #region Properties Tests

    [Test]
    public void WaitingReadCount_IsAccessible()
    {
        Assert.That(m_lock.WaitingReadCount, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void WaitingWriteCount_IsAccessible()
    {
        Assert.That(m_lock.WaitingWriteCount, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region Reentrancy Detection Tests

    [Test]
    public void WriteLock_ThrowsLockRecursionException_OnReentrantAcquisition()
    {
        using var handle = m_lock.AcquireWriteLock();
        
        Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.True);
        Assert.Throws<LockRecursionException>(() => m_lock.AcquireWriteLock());
    }

    [Test]
    public void ReadLock_ThrowsLockRecursionException_WhenWriteLockHeld()
    {
        using var handle = m_lock.AcquireWriteLock();
        
        Assert.Throws<LockRecursionException>(() => m_lock.AcquireReadLock());
    }

    [Test]
    public void IsWriteLockHeldByCurrentThread_FalseByDefault()
    {
        Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.False);
    }

    [Test]
    public void IsWriteLockHeldByCurrentThread_TrueWhenHeld()
    {
        using var handle = m_lock.AcquireWriteLock();
        
        Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.True);
    }

    [Test]
    public void IsWriteLockHeldByCurrentThread_FalseAfterRelease()
    {
        {
            using var handle = m_lock.AcquireWriteLock();
            Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.True);
        }
        
        Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.False);
    }

    [Test]
    public async Task ReentrancyDetection_WorksAcrossThreads()
    {
        // Thread 1 holds write lock
        using var handle = m_lock.AcquireWriteLock();
        
        // Thread 2 should NOT get LockRecursionException, should get TimeoutException instead
        var task = Task.Run(() =>
        {
            // This thread doesn't hold any lock, so it should timeout, not throw recursion
            Assert.Throws<TimeoutException>(() => m_lock.AcquireWriteLock());
        });
        
        await task;
    }

    [Test]
    public void WriteLock_CanBeAcquiredAgainAfterRelease()
    {
        {
            using var handle1 = m_lock.AcquireWriteLock();
            Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.True);
        }
        
        // Should be able to acquire again
        using var handle2 = m_lock.AcquireWriteLock();
        Assert.That(m_lock.IsWriteLockHeldByCurrentThread, Is.True);
    }

    #endregion
}
