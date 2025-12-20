using NUnit.Framework;
using OutWit.Database.Core.BouncyCastle;
using System.Security.Cryptography;

namespace OutWit.Database.Core.Tests.Encryption;

[TestFixture]
public class BouncyCastleCryptoProviderTests
{
    #region Constants

    private const int NONCE_SIZE = 12;
    private const int TAG_SIZE = 16;

    #endregion

    #region Fields

    private byte[] m_key = null!;
    private BouncyCastleCryptoProvider m_provider = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_key = new byte[32];
        RandomNumberGenerator.Fill(m_key);
        m_provider = new BouncyCastleCryptoProvider(m_key);
    }

    [TearDown]
    public void TearDown()
    {
        m_provider.Dispose();
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_WithValidKey_CreatesProviderTest()
    {
        using var provider = new BouncyCastleCryptoProvider(m_key);
        Assert.That(provider.NonceSize, Is.EqualTo(NONCE_SIZE));
        Assert.That(provider.TagSize, Is.EqualTo(TAG_SIZE));
    }

    [Test]
    public void Constructor_WithInvalidKeySize_ThrowsTest()
    {
        var shortKey = new byte[16];
        Assert.Throws<ArgumentException>(() => new BouncyCastleCryptoProvider(shortKey));
    }

    [Test]
    public void FromPassword_CreatesValidProviderTest()
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        
        using var provider = BouncyCastleCryptoProvider.FromPassword("test-password", salt);
        
        Assert.That(provider.NonceSize, Is.EqualTo(NONCE_SIZE));
        Assert.That(provider.TagSize, Is.EqualTo(TAG_SIZE));
    }

    [Test]
    public void FromPassword_SameInputs_ProducesSameKeyTest()
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        
        using var provider1 = BouncyCastleCryptoProvider.FromPassword("password123", salt, 10000);
        using var provider2 = BouncyCastleCryptoProvider.FromPassword("password123", salt, 10000);
        
        // Encrypt with provider1, decrypt with provider2
        var plaintext = "Hello, World!"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        provider1.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = new byte[plaintext.Length];
        var success = provider2.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.True);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    #endregion

    #region Encrypt/Decrypt Tests

    [Test]
    public void EncryptDecrypt_RoundTrip_SucceedsTest()
    {
        var plaintext = "Hello, ChaCha20-Poly1305!"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = new byte[plaintext.Length];
        var success = m_provider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.True);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void Encrypt_ProducesDifferentCiphertextForDifferentNoncesTest()
    {
        var plaintext = "Same plaintext"u8.ToArray();
        
        var nonce1 = new byte[NONCE_SIZE];
        var nonce2 = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce1);
        RandomNumberGenerator.Fill(nonce2);
        
        var ciphertext1 = new byte[plaintext.Length];
        var ciphertext2 = new byte[plaintext.Length];
        var tag1 = new byte[TAG_SIZE];
        var tag2 = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce1, plaintext, ciphertext1, tag1);
        m_provider.Encrypt(nonce2, plaintext, ciphertext2, tag2);
        
        Assert.That(ciphertext1, Is.Not.EqualTo(ciphertext2));
    }

    [Test]
    public void Decrypt_WithTamperedCiphertext_FailsTest()
    {
        var plaintext = "Sensitive data"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        // Tamper with ciphertext
        ciphertext[0] ^= 0xFF;
        
        var decrypted = new byte[plaintext.Length];
        var success = m_provider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.False);
    }

    [Test]
    public void Decrypt_WithTamperedTag_FailsTest()
    {
        var plaintext = "Sensitive data"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        // Tamper with tag
        tag[0] ^= 0xFF;
        
        var decrypted = new byte[plaintext.Length];
        var success = m_provider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.False);
    }

    [Test]
    public void Decrypt_WithWrongNonce_FailsTest()
    {
        var plaintext = "Sensitive data"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        var wrongNonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        RandomNumberGenerator.Fill(wrongNonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = new byte[plaintext.Length];
        var success = m_provider.Decrypt(wrongNonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.False);
    }

    [Test]
    public void Decrypt_WithWrongKey_FailsTest()
    {
        var plaintext = "Sensitive data"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        // Create provider with different key
        var wrongKey = new byte[32];
        RandomNumberGenerator.Fill(wrongKey);
        using var wrongProvider = new BouncyCastleCryptoProvider(wrongKey);
        
        var decrypted = new byte[plaintext.Length];
        var success = wrongProvider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.False);
    }

    #endregion

    #region Large Data Tests

    [Test]
    public void EncryptDecrypt_LargeData_SucceedsTest()
    {
        var plaintext = new byte[1024 * 1024]; // 1 MB
        RandomNumberGenerator.Fill(plaintext);
        
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = new byte[plaintext.Length];
        var success = m_provider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.True);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void EncryptDecrypt_EmptyData_SucceedsTest()
    {
        var plaintext = Array.Empty<byte>();
        var nonce = new byte[NONCE_SIZE];
        RandomNumberGenerator.Fill(nonce);
        
        var ciphertext = Array.Empty<byte>();
        var tag = new byte[TAG_SIZE];
        
        m_provider.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = Array.Empty<byte>();
        var success = m_provider.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.True);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Encrypt_AfterDispose_ThrowsTest()
    {
        m_provider.Dispose();
        
        var plaintext = "test"u8.ToArray();
        var nonce = new byte[NONCE_SIZE];
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TAG_SIZE];
        
        Assert.Throws<ObjectDisposedException>(() => 
            m_provider.Encrypt(nonce, plaintext, ciphertext, tag));
    }

    [Test]
    public void Decrypt_AfterDispose_ThrowsTest()
    {
        m_provider.Dispose();
        
        var ciphertext = new byte[16];
        var nonce = new byte[NONCE_SIZE];
        var tag = new byte[TAG_SIZE];
        var plaintext = new byte[16];
        
        Assert.Throws<ObjectDisposedException>(() => 
            m_provider.Decrypt(nonce, ciphertext, tag, plaintext));
    }

    #endregion
}
