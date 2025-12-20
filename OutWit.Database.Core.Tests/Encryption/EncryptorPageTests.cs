using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Tests for PageEncryptor - basic round-trip, nonce uniqueness, and edge cases.
/// </summary>
[TestFixture]
public class EncryptorPageTests
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
        using var encryptor = new EncryptorPage(provider, m_salt);
        
        byte[] plaintext = new byte[4096];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 1, decrypted);

        Assert.That(decryptedLen, Is.EqualTo(plaintext.Length));
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(100)]
    [TestCase(4096)]
    [TestCase(8192)]
    [TestCase(65536)]
    public void EncryptDecryptVariousSizesTest(int size)
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);
        
        byte[] plaintext = new byte[size];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 42, ciphertext);
        int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 42, decrypted);

        Assert.That(decryptedLen, Is.EqualTo(plaintext.Length));
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void EncryptDecryptEmptyPlaintextTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);
        
        byte[] plaintext = [];
        byte[] ciphertext = new byte[encryptor.Overhead];
        byte[] decrypted = [];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 0, ciphertext);
        int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 0, decrypted);

        Assert.That(decryptedLen, Is.EqualTo(0));
    }

    #endregion

    #region Nonce Uniqueness

    [Test]
    public void EncryptDifferentPageNumbersProducesDifferentCiphertextTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[4096];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext1 = new byte[plaintext.Length + encryptor.Overhead];
        byte[] ciphertext2 = new byte[plaintext.Length + encryptor.Overhead];

        encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext1);
        encryptor.Encrypt(plaintext, pageNumber: 2, ciphertext2);

        Assert.That(ciphertext1.SequenceEqual(ciphertext2), Is.False);
    }

    [Test]
    public void EncryptSamePageNumberProducesDifferentCiphertextTest()
    {
        // With monotonic counter, encrypting same page twice produces different ciphertext
        // This is critical for AES-GCM security - nonce must never repeat!
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext1 = new byte[plaintext.Length + encryptor.Overhead];
        byte[] ciphertext2 = new byte[plaintext.Length + encryptor.Overhead];

        encryptor.Encrypt(plaintext, pageNumber: 5, ciphertext1);
        encryptor.Encrypt(plaintext, pageNumber: 5, ciphertext2);

        // Ciphertexts should be DIFFERENT due to counter increment
        Assert.That(ciphertext1.SequenceEqual(ciphertext2), Is.False);
        
        // But both should decrypt correctly
        byte[] decrypted1 = new byte[plaintext.Length];
        byte[] decrypted2 = new byte[plaintext.Length];
        
        int len1 = encryptor.Decrypt(ciphertext1, pageNumber: 5, decrypted1);
        int len2 = encryptor.Decrypt(ciphertext2, pageNumber: 5, decrypted2);
        
        Assert.That(len1, Is.EqualTo(plaintext.Length));
        Assert.That(len2, Is.EqualTo(plaintext.Length));
        Assert.That(decrypted1, Is.EqualTo(plaintext));
        Assert.That(decrypted2, Is.EqualTo(plaintext));
    }

    [Test]
    public void EncryptDecryptEdgePageNumbersTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);

        long[] edgePageNumbers = [0, 1, -1, long.MaxValue, long.MinValue, int.MaxValue, int.MinValue];

        foreach (var pageNumber in edgePageNumbers)
        {
            byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
            byte[] decrypted = new byte[plaintext.Length];

            int encryptedLen = encryptor.Encrypt(plaintext, pageNumber, ciphertext);
            int decryptedLen = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber, decrypted);

            Assert.That(decryptedLen, Is.EqualTo(plaintext.Length), $"Failed for page number {pageNumber}");
            Assert.That(decrypted, Is.EqualTo(plaintext), $"Data mismatch for page number {pageNumber}");
        }
    }

    #endregion

    #region Overhead and Size

    [Test]
    public void OverheadIsCorrectTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        Assert.That(encryptor.Overhead, Is.EqualTo(28)); // 12 nonce + 16 tag
    }

    [Test]
    public void EncryptedSizeIsPlaintextPlusOverheadTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        int[] sizes = [0, 1, 100, 4096, 65536];

        foreach (var size in sizes)
        {
            byte[] plaintext = new byte[size];
            byte[] ciphertext = new byte[size + encryptor.Overhead];

            int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);

            Assert.That(encryptedLen, Is.EqualTo(size + encryptor.Overhead));
        }
    }

    #endregion

    #region Validation

    [Test]
    public void EncryptCiphertextBufferTooSmallThrowsTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        byte[] ciphertext = new byte[50]; // Too small

        Assert.Throws<ArgumentException>(() => encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext));
    }

    [Test]
    public void DecryptPlaintextBufferTooSmallThrowsTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);

        byte[] tooSmallDecrypted = new byte[50];

        Assert.Throws<ArgumentException>(() => 
            encryptor.Decrypt(ciphertext, pageNumber: 1, tooSmallDecrypted));
    }

    [Test]
    public void ConstructorSaltTooShortThrowsTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        Assert.Throws<ArgumentException>(() => new EncryptorPage(provider, new byte[4]));
    }

    [Test]
    public void AfterDisposeThrowsObjectDisposedExceptionTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        encryptor.Dispose();

        byte[] data = new byte[100];
        byte[] output = new byte[128];

        Assert.Throws<ObjectDisposedException>(() => encryptor.Encrypt(data, 1, output));
        Assert.Throws<ObjectDisposedException>(() => encryptor.Decrypt(output, 1, data));
    }

    #endregion
}
