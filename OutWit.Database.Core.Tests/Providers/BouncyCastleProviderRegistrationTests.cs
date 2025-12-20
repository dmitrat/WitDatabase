using NUnit.Framework;
using OutWit.Database.Core.BouncyCastle;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// Tests for BouncyCastle provider registration.
/// </summary>
[TestFixture]
public class BouncyCastleProviderRegistrationTests
{
    [SetUp]
    public void Setup()
    {
        // Explicitly register BouncyCastle providers
        // ModuleInitializer may not run automatically in test context
        BouncyCastleProviderRegistration.EnsureRegistered();
    }

    [Test]
    public void ChaCha20Poly1305CryptoProviderRegisteredTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("chacha20-poly1305"), Is.True);
    }

    [Test]
    public void ChaCha20Poly1305CanBeCreatedViaFactoryTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        var crypto = ProviderRegistry.Instance.Create<ICryptoProvider>("chacha20-poly1305",
            new ProviderParameters().Set("key", key));

        Assert.That(crypto, Is.Not.Null);
        Assert.That(crypto.ProviderKey, Is.EqualTo("chacha20-poly1305"));
        Assert.That(crypto.NonceSize, Is.EqualTo(12));
        Assert.That(crypto.TagSize, Is.EqualTo(16));
        
        crypto.Dispose();
    }

    [Test]
    public void AllCryptoProvidersIncludeChaCha20Test()
    {
        var keys = ProviderRegistry.Instance.GetRegisteredKeys<ICryptoProvider>();
        
        Assert.That(keys, Contains.Item("aes-gcm"), "Built-in AES-GCM should be registered");
        Assert.That(keys, Contains.Item("chacha20-poly1305"), "BouncyCastle ChaCha20-Poly1305 should be registered");
    }

    [Test]
    public void ChaCha20EncryptDecryptRoundTripViaFactoryTest()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var crypto = ProviderRegistry.Instance.Create<ICryptoProvider>("chacha20-poly1305",
            new ProviderParameters().Set("key", key));

        var plaintext = "Hello, World!"u8.ToArray();
        var nonce = new byte[crypto.NonceSize];
        new Random(123).NextBytes(nonce);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[crypto.TagSize];
        
        crypto.Encrypt(nonce, plaintext, ciphertext, tag);
        
        var decrypted = new byte[plaintext.Length];
        var success = crypto.Decrypt(nonce, ciphertext, tag, decrypted);
        
        Assert.That(success, Is.True);
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }

    [Test]
    public void EnsureRegisteredCanBeCalledMultipleTimesTest()
    {
        // Should not throw
        BouncyCastleProviderRegistration.EnsureRegistered();
        BouncyCastleProviderRegistration.EnsureRegistered();
        BouncyCastleProviderRegistration.EnsureRegistered();
        
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("chacha20-poly1305"), Is.True);
    }
}
