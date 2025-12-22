using NUnit.Framework;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// Tests for ProviderRegistry and ProviderFactory.
/// </summary>
[TestFixture]
public class ProviderRegistryTests
{
    #region Test Provider

    private sealed class TestProvider : ICryptoProvider
    {
        public string ProviderKey { get; }
        public string Name { get; }
        
        public TestProvider(string key, string name)
        {
            ProviderKey = key;
            Name = name;
        }

        public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
            => throw new NotImplementedException();

        public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
            => throw new NotImplementedException();

        public ICryptoProvider Clone() => new TestProvider(ProviderKey, Name);

        public int NonceSize => 12;
        public int TagSize => 16;
        
        public void Dispose() { }
    }

    #endregion

    #region Setup

    [TearDown]
    public void TearDown()
    {
        // Clean up test registrations
        ProviderRegistry.Instance.Unregister<ICryptoProvider>("test-provider");
        ProviderRegistry.Instance.Unregister<ICryptoProvider>("test-provider-2");
        ProviderRegistry.Instance.Unregister<ICryptoProvider>("another-provider");
    }

    #endregion

    #region Registration Tests

    [Test]
    public void RegisterAndCreateProviderTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", p.Get("name", "default")));

        var provider = ProviderRegistry.Instance.Create<ICryptoProvider>("test-provider",
            new ProviderParameters().Set("name", "TestName"));

        Assert.That(provider, Is.Not.Null);
        Assert.That(provider.ProviderKey, Is.EqualTo("test-provider"));
        Assert.That(((TestProvider)provider).Name, Is.EqualTo("TestName"));
    }

    [Test]
    public void RegisterDuplicateKeyThrowsTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "first"));

        Assert.Throws<ArgumentException>(() =>
        {
            ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
                p => new TestProvider("test-provider", "second"));
        });
    }

    [Test]
    public void RegisterOrReplaceOverwritesExistingTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "first"));

        ProviderRegistry.Instance.RegisterOrReplace<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "second"));

        var provider = ProviderRegistry.Instance.Create<ICryptoProvider>("test-provider");
        Assert.That(((TestProvider)provider).Name, Is.EqualTo("second"));
    }

    [Test]
    public void UnregisterRemovesProviderTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "test"));

        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("test-provider"), Is.True);

        var removed = ProviderRegistry.Instance.Unregister<ICryptoProvider>("test-provider");
        
        Assert.That(removed, Is.True);
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("test-provider"), Is.False);
    }

    [Test]
    public void UnregisterNonExistentReturnsFalseTest()
    {
        var removed = ProviderRegistry.Instance.Unregister<ICryptoProvider>("non-existent");
        Assert.That(removed, Is.False);
    }

    #endregion

    #region Create Tests

    [Test]
    public void CreateNonExistentProviderThrowsTest()
    {
        var ex = Assert.Throws<ProviderNotFoundException>(() =>
        {
            ProviderRegistry.Instance.Create<ICryptoProvider>("non-existent-provider");
        });

        Assert.That(ex.ProviderKey, Is.EqualTo("non-existent-provider"));
        Assert.That(ex.ProviderType, Is.EqualTo(typeof(ICryptoProvider)));
    }

    [Test]
    public void TryCreateNonExistentProviderReturnsFalseTest()
    {
        var success = ProviderRegistry.Instance.TryCreate<ICryptoProvider>(
            "non-existent-provider", 
            null, 
            out var provider);

        Assert.That(success, Is.False);
        Assert.That(provider, Is.Null);
    }

    [Test]
    public void TryCreateExistingProviderSucceedsTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "test"));

        var success = ProviderRegistry.Instance.TryCreate<ICryptoProvider>(
            "test-provider", 
            null, 
            out var provider);

        Assert.That(success, Is.True);
        Assert.That(provider, Is.Not.Null);
        Assert.That(provider!.ProviderKey, Is.EqualTo("test-provider"));
    }

    [Test]
    public void CreateWithNullParametersUsesEmptyTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", p.Get("name", "default-value")));

        var provider = ProviderRegistry.Instance.Create<ICryptoProvider>("test-provider");
        
        Assert.That(((TestProvider)provider).Name, Is.EqualTo("default-value"));
    }

    #endregion

    #region Query Tests

    [Test]
    public void IsRegisteredReturnsTrueForExistingTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "test"));

        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("test-provider"), Is.True);
    }

    [Test]
    public void IsRegisteredReturnsFalseForNonExistentTest()
    {
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("non-existent"), Is.False);
    }

    [Test]
    public void IsRegisteredCaseInsensitiveTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "test"));

        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("TEST-PROVIDER"), Is.True);
        Assert.That(ProviderRegistry.Instance.IsRegistered<ICryptoProvider>("Test-Provider"), Is.True);
    }

    [Test]
    public void GetRegisteredKeysReturnsAllKeysTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider", 
            p => new TestProvider("test-provider", "test"));
        ProviderRegistry.Instance.Register<ICryptoProvider>("another-provider", 
            p => new TestProvider("another-provider", "test"));

        var keys = ProviderRegistry.Instance.GetRegisteredKeys<ICryptoProvider>();

        Assert.That(keys, Contains.Item("test-provider"));
        Assert.That(keys, Contains.Item("another-provider"));
    }

    #endregion

    #region ProviderParameters Tests

    [Test]
    public void ProviderParametersSetAndGetTest()
    {
        var parameters = new ProviderParameters()
            .Set("string-key", "string-value")
            .Set("int-key", 42)
            .Set("bool-key", true);

        Assert.That(parameters.Get<string>("string-key"), Is.EqualTo("string-value"));
        Assert.That(parameters.Get<int>("int-key"), Is.EqualTo(42));
        Assert.That(parameters.Get<bool>("bool-key"), Is.True);
    }

    [Test]
    public void ProviderParametersGetMissingKeyReturnsDefaultTest()
    {
        var parameters = new ProviderParameters();

        Assert.That(parameters.Get<string>("missing"), Is.Null);
        Assert.That(parameters.Get<int>("missing"), Is.EqualTo(0));
        Assert.That(parameters.Get("missing", 42), Is.EqualTo(42));
    }

    [Test]
    public void ProviderParametersGetRequiredMissingThrowsTest()
    {
        var parameters = new ProviderParameters();

        Assert.Throws<ArgumentException>(() => parameters.GetRequired<string>("missing"));
    }

    [Test]
    public void ProviderParametersGetRequiredWrongTypeThrowsTest()
    {
        var parameters = new ProviderParameters()
            .Set("key", "string-value");

        Assert.Throws<ArgumentException>(() => parameters.GetRequired<int>("key"));
    }

    [Test]
    public void ProviderParametersHasReturnsCorrectlyTest()
    {
        var parameters = new ProviderParameters()
            .Set("exists", "value");

        Assert.That(parameters.Has("exists"), Is.True);
        Assert.That(parameters.Has("missing"), Is.False);
    }

    [Test]
    public void ProviderParametersCaseInsensitiveTest()
    {
        var parameters = new ProviderParameters()
            .Set("MyKey", "value");

        Assert.That(parameters.Get<string>("mykey"), Is.EqualTo("value"));
        Assert.That(parameters.Get<string>("MYKEY"), Is.EqualTo("value"));
        Assert.That(parameters.Has("myKey"), Is.True);
    }

    #endregion

    #region ProviderFactory Tests

    [Test]
    public void ProviderFactoryInstanceIsSingletonTest()
    {
        var factory1 = ProviderFactory<ICryptoProvider>.Instance;
        var factory2 = ProviderFactory<ICryptoProvider>.Instance;

        Assert.That(factory1, Is.SameAs(factory2));
    }

    [Test]
    public void ProviderFactoryDelegatestoRegistryTest()
    {
        ProviderRegistry.Instance.Register<ICryptoProvider>("test-provider-2", 
            p => new TestProvider("test-provider-2", "test"));

        var factory = ProviderFactory<ICryptoProvider>.Instance;
        
        Assert.That(factory.IsRegistered("test-provider-2"), Is.True);
        Assert.That(factory.RegisteredKeys, Contains.Item("test-provider-2"));

        var provider = factory.Create("test-provider-2", new ProviderParameters());
        Assert.That(provider.ProviderKey, Is.EqualTo("test-provider-2"));
    }

    #endregion

    #region ProviderNotFoundException Tests

    [Test]
    public void ProviderNotFoundExceptionContainsInfoTest()
    {
        var ex = new ProviderNotFoundException(
            "missing-key", 
            typeof(ICryptoProvider), 
            new[] { "aes-gcm", "chacha20" });

        Assert.That(ex.ProviderKey, Is.EqualTo("missing-key"));
        Assert.That(ex.ProviderType, Is.EqualTo(typeof(ICryptoProvider)));
        Assert.That(ex.AvailableKeys, Contains.Item("aes-gcm"));
        Assert.That(ex.AvailableKeys, Contains.Item("chacha20"));
        Assert.That(ex.Message, Contains.Substring("missing-key"));
        Assert.That(ex.Message, Contains.Substring("aes-gcm"));
    }

    #endregion
}
