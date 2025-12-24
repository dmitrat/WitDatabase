using NUnit.Framework;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;

namespace OutWit.Database.Core.IndexedDb.Tests;

/// <summary>
/// Tests for IndexedDbInterop.
/// </summary>
[TestFixture]
public class IndexedDbInteropTests
{
    #region Fields

    private MockJSRuntime m_jsRuntime = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_jsRuntime = new MockJSRuntime();
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void ConstructorWithValidParametersSucceedsTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        
        Assert.That(interop.DatabaseName, Is.EqualTo("TestDb"));
    }

    [Test]
    public void ConstructorWithNullJsRuntimeThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new IndexedDbInterop(null!, "TestDb");
        });
    }

    [Test]
    public void ConstructorWithEmptyDatabaseNameThrowsTest()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new IndexedDbInterop(m_jsRuntime, "");
        });
    }

    [Test]
    public void ConstructorWithWhitespaceDatabaseNameThrowsTest()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new IndexedDbInterop(m_jsRuntime, "   ");
        });
    }

    #endregion

    #region Open/Close Tests

    [Test]
    public async Task OpenAsyncCreatesDatabaseTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        
        await interop.OpenAsync();
        
        Assert.That(m_jsRuntime.Databases.ContainsKey("TestDb"), Is.True);
    }

    [Test]
    public async Task CloseAsyncCompletesSuccessfullyTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        await interop.CloseAsync();
        
        // Should not throw
    }

    #endregion

    #region Read/Write Tests

    [Test]
    public async Task WriteAndReadPageAsyncRoundTripTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        await interop.WritePageAsync(0, testData);
        
        var result = await interop.ReadPageAsync(0);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(testData));
    }

    [Test]
    public async Task ReadNonExistentPageReturnsNullTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var result = await interop.ReadPageAsync(999);
        
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task WritePagesAsyncWritesMultiplePagesTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var pages = new[]
        {
            (PageNumber: 0L, Data: new byte[] { 1, 2, 3 }),
            (PageNumber: 1L, Data: new byte[] { 4, 5, 6 }),
            (PageNumber: 2L, Data: new byte[] { 7, 8, 9 })
        };
        
        await interop.WritePagesAsync(pages);
        
        var page0 = await interop.ReadPageAsync(0);
        var page1 = await interop.ReadPageAsync(1);
        var page2 = await interop.ReadPageAsync(2);
        
        Assert.That(page0, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(page1, Is.EqualTo(new byte[] { 4, 5, 6 }));
        Assert.That(page2, Is.EqualTo(new byte[] { 7, 8, 9 }));
    }

    #endregion

    #region Metadata Tests

    [Test]
    public async Task SetAndGetPageCountAsyncTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        await interop.SetPageCountAsync(42);
        var count = await interop.GetPageCountAsync();
        
        Assert.That(count, Is.EqualTo(42));
    }

    [Test]
    public async Task SetAndGetPageSizeAsyncTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        await interop.SetPageSizeAsync(8192);
        var size = await interop.GetPageSizeAsync();
        
        Assert.That(size, Is.EqualTo(8192));
    }

    [Test]
    public async Task GetPageCountReturnsZeroForNewDatabaseTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var count = await interop.GetPageCountAsync();
        
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetPageSizeReturnsZeroForNewDatabaseTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var size = await interop.GetPageSizeAsync();
        
        Assert.That(size, Is.EqualTo(0));
    }

    #endregion

    #region Database Operations Tests

    [Test]
    public async Task DatabaseExistsReturnsFalseForNewDatabaseTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        
        var exists = await interop.DatabaseExistsAsync();
        
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task DatabaseExistsReturnsTrueAfterOpenTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        var exists = await interop.DatabaseExistsAsync();
        
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task DeleteDatabaseAsyncRemovesDatabaseTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        Assert.That(m_jsRuntime.Databases.ContainsKey("TestDb"), Is.True);
        
        await interop.DeleteDatabaseAsync();
        
        Assert.That(m_jsRuntime.Databases.ContainsKey("TestDb"), Is.False);
    }

    [Test]
    public async Task TruncatePagesAsyncRemovesPagesTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        // Write some pages
        await interop.WritePageAsync(0, new byte[] { 1 });
        await interop.WritePageAsync(1, new byte[] { 2 });
        await interop.WritePageAsync(2, new byte[] { 3 });
        await interop.WritePageAsync(3, new byte[] { 4 });
        await interop.SetPageCountAsync(4);
        
        // Truncate to 2 pages
        await interop.TruncatePagesAsync(2);
        
        var mockDb = m_jsRuntime.Databases["TestDb"];
        Assert.That(mockDb.PageCount, Is.EqualTo(2));
        Assert.That(mockDb.Pages.ContainsKey(0), Is.True);
        Assert.That(mockDb.Pages.ContainsKey(1), Is.True);
        Assert.That(mockDb.Pages.ContainsKey(2), Is.False);
        Assert.That(mockDb.Pages.ContainsKey(3), Is.False);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public async Task DisposeAsyncClosesConnectionTest()
    {
        var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        await interop.DisposeAsync();
        
        // Should not throw
    }

    [Test]
    public async Task DisposeAsyncCanBeCalledMultipleTimesTest()
    {
        var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.OpenAsync();
        
        await interop.DisposeAsync();
        await interop.DisposeAsync();
        await interop.DisposeAsync();
        
        // Should not throw
    }

    [Test]
    public async Task OperationsAfterDisposeThrowTest()
    {
        var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        await interop.DisposeAsync();
        
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await interop.OpenAsync();
        });
        
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await interop.ReadPageAsync(0);
        });
        
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await interop.WritePageAsync(0, new byte[10]);
        });
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public void ReadPageAsyncWithCancelledTokenThrowsTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await interop.ReadPageAsync(0, cts.Token);
        });
    }

    [Test]
    public void WritePageAsyncWithCancelledTokenThrowsTest()
    {
        using var interop = new IndexedDbInterop(m_jsRuntime, "TestDb");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await interop.WritePageAsync(0, new byte[10], cts.Token);
        });
    }

    #endregion
}
