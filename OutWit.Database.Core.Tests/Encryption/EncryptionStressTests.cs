using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Stress tests for encryption components.
/// </summary>
[TestFixture]
[Category("Stress")]
public class EncryptionStressTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    [Test]
    public void PageEncryptorEncryptManyPagesAllSucceedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[4096];
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        for (int page = 0; page < 1000; page++)
        {
            Random.Shared.NextBytes(plaintext);
            
            int encryptedLen = encryptor.Encrypt(plaintext, page, ciphertext);
            int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), page, decrypted);
            
            Assert.That(decryptedLen, Is.EqualTo(plaintext.Length));
            Assert.That(decrypted, Is.EqualTo(plaintext));
        }
    }

    [Test]
    public void PageEncryptorRandomPageAccessAllSucceedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        var random = new Random(42);
        var pages = new Dictionary<long, (byte[] Plaintext, byte[] Ciphertext)>();

        // Encrypt 100 random pages
        for (int i = 0; i < 100; i++)
        {
            long pageNumber = random.NextInt64();
            byte[] plaintext = new byte[4096];
            random.NextBytes(plaintext);
            
            byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
            encryptor.Encrypt(plaintext, pageNumber, ciphertext);
            
            pages[pageNumber] = (plaintext, ciphertext);
        }

        // Decrypt in random order
        foreach (var (pageNumber, (expectedPlaintext, ciphertext)) in pages.OrderBy(_ => random.Next()))
        {
            byte[] decrypted = new byte[expectedPlaintext.Length];
            int decryptedLen = encryptor.Decrypt(ciphertext, pageNumber, decrypted);

            Assert.That(decryptedLen, Is.EqualTo(expectedPlaintext.Length));
            Assert.That(decrypted, Is.EqualTo(expectedPlaintext));
        }
    }

    [Test]
    public void BlockEncryptorEncryptManyBlocksAllSucceedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        var random = new Random(42);

        for (long blockId = 0; blockId < 1000; blockId++)
        {
            int size = random.Next(100, 10000);
            byte[] plaintext = new byte[size];
            random.NextBytes(plaintext);
            
            byte[] encrypted = encryptor.Encrypt(plaintext, blockId);
            byte[]? decrypted = encryptor.Decrypt(encrypted, blockId);
            
            Assert.That(decrypted, Is.Not.Null);
            Assert.That(decrypted, Is.EqualTo(plaintext));
        }
    }

    [Test]
    public void BlockEncryptorRandomBlockAccessAllSucceedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        var random = new Random(42);
        var blocks = new Dictionary<long, (byte[] Plaintext, byte[] Encrypted)>();

        // Encrypt 100 random blocks
        for (int i = 0; i < 100; i++)
        {
            long blockId = random.NextInt64();
            int size = random.Next(100, 5000);
            byte[] plaintext = new byte[size];
            random.NextBytes(plaintext);
            
            byte[] encrypted = encryptor.Encrypt(plaintext, blockId);
            blocks[blockId] = (plaintext, encrypted);
        }

        // Decrypt in random order
        foreach (var (blockId, (expectedPlaintext, encrypted)) in blocks.OrderBy(_ => random.Next()))
        {
            byte[]? decrypted = encryptor.Decrypt(encrypted, blockId);

            Assert.That(decrypted, Is.Not.Null);
            Assert.That(decrypted, Is.EqualTo(expectedPlaintext));
        }
    }

    [Test]
    public void PageEncryptorLargePagesAllSucceedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        int[] pageSizes = [8192, 16384, 32768, 65536];

        foreach (var pageSize in pageSizes)
        {
            byte[] plaintext = new byte[pageSize];
            Random.Shared.NextBytes(plaintext);
            
            byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
            byte[] decrypted = new byte[plaintext.Length];

            int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
            int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 1, decrypted);

            Assert.That(decryptedLen, Is.EqualTo(plaintext.Length), $"Failed for page size {pageSize}");
            Assert.That(decrypted, Is.EqualTo(plaintext), $"Data mismatch for page size {pageSize}");
        }
    }
}
