using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Tests for EncryptedStorage - transparent encryption wrapper.
/// </summary>
[TestFixture]
public class StorageEncryptedTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;
    private string? m_testDir;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
        m_testDir = Path.Combine(Path.GetTempPath(), $"EncryptedStorageTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDir != null && Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private StorageEncrypted CreateEncryptedMemoryStorage(int pageSize = 4096, int pageCount = 1000)
    {
        var innerStorage = new StorageMemory(pageSize + 28, pageCount);
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        return new StorageEncrypted(innerStorage, encryptor);
    }

    #region Basic Operations

    [Test]
    public void WriteAndReadPageRoundTripTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        byte[] data = new byte[storage.PageSize];
        Random.Shared.NextBytes(data);

        storage.WritePage(0, data);

        byte[] readBuffer = new byte[storage.PageSize];
        storage.ReadPage(0, readBuffer);

        Assert.That(readBuffer, Is.EqualTo(data));
    }

    [Test]
    public void WriteAndReadMultiplePagesAllCorrectTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        var pages = new Dictionary<long, byte[]>();

        for (int i = 0; i < 100; i++)
        {
            byte[] data = new byte[storage.PageSize];
            Random.Shared.NextBytes(data);
            storage.WritePage(i, data);
            pages[i] = data;
        }

        foreach (var (pageNum, expectedData) in pages)
        {
            byte[] readBuffer = new byte[storage.PageSize];
            storage.ReadPage(pageNum, readBuffer);
            Assert.That(readBuffer, Is.EqualTo(expectedData), $"Page {pageNum} mismatch");
        }
    }

    [Test]
    public void ReadUnwrittenPageReturnsZerosTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        byte[] readBuffer = new byte[storage.PageSize];
        storage.ReadPage(42, readBuffer);

        Assert.That(readBuffer.All(b => b == 0), Is.True);
    }

    [Test]
    public void OverwritePageReturnsNewDataTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        byte[] data1 = new byte[storage.PageSize];
        byte[] data2 = new byte[storage.PageSize];
        Random.Shared.NextBytes(data1);
        Random.Shared.NextBytes(data2);

        storage.WritePage(5, data1);
        storage.WritePage(5, data2);

        byte[] readBuffer = new byte[storage.PageSize];
        storage.ReadPage(5, readBuffer);

        Assert.That(readBuffer, Is.EqualTo(data2));
        Assert.That(readBuffer, Is.Not.EqualTo(data1));
    }

    #endregion

    #region Async Operations

    [Test]
    public async Task WriteAndReadPageAsyncRoundTripTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        byte[] data = new byte[storage.PageSize];
        Random.Shared.NextBytes(data);

        await storage.WritePageAsync(0, data);

        byte[] readBuffer = new byte[storage.PageSize];
        await storage.ReadPageAsync(0, readBuffer);

        Assert.That(readBuffer, Is.EqualTo(data));
    }

    [Test]
    public async Task FlushAsyncCompletesSuccessfullyTest()
    {
        using var storage = CreateEncryptedMemoryStorage();

        byte[] data = new byte[storage.PageSize];
        Random.Shared.NextBytes(data);
        await storage.WritePageAsync(0, data);

        await storage.FlushAsync();

        byte[] readBuffer = new byte[storage.PageSize];
        await storage.ReadPageAsync(0, readBuffer);
        Assert.That(readBuffer, Is.EqualTo(data));
    }

    #endregion

    #region Properties

    [Test]
    public void PageSizeReturnsInnerPageSizeMinusOverheadTest()
    {
        int innerPageSize = 4096 + 28;
        using var innerStorage = new StorageMemory(innerPageSize, 100);
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);
        using var storage = new StorageEncrypted(innerStorage, encryptor);

        Assert.That(storage.PageSize, Is.EqualTo(4096));
    }

    [Test]
    public void PageCountReflectsInnerStorageTest()
    {
        using var storage = CreateEncryptedMemoryStorage(pageCount: 500);
        Assert.That(storage.PageCount, Is.EqualTo(500));
    }

    [Test]
    public void IsReadOnlyReflectsInnerStorageTest()
    {
        using var storage = CreateEncryptedMemoryStorage();
        Assert.That(storage.IsReadOnly, Is.False);
    }

    #endregion

    #region SetSize

    [Test]
    public void SetSizeChangesPageCountTest()
    {
        using var storage = CreateEncryptedMemoryStorage(pageCount: 100);

        storage.SetSize(200);

        Assert.That(storage.PageCount, Is.EqualTo(200));
    }

    #endregion

    #region Validation

    [Test]
    public void AfterDisposeThrowsObjectDisposedExceptionTest()
    {
        var storage = CreateEncryptedMemoryStorage();
        storage.Dispose();

        byte[] data = new byte[4096];

        Assert.Throws<ObjectDisposedException>(() => storage.WritePage(0, data));
        Assert.Throws<ObjectDisposedException>(() => storage.ReadPage(0, data));
        Assert.Throws<ObjectDisposedException>(() => storage.Flush());
    }

    #endregion

    #region Edge Cases

    [Test]
    public void LargePageEncryptsCorrectlyTest()
    {
        int pageSize = DatabaseConstants.MAX_PAGE_SIZE - 28;
        using var innerStorage = new StorageMemory(DatabaseConstants.MAX_PAGE_SIZE, 10);
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);
        using var storage = new StorageEncrypted(innerStorage, encryptor);

        Assert.That(storage.PageSize, Is.EqualTo(pageSize));

        byte[] data = new byte[storage.PageSize];
        Random.Shared.NextBytes(data);

        storage.WritePage(0, data);

        byte[] readBuffer = new byte[storage.PageSize];
        storage.ReadPage(0, readBuffer);

        Assert.That(readBuffer, Is.EqualTo(data));
    }

    #endregion
}
