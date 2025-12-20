using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Tests for BlockEncryptor - variable-length data encryption for LSM-Tree.
/// </summary>
[TestFixture]
public class EncryptorBlockTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    #region Basic Round-Trip

    [Test]
    public void EncryptDecryptRoundTripTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[1234];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 42);
        byte[]? decrypted = encryptor.Decrypt(encrypted, blockId: 42);

        Assert.That(decrypted, Is.Not.Null);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(100)]
    [TestCase(1000)]
    [TestCase(10000)]
    [TestCase(100000)]
    public void EncryptDecryptVariousSizesTest(int size)
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[size];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 1);
        byte[]? decrypted = encryptor.Decrypt(encrypted, blockId: 1);

        Assert.That(decrypted, Is.Not.Null);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void EncryptDecryptEmptyDataTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = [];
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 0);
        byte[]? decrypted = encryptor.Decrypt(encrypted, blockId: 0);

        Assert.That(decrypted, Is.Not.Null);
        Assert.That(decrypted!.Length, Is.EqualTo(0));
    }

    #endregion

    #region Authentication Failures

    [Test]
    public void DecryptWrongBlockIdReturnsNullTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 1);
        byte[]? decrypted = encryptor.Decrypt(encrypted, blockId: 2);

        Assert.That(decrypted, Is.Null);
    }

    [Test]
    public void DecryptTamperedDataReturnsNullTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 1);
        encrypted[50] ^= 0xFF;
        byte[]? decrypted = encryptor.Decrypt(encrypted, blockId: 1);

        Assert.That(decrypted, Is.Null);
    }

    [Test]
    public void DecryptTruncatedDataReturnsNullTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 1);
        byte[] truncated = encrypted[..^10];
        byte[]? decrypted = encryptor.Decrypt(truncated, blockId: 1);

        Assert.That(decrypted, Is.Null);
    }

    [Test]
    public void DecryptDataTooShortReturnsNullTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] shortData = new byte[10];
        byte[]? decrypted = encryptor.Decrypt(shortData, blockId: 1);

        Assert.That(decrypted, Is.Null);
    }

    #endregion

    #region Block ID Uniqueness

    [Test]
    public void EncryptDifferentBlockIdsProduceDifferentCiphertextTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted1 = encryptor.Encrypt(plaintext, blockId: 1);
        byte[] encrypted2 = encryptor.Encrypt(plaintext, blockId: 2);

        Assert.That(encrypted1.SequenceEqual(encrypted2), Is.False);
    }

    [Test]
    public void EncryptSameBlockIdProducesDifferentCiphertextTest()
    {
        // With monotonic counter, encrypting same block twice produces different ciphertext
        // This is critical for AES-GCM security - nonce must never repeat!
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] encrypted1 = encryptor.Encrypt(plaintext, blockId: 5);
        byte[] encrypted2 = encryptor.Encrypt(plaintext, blockId: 5);

        // Ciphertexts should be DIFFERENT due to counter increment
        Assert.That(encrypted1.SequenceEqual(encrypted2), Is.False);
        
        // But both should decrypt correctly
        byte[]? decrypted1 = encryptor.Decrypt(encrypted1, blockId: 5);
        byte[]? decrypted2 = encryptor.Decrypt(encrypted2, blockId: 5);
        
        Assert.That(decrypted1, Is.Not.Null);
        Assert.That(decrypted2, Is.Not.Null);
        Assert.That(decrypted1, Is.EqualTo(plaintext));
        Assert.That(decrypted2, Is.EqualTo(plaintext));
    }

    #endregion

    #region Overhead

    [Test]
    public void OverheadIsCorrectTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        Assert.That(encryptor.Overhead, Is.EqualTo(28));
    }

    [Test]
    public void EncryptedSizeIsPlaintextPlusOverheadTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        int[] sizes = [0, 1, 100, 1000, 10000];

        foreach (var size in sizes)
        {
            byte[] plaintext = new byte[size];
            byte[] encrypted = encryptor.Encrypt(plaintext, blockId: 1);

            Assert.That(encrypted.Length, Is.EqualTo(size + encryptor.Overhead));
        }
    }

    #endregion
}
