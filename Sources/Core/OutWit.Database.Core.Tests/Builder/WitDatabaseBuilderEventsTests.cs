using System.Text;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Tests.Builder;

[TestFixture]
public class WitDatabaseBuilderEventsTests : IDisposable
{
    #region Fields

    private string? m_testDir;

    #endregion

    #region Setup/Teardown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"builder_events_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (m_testDir != null && Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    #endregion

    #region OnValidating Event Tests

    [Test]
    public void OnValidatingEventFiredOnBuildTest()
    {
        var builder = new WitDatabaseBuilder();
        var eventFired = false;
        
        builder.OnValidating += options =>
        {
            eventFired = true;
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public async Task OnValidatingEventFiredOnBuildAsyncTest()
    {
        var builder = new WitDatabaseBuilder();
        var eventFired = false;
        
        builder.OnValidating += options =>
        {
            eventFired = true;
        };
        
        using var db = await builder
            .WithMemoryStorage()
            .WithBTree()
            .BuildAsync();
        
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void OnValidatingEventReceivesOptionsTest()
    {
        var builder = new WitDatabaseBuilder();
        WitDatabaseBuilderOptions? receivedOptions = null;
        
        builder.OnValidating += options =>
        {
            receivedOptions = options;
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(receivedOptions, Is.Not.Null);
        Assert.That(receivedOptions!.UseMemoryStorage, Is.True);
        Assert.That(receivedOptions.UseBTree, Is.True);
    }

    [Test]
    public void OnValidatingEventCanThrowToFailBuildTest()
    {
        var builder = new WitDatabaseBuilder();
        
        builder.OnValidating += options =>
        {
            throw new InvalidOperationException("Custom validation failed");
        };
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder
                .WithMemoryStorage()
                .WithBTree()
                .Build());
        
        Assert.That(ex.Message, Is.EqualTo("Custom validation failed"));
    }

    [Test]
    public async Task OnValidatingEventCanThrowToFailBuildAsyncTest()
    {
        var builder = new WitDatabaseBuilder();
        
        builder.OnValidating += options =>
        {
            throw new InvalidOperationException("Custom validation failed async");
        };
        
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await builder
                .WithMemoryStorage()
                .WithBTree()
                .BuildAsync());
        
        Assert.That(ex.Message, Is.EqualTo("Custom validation failed async"));
    }

    [Test]
    public void OnValidatingEventMultipleSubscribersTest()
    {
        var builder = new WitDatabaseBuilder();
        var subscriber1Called = false;
        var subscriber2Called = false;
        
        builder.OnValidating += options => subscriber1Called = true;
        builder.OnValidating += options => subscriber2Called = true;
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(subscriber1Called, Is.True);
        Assert.That(subscriber2Called, Is.True);
    }

    [Test]
    public void OnValidatingEventCanInspectLsmTreeOptionTest()
    {
        var builder = new WitDatabaseBuilder();
        var usesLsmTree = false;
        
        builder.OnValidating += options =>
        {
            usesLsmTree = options.UseLsmTree;
            if (usesLsmTree)
            {
                throw new InvalidOperationException("LSM-Tree not allowed in this environment");
            }
        };
        
        // Should work with BTree
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(usesLsmTree, Is.False);
    }

    [Test]
    public void OnValidatingEventCanInspectEncryptionOptionTest()
    {
        var builder = new WitDatabaseBuilder();
        var isEncrypted = false;
        
        builder.OnValidating += options =>
        {
            isEncrypted = options.HasEncryption;
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("password")
            .Build();
        
        Assert.That(isEncrypted, Is.True);
    }

    #endregion

    #region OnStoreBuilt Event Tests

    [Test]
    public void OnStoreBuiltEventFiredOnBuildTest()
    {
        var builder = new WitDatabaseBuilder();
        var eventFired = false;
        
        builder.OnStoreBuilt += store =>
        {
            eventFired = true;
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public async Task OnStoreBuiltEventFiredOnBuildAsyncTest()
    {
        var builder = new WitDatabaseBuilder();
        var eventFired = false;
        
        builder.OnStoreBuilt += store =>
        {
            eventFired = true;
        };
        
        using var db = await builder
            .WithMemoryStorage()
            .WithBTree()
            .BuildAsync();
        
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void OnStoreBuiltEventReceivesStoreTest()
    {
        var builder = new WitDatabaseBuilder();
        IKeyValueStore? receivedStore = null;
        
        builder.OnStoreBuilt += store =>
        {
            receivedStore = store;
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(receivedStore, Is.Not.Null);
        Assert.That(receivedStore!.ProviderKey, Is.EqualTo("btree"));
    }

    [Test]
    public void OnStoreBuiltEventCalledAfterValidationTest()
    {
        var builder = new WitDatabaseBuilder();
        var callOrder = new List<string>();
        
        builder.OnValidating += options =>
        {
            callOrder.Add("validating");
        };
        
        builder.OnStoreBuilt += store =>
        {
            callOrder.Add("store_built");
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(callOrder.Count, Is.EqualTo(2));
        Assert.That(callOrder[0], Is.EqualTo("validating"));
        Assert.That(callOrder[1], Is.EqualTo("store_built"));
    }

    [Test]
    public void OnStoreBuiltEventNotCalledIfValidationFailsTest()
    {
        var builder = new WitDatabaseBuilder();
        var storeBuiltCalled = false;
        
        builder.OnValidating += options =>
        {
            throw new InvalidOperationException("Validation failed");
        };
        
        builder.OnStoreBuilt += store =>
        {
            storeBuiltCalled = true;
        };
        
        Assert.Throws<InvalidOperationException>(() =>
            builder
                .WithMemoryStorage()
                .WithBTree()
                .Build());
        
        Assert.That(storeBuiltCalled, Is.False);
    }

    #endregion

    #region BuildAsync Tests

    [Test]
    public async Task BuildAsyncCreatesWorkingDatabaseTest()
    {
        var builder = new WitDatabaseBuilder();
        
        using var db = await builder
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .BuildAsync();
        
        await db.PutAsync(System.Text.Encoding.UTF8.GetBytes("key"), System.Text.Encoding.UTF8.GetBytes("value"));
        
        var value = await db.GetAsync(System.Text.Encoding.UTF8.GetBytes("key"));
        Assert.That(value, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(value!), Is.EqualTo("value"));
    }

    [Test]
    public async Task BuildAsyncWithFileStorageTest()
    {
        var dbPath = Path.Combine(m_testDir!, $"async_db_{Guid.NewGuid():N}.db");
        var builder = new WitDatabaseBuilder();
        
        using var db = await builder
            .WithFilePath(dbPath)
            .WithBTree()
            .BuildAsync();
        
        await db.PutAsync(System.Text.Encoding.UTF8.GetBytes("key"), System.Text.Encoding.UTF8.GetBytes("value"));
        await db.FlushAsync();
        
        Assert.That(File.Exists(dbPath), Is.True);
    }

    [Test]
    public async Task BuildAsyncWithEncryptionTest()
    {
        var builder = new WitDatabaseBuilder();
        
        using var db = await builder
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption("password")
            .BuildAsync();
        
        await db.PutAsync(System.Text.Encoding.UTF8.GetBytes("secret-key"), System.Text.Encoding.UTF8.GetBytes("secret-value"));
        
        var value = await db.GetAsync(System.Text.Encoding.UTF8.GetBytes("secret-key"));
        Assert.That(value, Is.Not.Null);
    }

    #endregion

    #region BuildStoreAsync Tests

    [Test]
    public async Task BuildStoreAsyncTest()
    {
        var builder = new WitDatabaseBuilder();
        
        var store = await builder
            .WithMemoryStorage()
            .WithBTree()
            .BuildStoreAsync();
        
        await store.PutAsync(System.Text.Encoding.UTF8.GetBytes("key"), System.Text.Encoding.UTF8.GetBytes("value"));
        
        var value = await store.GetAsync(System.Text.Encoding.UTF8.GetBytes("key"));
        Assert.That(value, Is.Not.Null);
        
        store.Dispose();
    }

    #endregion

    #region BuildTransactionalStoreAsync Tests

    [Test]
    public async Task BuildTransactionalStoreAsyncTest()
    {
        var builder = new WitDatabaseBuilder();
        
        var store = await builder
            .WithMemoryStorage()
            .WithBTree()
            .BuildTransactionalStoreAsync();
        
        using var tx = store.BeginTransaction();
        tx.Put(System.Text.Encoding.UTF8.GetBytes("tx-key"), System.Text.Encoding.UTF8.GetBytes("tx-value"));
        tx.Commit();
        
        var value = store.Get(System.Text.Encoding.UTF8.GetBytes("tx-key"));
        Assert.That(value, Is.Not.Null);
        
        store.Dispose();
    }

    #endregion

    #region Extension Package Validation Pattern Tests

    [Test]
    public void ExtensionPackageCanValidateConfigurationTest()
    {
        // This simulates how an extension package like IndexedDb would use OnValidating
        var builder = new WitDatabaseBuilder();
        
        // Simulate IndexedDb extension validation
        builder.OnValidating += options =>
        {
            // IndexedDb doesn't support LSM-Tree
            if (options.UseLsmTree)
            {
                throw new InvalidOperationException(
                    "IndexedDB storage is not compatible with LSM-Tree. Use .WithBTree() instead.");
            }
            
            // Auto-disable file locking for browser environment via TransactionParameters
            options.TransactionParameters.Set("fileLocking", false);
        };
        
        // Should work with BTree
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(builder.Options.EnableFileLocking, Is.False);
    }

    [Test]
    public void ExtensionPackageCanLogStoreCreationTest()
    {
        var builder = new WitDatabaseBuilder();
        var logMessages = new List<string>();
        
        builder.OnStoreBuilt += store =>
        {
            logMessages.Add($"Store created: {store.ProviderKey}");
        };
        
        using var db = builder
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        
        Assert.That(logMessages.Count, Is.EqualTo(1));
        Assert.That(logMessages[0], Is.EqualTo("Store created: btree"));
    }

    #endregion
}
