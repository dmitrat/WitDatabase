using NUnit.Framework;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;

namespace OutWit.Database.Core.IndexedDb.Tests;

/// <summary>
/// Tests for StorageIndexedDb.
/// </summary>
[TestFixture]
public class StorageIndexedDbTests
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
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        Assert.That(storage.DatabaseName, Is.EqualTo("TestDb"));
        Assert.That(storage.PageSize, Is.EqualTo(DatabaseConstants.DEFAULT_PAGE_SIZE));
        Assert.That(storage.ProviderKey, Is.EqualTo(StorageIndexedDb.PROVIDER_KEY));
    }

    [Test]
    public void ConstructorWithCustomPageSizeSucceedsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime, pageSize: 8192);
        
        Assert.That(storage.PageSize, Is.EqualTo(8192));
    }

    [Test]
    public void ConstructorWithNullJsRuntimeThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new StorageIndexedDb("TestDb", null!);
        });
    }

    [Test]
    public void ConstructorWithEmptyDatabaseNameThrowsTest()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = new StorageIndexedDb("", m_jsRuntime);
        });
    }

    [Test]
    public void ConstructorWithInvalidPageSizeThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new StorageIndexedDb("TestDb", m_jsRuntime, pageSize: 100);
        });
    }

    #endregion

    #region Initialization Tests

    [Test]
    public async Task InitializeAsyncCreatesEmptyDatabaseTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        await storage.InitializeAsync();
        
        Assert.That(storage.IsInitialized, Is.True);
        Assert.That(storage.PageCount, Is.EqualTo(1)); // Initial page
    }

    [Test]
    public async Task InitializeAsyncStoresPageSizeTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime, pageSize: 8192);
        
        await storage.InitializeAsync();
        
        var mockDb = m_jsRuntime.Databases["TestDb"];
        Assert.That(mockDb.PageSize, Is.EqualTo(8192));
    }

    [Test]
    public async Task InitializeAsyncWithMismatchedPageSizeThrowsTest()
    {
        // First, create database with 4096 page size
        using (var storage1 = new StorageIndexedDb("TestDb", m_jsRuntime, pageSize: 4096))
        {
            await storage1.InitializeAsync();
        }
        
        // Try to open with different page size
        using var storage2 = new StorageIndexedDb("TestDb", m_jsRuntime, pageSize: 8192);
        
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await storage2.InitializeAsync();
        });
        
        Assert.That(ex!.Message, Does.Contain("4096"));
        Assert.That(ex.Message, Does.Contain("8192"));
    }

    [Test]
    public async Task InitializeAsyncMultipleTimesIsIdempotentTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        await storage.InitializeAsync();
        await storage.InitializeAsync();
        await storage.InitializeAsync();
        
        Assert.That(storage.IsInitialized, Is.True);
    }

    #endregion

    #region Read/Write Tests

    [Test]
    public async Task WriteAndReadPageAsyncRoundTripTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        // Extend storage to have page 1
        await storage.SetSizeAsync(2);
        
        // Write test data
        var writeBuffer = new byte[storage.PageSize];
        writeBuffer[0] = 0xAB;
        writeBuffer[1] = 0xCD;
        writeBuffer[storage.PageSize - 1] = 0xEF;
        
        await storage.WritePageAsync(1, writeBuffer);
        
        // Read it back
        var readBuffer = new byte[storage.PageSize];
        await storage.ReadPageAsync(1, readBuffer);
        
        Assert.That(readBuffer[0], Is.EqualTo(0xAB));
        Assert.That(readBuffer[1], Is.EqualTo(0xCD));
        Assert.That(readBuffer[storage.PageSize - 1], Is.EqualTo(0xEF));
    }

    [Test]
    public async Task WriteSyncAndReadSyncRoundTripTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        // Must initialize first (required for sync operations)
        await storage.InitializeAsync();
        
        // Extend storage
        storage.SetSize(2);
        
        // Write test data
        var writeBuffer = new byte[storage.PageSize];
        writeBuffer[0] = 0x12;
        writeBuffer[1] = 0x34;
        
        storage.WritePage(1, writeBuffer);
        
        // Read it back
        var readBuffer = new byte[storage.PageSize];
        storage.ReadPage(1, readBuffer);
        
        Assert.That(readBuffer[0], Is.EqualTo(0x12));
        Assert.That(readBuffer[1], Is.EqualTo(0x34));
    }

    [Test]
    public async Task ReadNonExistentPageReturnsZerosTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        await storage.SetSizeAsync(5);
        
        // Read page that was never written
        var buffer = new byte[storage.PageSize];
        buffer[0] = 0xFF; // Pre-fill to verify it gets cleared
        
        await storage.ReadPageAsync(3, buffer);
        
        Assert.That(buffer[0], Is.EqualTo(0));
        Assert.That(buffer.All(b => b == 0), Is.True);
    }

    [Test]
    public async Task ReadPageWithInvalidPageNumberThrowsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        var buffer = new byte[storage.PageSize];
        
        // Reading beyond current size should throw
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await storage.ReadPageAsync(999, buffer);
        });
        
        // Reading with negative page number should throw
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await storage.ReadPageAsync(-1, buffer);
        });
    }

    [Test]
    public async Task WritePageWithNegativePageNumberThrowsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        var buffer = new byte[storage.PageSize];
        
        // Negative page number should throw
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await storage.WritePageAsync(-1, buffer);
        });
    }

    [Test]
    public async Task WritePageBeyondCurrentSizeAutoExtendsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        Assert.That(storage.PageCount, Is.EqualTo(1)); // Initial page
        
        // Write to page 5 (beyond current size)
        var buffer = new byte[storage.PageSize];
        buffer[0] = 0x42;
        await storage.WritePageAsync(5, buffer);
        
        // Storage should have auto-extended
        Assert.That(storage.PageCount, Is.EqualTo(6)); // 0-5 = 6 pages
        
        // Read it back
        var readBuffer = new byte[storage.PageSize];
        await storage.ReadPageAsync(5, readBuffer);
        Assert.That(readBuffer[0], Is.EqualTo(0x42));
    }

    [Test]
    public async Task ReadPageWithSmallBufferThrowsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        var smallBuffer = new byte[100];
        
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await storage.ReadPageAsync(0, smallBuffer);
        });
    }

    #endregion

    #region SetSize Tests

    [Test]
    public async Task SetSizeAsyncExtendsStorageTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        Assert.That(storage.PageCount, Is.EqualTo(1));
        
        await storage.SetSizeAsync(10);
        
        Assert.That(storage.PageCount, Is.EqualTo(10));
    }

    [Test]
    public async Task SetSizeAsyncTruncatesStorageTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        await storage.SetSizeAsync(10);
        
        // Write to page that will be truncated
        var buffer = new byte[storage.PageSize];
        buffer[0] = 0xAA;
        await storage.WritePageAsync(5, buffer);
        
        // Truncate
        await storage.SetSizeAsync(3);
        
        Assert.That(storage.PageCount, Is.EqualTo(3));
        
        // Verify page 5 no longer exists in mock
        var mockDb = m_jsRuntime.Databases["TestDb"];
        Assert.That(mockDb.Pages.ContainsKey(5), Is.False);
    }

    [Test]
    public async Task SetSizeSyncWorksTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        // Must initialize first (required for sync operations)
        await storage.InitializeAsync();
        
        storage.SetSize(5);
        
        Assert.That(storage.PageCount, Is.EqualTo(5));
    }

    [Test]
    public async Task SetSizeWithNegativeValueThrowsTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await storage.SetSizeAsync(-1);
        });
    }

    #endregion

    #region Flush Tests

    [Test]
    public async Task FlushAsyncCompletesSuccessfullyTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        await storage.InitializeAsync();
        
        // Should complete without error
        await storage.FlushAsync();
    }

    [Test]
    public void FlushSyncCompletesSuccessfullyTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        // Should complete without error
        storage.Flush();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposeCanBeCalledMultipleTimesTest()
    {
        var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        storage.Dispose();
        storage.Dispose();
        storage.Dispose();
        
        // Should not throw
    }

    [Test]
    public async Task DisposeAsyncCanBeCalledMultipleTimesTest()
    {
        var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        await storage.DisposeAsync();
        await storage.DisposeAsync();
        await storage.DisposeAsync();
        
        // Should not throw
    }

    [Test]
    public void OperationsAfterDisposeThrowTest()
    {
        var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        storage.Dispose();
        
        var buffer = new byte[4096];
        
        Assert.Throws<ObjectDisposedException>(() => storage.ReadPage(0, buffer));
        Assert.Throws<ObjectDisposedException>(() => storage.WritePage(0, buffer));
        Assert.Throws<ObjectDisposedException>(() => storage.SetSize(10));
        Assert.Throws<ObjectDisposedException>(() => storage.Flush());
    }

    #endregion

    #region Properties Tests

    [Test]
    public void ProviderKeyIsCorrectTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        Assert.That(storage.ProviderKey, Is.EqualTo("indexeddb"));
    }

    [Test]
    public void IsReadOnlyIsFalseTest()
    {
        using var storage = new StorageIndexedDb("TestDb", m_jsRuntime);
        
        Assert.That(storage.IsReadOnly, Is.False);
    }

    [Test]
    public void DatabaseNameIsCorrectTest()
    {
        using var storage = new StorageIndexedDb("MyTestDatabase", m_jsRuntime);
        
        Assert.That(storage.DatabaseName, Is.EqualTo("MyTestDatabase"));
    }

    #endregion
}
