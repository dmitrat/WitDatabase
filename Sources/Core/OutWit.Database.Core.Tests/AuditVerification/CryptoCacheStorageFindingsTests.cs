using System.Diagnostics;
using System.Security.Cryptography;
using NUnit.Framework;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>core-crypto-cache-storage</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>See Docs/NEXT-SESSION-PLAN.md workstream B.</remarks>
[TestFixture]
[Category("AuditVerification")]
public class CryptoCacheStorageFindingsTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    #region Zeroed and rolled-back pages bypass AEAD authentication

    [Test]
    [Ignore("CONFIRMED 2026-07-27: no exception - the zeroed ciphertext reads back as a page of zeros. "
            + "ReadPage tests IsAllZeros BEFORE decrypting and returns early, so authentication is "
            + "skipped for exactly the shape it exists to catch: a wiped sector or a truncated write "
            + "is indistinguishable from a page that was never written. "
            + "core-crypto-cache-storage, Core/Storage/StorageEncrypted.cs:78")]
    public void ZeroedPageIsRejectedRatherThanReadAsZerosTest()
    {
        // Finding: StorageEncrypted.cs:78 - ReadPage checks IsAllZeros(encrypted) *before*
        // decrypting and returns a zero-filled buffer, so a page an attacker or a failing disk has
        // zeroed is indistinguishable from a page that was never written. Authentication is skipped
        // for exactly the shape it exists to catch.
        var inner = new StorageMemory(PageSize + Overhead, 64);
        using var storage = CreateEncrypted(inner);

        var page = new byte[PageSize];
        page.AsSpan().Fill(0xAB);
        storage.WritePage(1, page);

        // Zero the ciphertext the way a truncating write or a wiped sector would.
        inner.WritePage(1, new byte[PageSize + Overhead]);

        var readBack = new byte[PageSize];
        Assert.That(() => storage.ReadPage(1, readBack), Throws.InstanceOf<CryptographicException>(),
            "a zeroed ciphertext must fail authentication, not read back as a page of zeros");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the superseded ciphertext re-authenticated and read back as 0x11. No "
            + "version or counter is bound into the AEAD, so an older ciphertext of the SAME page is "
            + "accepted as current - a single page can be reverted with no trace. Note the narrower "
            + "scope though: PageSwapIsRejectedTest passes, so the page number IS bound as AAD and "
            + "cross-page substitution is caught. The finding's \"no AAD/version binding\" overstates "
            + "it; the gap is version binding alone.")]
    public void RolledBackPageIsRejectedTest()
    {
        // The other half: no version or counter is bound into the AEAD, so an *older ciphertext of
        // the same page* re-authenticates perfectly. An attacker with file access - or a botched
        // restore - can revert a single page and leave no trace.
        var inner = new StorageMemory(PageSize + Overhead, 64);
        using var storage = CreateEncrypted(inner);

        var first = new byte[PageSize];
        first.AsSpan().Fill(0x11);
        storage.WritePage(1, first);

        var oldCiphertext = new byte[PageSize + Overhead];
        inner.ReadPage(1, oldCiphertext);

        var second = new byte[PageSize];
        second.AsSpan().Fill(0x22);
        storage.WritePage(1, second);

        // Replay the earlier ciphertext for the same page.
        inner.WritePage(1, oldCiphertext);

        var readBack = new byte[PageSize];
        storage.ReadPage(1, readBack);

        Assert.That(readBack[0], Is.Not.EqualTo(0x11),
            "a superseded version of this page must not authenticate as the current one");
    }

    [Test]
    public void PageSwapIsRejectedTest()
    {
        // PASSES - and that is the point. Control for the claim above: the page number IS passed to Decrypt, so moving a ciphertext
        // to a different page number should be caught. If this passes, the AAD binding exists and
        // the gap is specifically the missing *version*, which is a narrower claim than "no AAD".
        var inner = new StorageMemory(PageSize + Overhead, 64);
        using var storage = CreateEncrypted(inner);

        var one = new byte[PageSize];
        one.AsSpan().Fill(0x11);
        storage.WritePage(1, one);

        var two = new byte[PageSize];
        two.AsSpan().Fill(0x22);
        storage.WritePage(2, two);

        var cipherOne = new byte[PageSize + Overhead];
        inner.ReadPage(1, cipherOne);
        inner.WritePage(2, cipherOne);

        var readBack = new byte[PageSize];
        Assert.That(() => storage.ReadPage(2, readBack), Throws.InstanceOf<CryptographicException>(),
            "page 1's ciphertext must not authenticate as page 2");
    }

    #endregion

    #region Page cache guards the same state with two different locks

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the synchronous CreatePage proceeded after 0 ms while an async flush "
            + "provably held the shard - there is no mutual exclusion between the two APIs at all. "
            + "core-crypto-cache-storage, Core/Cache/PageCacheShardedClock.cs:36")]
    public void SyncCacheOperationWaitsForAnInFlightAsyncOneTest()
    {
        // Finding: PageCacheShardedClock.cs:36 - each shard holds BOTH a `Lock m_lock` and a
        // `SemaphoreSlim m_asyncLock`, and they guard the same m_pages / m_pageIndex / m_count /
        // m_clockHand. Two locks over one piece of mutable state means the sync and async APIs do
        // not exclude each other at all.
        //
        // Deterministic, no race: the storage double parks inside WritePageAsync, so the async flush
        // is provably still in flight when the synchronous call below runs. If one lock guarded the
        // state, that call would block until the flush finished.
        using var storage = new BlockingStorage(PageSize, 32);
        using var cache = new PageCacheShardedClock(storage, maxPages: 8, shardCount: 1);

        var page = cache.CreatePage(1);
        page.Data.Fill(0xAB);
        cache.MarkDirty(1);

        var flush = cache.FlushAllAsync().AsTask();
        Assert.That(storage.WriteEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        var sw = Stopwatch.StartNew();
        var entered = Task.Run(() => cache.CreatePage(2)).Wait(TimeSpan.FromSeconds(2));
        sw.Stop();

        storage.ReleaseWrite.Set();
        flush.Wait(TimeSpan.FromSeconds(10));

        TestContext.Out.WriteLine(
            $"sync CreatePage while an async flush held the shard: " +
            $"{(entered ? $"proceeded after {sw.ElapsedMilliseconds} ms" : "blocked, as it should")}");

        Assert.That(entered, Is.False,
            "a synchronous cache operation must not mutate shard state while an async one holds it");
    }

    #endregion

    #region Helpers

    private const int PageSize = 4096;
    private const int Overhead = 28;

    private StorageEncrypted CreateEncrypted(IStorage inner)
    {
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        return new StorageEncrypted(inner, encryptor);
    }

    /// <summary>
    /// An <see cref="IStorage"/> whose asynchronous write parks until released, so a test can act
    /// while a write is provably in flight.
    /// </summary>
    private sealed class BlockingStorage : IStorage
    {
        private readonly byte[][] m_pages;

        public BlockingStorage(int pageSize, int pageCount)
        {
            PageSize = pageSize;
            m_pages = Enumerable.Range(0, pageCount).Select(_ => new byte[pageSize]).ToArray();
        }

        public ManualResetEventSlim WriteEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseWrite { get; } = new(false);

        public void ReadPage(long pageNumber, Span<byte> buffer) => m_pages[pageNumber].CopyTo(buffer);

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadPage(pageNumber, buffer.Span);
            return ValueTask.CompletedTask;
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer) =>
            buffer[..PageSize].CopyTo(m_pages[pageNumber]);

        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteEntered.Set();
            await Task.Run(() => ReleaseWrite.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
            buffer.Span[..PageSize].CopyTo(m_pages[pageNumber]);
        }

        public void Flush() { }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void SetSize(long pageCount) { }

        public int PageSize { get; }
        public long PageCount => m_pages.Length;
        public bool IsReadOnly => false;
        public string ProviderKey => "blocking-test";

        public void Dispose()
        {
            WriteEntered.Dispose();
            ReleaseWrite.Dispose();
        }
    }

    #endregion
}
