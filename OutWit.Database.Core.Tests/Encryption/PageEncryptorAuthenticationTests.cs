using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Tests for PageEncryptor authentication failures - tampering detection.
/// </summary>
[TestFixture]
public class PageEncryptorAuthenticationTests
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
    public void DecryptWrongPageNumberReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[4096];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        int result = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 2, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptTamperedCiphertextBodyReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[4096];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        
        // Tamper with ciphertext body (after nonce, before tag)
        ciphertext[encryptor.Overhead + 100] ^= 0xFF;
        
        int result = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptTamperedTagReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        
        // Tamper with authentication tag (last 16 bytes)
        ciphertext[^1] ^= 0xFF;
        
        int result = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptTamperedNonceReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        
        // Tamper with nonce (first 12 bytes)
        ciphertext[0] ^= 0xFF;
        
        int result = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen), pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptTruncatedCiphertextReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] plaintext = new byte[100];
        Random.Shared.NextBytes(plaintext);
        
        byte[] ciphertext = new byte[plaintext.Length + encryptor.Overhead];
        byte[] decrypted = new byte[plaintext.Length];

        int encryptedLen = encryptor.Encrypt(plaintext, pageNumber: 1, ciphertext);
        
        // Truncate ciphertext
        int result = encryptor.Decrypt(ciphertext.AsSpan(0, encryptedLen - 10), pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptCiphertextShorterThanOverheadReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] shortCiphertext = new byte[10]; // Less than overhead (28 bytes)
        byte[] decrypted = new byte[100];

        int result = encryptor.Decrypt(shortCiphertext, pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptAllZeroCiphertextReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] zeroCiphertext = new byte[128]; // All zeros
        byte[] decrypted = new byte[100];

        int result = encryptor.Decrypt(zeroCiphertext, pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void DecryptRandomGarbageReturnsMinusOneTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorPage(provider, m_salt);

        byte[] garbage = new byte[128];
        Random.Shared.NextBytes(garbage);
        byte[] decrypted = new byte[100];

        int result = encryptor.Decrypt(garbage, pageNumber: 1, decrypted);

        Assert.That(result, Is.EqualTo(-1));
    }
}
