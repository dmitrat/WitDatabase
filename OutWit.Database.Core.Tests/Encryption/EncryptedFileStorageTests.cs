using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Tests for EncryptedStorage with FileStorage - persistence and security.
/// </summary>
[TestFixture]
public class EncryptedFileStorageTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;
    private string? m_testDir;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
        m_testDir = Path.Combine(Path.GetTempPath(), $"EncryptedFileStorageTest_{Guid.NewGuid():N}");
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

    private StorageEncrypted CreateEncryptedFileStorage(string filename, byte[] key, byte[] salt, int pageSize = 4096)
    {
        var innerStorage = new StorageFile(filename, pageSize + 28);
        var provider = new EncryptorProviderAesGcm(key);
        var encryptor = new EncryptorPage(provider, salt);
        return new StorageEncrypted(innerStorage, encryptor);
    }

    [Test]
    public void FileStorageWriteReadFlushPersistsTest()
    {
        var filename = Path.Combine(m_testDir!, "encrypted.db");

        byte[] data = new byte[4096];
        Random.Shared.NextBytes(data);

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            storage.WritePage(0, data);
            storage.Flush();
        }

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            byte[] readBuffer = new byte[storage.PageSize];
            storage.ReadPage(0, readBuffer);
            Assert.That(readBuffer, Is.EqualTo(data));
        }
    }

    [Test]
    public void FileStorageMultiplePagesPersistCorrectlyTest()
    {
        var filename = Path.Combine(m_testDir!, "multi_page.db");
        var pages = new Dictionary<int, byte[]>();

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            for (int i = 0; i < 50; i++)
            {
                byte[] data = new byte[storage.PageSize];
                Random.Shared.NextBytes(data);
                storage.WritePage(i, data);
                pages[i] = data;
            }
            storage.Flush();
        }

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            foreach (var (pageNum, expectedData) in pages)
            {
                byte[] readBuffer = new byte[storage.PageSize];
                storage.ReadPage(pageNum, readBuffer);
                Assert.That(readBuffer, Is.EqualTo(expectedData), $"Page {pageNum} mismatch after reopen");
            }
        }
    }

    [Test]
    public void FileStorageWrongKeyFailsToDecryptTest()
    {
        var filename = Path.Combine(m_testDir!, "wrong_key.db");

        byte[] data = new byte[4096];
        Random.Shared.NextBytes(data);

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            storage.WritePage(0, data);
            storage.Flush();
        }

        byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
        using var storage2 = CreateEncryptedFileStorage(filename, wrongKey, m_salt);

        byte[] readBuffer = new byte[storage2.PageSize];
        
        Assert.Throws<CryptographicException>(() => storage2.ReadPage(0, readBuffer));
    }

    [Test]
    public void FileStorageWrongSaltFailsToDecryptTest()
    {
        var filename = Path.Combine(m_testDir!, "wrong_salt.db");

        byte[] data = new byte[4096];
        Random.Shared.NextBytes(data);

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            storage.WritePage(0, data);
            storage.Flush();
        }

        byte[] wrongSalt = RandomNumberGenerator.GetBytes(16);
        using var storage2 = CreateEncryptedFileStorage(filename, m_key, wrongSalt);

        byte[] readBuffer = new byte[storage2.PageSize];
        
        Assert.Throws<CryptographicException>(() => storage2.ReadPage(0, readBuffer));
    }

    [Test]
    public void FileStorageDataIsEncryptedOnDiskTest()
    {
        var filename = Path.Combine(m_testDir!, "verify_encrypted.db");

        byte[] knownPattern = new byte[4096];
        for (int i = 0; i < knownPattern.Length; i++)
            knownPattern[i] = (byte)(i % 256);

        using (var storage = CreateEncryptedFileStorage(filename, m_key, m_salt))
        {
            storage.WritePage(0, knownPattern);
            storage.Flush();
        }

        byte[] rawFileContents = File.ReadAllBytes(filename);
        
        bool foundPattern = false;
        for (int i = 0; i <= rawFileContents.Length - 100; i++)
        {
            bool match = true;
            for (int j = 0; j < 100 && match; j++)
            {
                if (rawFileContents[i + j] != (byte)(j % 256))
                    match = false;
            }
            if (match)
            {
                foundPattern = true;
                break;
            }
        }

        Assert.That(foundPattern, Is.False, "Plaintext pattern found in encrypted file!");
    }

    [Test]
    public void FileStoragePasswordBasedPersistsCorrectlyTest()
    {
        var filename = Path.Combine(m_testDir!, "password_based.db");
        string password = "MySecurePassword123!";

        byte[] data = new byte[4096];
        Random.Shared.NextBytes(data);

        using (var innerStorage = new StorageFile(filename, 4096 + 28))
        using (var provider = EncryptorProviderAesGcm.FromPassword(password, m_salt, iterations: 10000))
        using (var encryptor = new EncryptorPage(provider, m_salt))
        using (var storage = new StorageEncrypted(innerStorage, encryptor))
        {
            storage.WritePage(0, data);
            storage.Flush();
        }

        using (var innerStorage = new StorageFile(filename, 4096 + 28))
        using (var provider = EncryptorProviderAesGcm.FromPassword(password, m_salt, iterations: 10000))
        using (var encryptor = new EncryptorPage(provider, m_salt))
        using (var storage = new StorageEncrypted(innerStorage, encryptor))
        {
            byte[] readBuffer = new byte[storage.PageSize];
            storage.ReadPage(0, readBuffer);
            Assert.That(readBuffer, Is.EqualTo(data));
        }
    }
}
