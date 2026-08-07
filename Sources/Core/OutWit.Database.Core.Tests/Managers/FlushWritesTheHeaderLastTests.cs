using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Pages;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Managers;

/// <summary>
/// The header goes to the disk LAST, and only after the pages it counts are durable.
/// </summary>
/// <remarks>
/// <para>
/// <b>KnownIssues issue 10.</b> The header carries <c>TotalPageCount</c> and the head of the free
/// list, so it is the file's account of itself. <c>Flush</c> used to write it FIRST and the pages
/// after, which is the unsafe order: an interruption in the middle leaves a header promising pages
/// that never reached the disk, and the next open says <i>"Page number N is out of range"</i> - the
/// message both of issue 10's casualties gave.
/// </para>
/// <para>
/// Written last, the same interruption can only leave a header OLDER than the pages. That is not a
/// guarantee of consistency - a page evicted since the last flush still makes the two disagree, and
/// closing that window needs a journal the MVCC store cannot currently have - but it removes the one
/// case the engine was manufacturing for itself.
/// </para>
/// <para>
/// <b>The order is asserted on the storage, not read out of the method.</b> A test that called
/// <c>Flush</c> and then inspected the file could not tell the two orders apart: both end with the
/// same bytes. Only the sequence of calls distinguishes them, so the sequence is what is recorded.
/// </para>
/// <para>
/// <b>And the flush BETWEEN them is asserted too</b>, which is the half that is easy to leave out.
/// Writing the header after the pages in this method means nothing if both are still sitting in the
/// operating system's cache when the power goes: the order would exist in the source and not on the
/// disk. Measured by reverting: with the pre-fix ordering both cases below go red, and with the
/// intermediate flush removed the second one alone goes red.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FlushWritesTheHeaderLastTests
{
    #region Types

    /// <summary>
    /// Records the order of what reaches the storage. Everything is passed through, so the page
    /// manager under it behaves exactly as it does in a real database.
    /// </summary>
    private sealed class RecordingStorage(IStorage inner) : IStorage
    {
        private readonly List<string> m_log = [];

        /// <summary>Writes and flushes in the order they happened, as "page:N" and "flush".</summary>
        public IReadOnlyList<string> Log => m_log;

        public void Clear() => m_log.Clear();

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            m_log.Add($"page:{pageNumber}");
            inner.WritePage(pageNumber, buffer);
        }

        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            m_log.Add($"page:{pageNumber}");
            await inner.WritePageAsync(pageNumber, buffer, cancellationToken).ConfigureAwait(false);
        }

        public void Flush()
        {
            m_log.Add("flush");
            inner.Flush();
        }

        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            m_log.Add("flush");
            await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ReadPage(long pageNumber, Span<byte> buffer) => inner.ReadPage(pageNumber, buffer);

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadPageAsync(pageNumber, buffer, cancellationToken);

        public void SetSize(long pageCount) => inner.SetSize(pageCount);

        public ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default) =>
            inner.SetSizeAsync(pageCount, cancellationToken);

        public int PageSize => inner.PageSize;

        public long PageCount => inner.PageCount;

        public bool IsReadOnly => inner.IsReadOnly;

        public string ProviderKey => inner.ProviderKey;

        public void Dispose() => inner.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public void FlushWritesTheHeaderAfterThePagesTest()
    {
        using var storage = new RecordingStorage(new StorageMemory(initialPageCount: 0));
        using var pageManager = new PageManager(storage, new PageCacheLru(storage, 100));

        DirtySomePages(pageManager);

        // Only what the flush itself does is the subject; creating the database writes a header too.
        storage.Clear();

        pageManager.Flush();

        AssertHeaderIsLast(storage.Log);
    }

    [Test]
    public async Task FlushAsyncWritesTheHeaderAfterThePagesTest()
    {
        using var storage = new RecordingStorage(new StorageMemory(initialPageCount: 0));
        using var pageManager = new PageManager(storage, new PageCacheLru(storage, 100));

        DirtySomePages(pageManager);

        storage.Clear();

        await pageManager.FlushAsync();

        AssertHeaderIsLast(storage.Log);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Allocates pages and marks them dirty, so the flush has both a header and pages to write. An
    /// allocation also moves <c>TotalPageCount</c>, which is what makes the header dirty in the first
    /// place - without it the flush would have no header to write and the case would pass vacuously.
    /// </summary>
    private static void DirtySomePages(PageManager pageManager)
    {
        for (var i = 0; i < 4; i++)
        {
            var (pageNumber, _) = pageManager.AllocatePage(PageType.Leaf);

            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
        }
    }

    private static void AssertHeaderIsLast(IReadOnlyList<string> log)
    {
        TestContext.Out.WriteLine($"storage saw: {string.Join(" -> ", log)}");

        var header = log.ToList().FindLastIndex(entry => entry == "page:0");
        var lastPage = log.ToList().FindLastIndex(entry => entry.StartsWith("page:") && entry != "page:0");

        Assert.Multiple(() =>
        {
            // CONTROL: with neither of these the assertions below are about an empty list, and
            // "the header came last" and "nothing was written" look identical.
            Assert.That(header, Is.GreaterThanOrEqualTo(0),
                "CONTROL: the flush wrote no header at all, so this case is guarding nothing");

            Assert.That(lastPage, Is.GreaterThanOrEqualTo(0),
                "CONTROL: the flush wrote no data page at all, so there is no order to check");

            Assert.That(header, Is.GreaterThan(lastPage),
                "the header was written BEFORE the pages it counts. An interruption between the two "
                + "leaves a file whose header promises more pages than the file holds, which is "
                + "unreadable rather than merely out of date.");

            Assert.That(log.Skip(lastPage + 1).Take(header - lastPage - 1), Does.Contain("flush"),
                "the pages were not made durable before the header was written. The order then exists "
                + "in the source and not on the disk: the operating system is free to write the header "
                + "first anyway.");
        });
    }

    #endregion
}
