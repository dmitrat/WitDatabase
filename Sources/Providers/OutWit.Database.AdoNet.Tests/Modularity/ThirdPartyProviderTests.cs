using System.Text;
using OutWit.Database.Core;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 follow-up - the construction kit's central claim, executed: a third-party implementation of
/// each provider interface, registered the documented way, driving a real database through SQL.
/// </summary>
/// <remarks>
/// <para>
/// The census and the matrix both work with providers this repository ships. Nothing had ever
/// registered an <see cref="IKeyValueStore"/>, <see cref="IPageCache"/>, <see cref="ICryptoProvider"/>
/// or <see cref="IStorage"/> written outside it and asked whether a database actually runs on it - and
/// that is the claim the whole design rests on.
/// </para>
/// <para>
/// <b>The control is inside every probe, and it is the counter.</b> Each provider counts the calls it
/// receives, and every test asserts that the count is not zero. "Registered and then ignored" is
/// exactly the failure this phase kept finding - the async builder route did it to a registered store
/// until this branch - and a test that only checked the rows would pass while the engine quietly built
/// something else. The rows are checked too: a provider on the path that returns wrong answers is a
/// different failure from one that is not on the path at all.
/// </para>
/// <para>
/// <b>And in the other direction:</b> a provider key that is registered nowhere must be refused at
/// <c>Open</c>. Without that, "the key was honoured" cannot be distinguished from "the key was ignored
/// and the default was built".
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class ThirdPartyProviderTests
{
    #region Constants

    private const string EXPECTED = "1:row1|2:row2|3:row3|4:row4|5:row5|6:row6|7:row7|8:row8";

    private const string STORE_KEY = "third-party-store";
    private const string CACHE_KEY = "third-party-cache";
    private const string CRYPTO_KEY = "third-party-crypto";

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [OneTimeSetUp]
    public void RegisterProviders()
    {
        ProviderRegistry.Instance.RegisterOrReplace<IKeyValueStore>(STORE_KEY, _ => new ProbeStore());

        ProviderRegistry.Instance.RegisterOrReplace<IPageCache>(CACHE_KEY, p =>
            new ProbeCache(p.GetRequired<IStorage>("storage"), p.Get("capacity", 1000)));

        ProviderRegistry.Instance.RegisterOrReplace<ICryptoProvider>(CRYPTO_KEY, p =>
            new ProbeCrypto(p.GetRequired<byte[]>("key")));
    }

    [OneTimeTearDown]
    public void UnregisterProviders()
    {
        ProviderRegistry.Instance.Unregister<IKeyValueStore>(STORE_KEY);
        ProviderRegistry.Instance.Unregister<IPageCache>(CACHE_KEY);
        ProviderRegistry.Instance.Unregister<ICryptoProvider>(CRYPTO_KEY);
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_thirdparty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;

        ProbeStore.Reset();
        ProbeCache.Reset();
        ProbeCrypto.Reset();
        ProbeStorage.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: a store provider key nobody registered is refused at <c>Open</c>. If it were accepted,
    /// every other verdict here would be worthless - a honoured key and an ignored one would look the
    /// same.
    /// </summary>
    [Test]
    public void ControlAnUnregisteredProviderKeyIsRefusedTest()
    {
        using var connection = new WitDbConnection($"Data Source={NewDataSource()};Store=no-such-store");

        Assert.That(() => connection.Open(), Throws.Exception,
            "a store provider that is registered nowhere was accepted, which means the keyword selects " +
            "nothing and every 'the provider was honoured' verdict in this fixture is meaningless");
    }

    #endregion

    #region The probes

    /// <summary>
    /// A third-party <see cref="IKeyValueStore"/>, named in the connection string, running a database.
    /// </summary>
    [Test]
    public void AThirdPartyStoreRunsADatabaseTest()
    {
        using (var connection = new WitDbConnection($"Data Source={NewDataSource()};Store={STORE_KEY}"))
        {
            connection.Open();
            Write(connection);

            Assert.That(Scan(connection), Is.EqualTo(EXPECTED), "the third-party store answered differently");
        }

        TestContext.Out.WriteLine($"THIRD-PARTY store  puts={ProbeStore.Puts}  gets={ProbeStore.Gets}");

        Assert.That(ProbeStore.Puts, Is.GreaterThan(0),
            "the engine answered correctly and the third-party store was never written to - so it was " +
            "registered, selected, and something else did the work");
    }

    /// <summary>
    /// A third-party <see cref="IPageCache"/>, named in the connection string, under the B+Tree store.
    /// </summary>
    [Test]
    public void AThirdPartyPageCacheRunsADatabaseTest()
    {
        using (var connection = new WitDbConnection($"Data Source={NewDataSource()};Cache={CACHE_KEY}"))
        {
            connection.Open();
            Write(connection);

            Assert.That(Scan(connection), Is.EqualTo(EXPECTED), "the third-party cache answered differently");
        }

        TestContext.Out.WriteLine(
            $"THIRD-PARTY cache  gets={ProbeCache.Gets}  creates={ProbeCache.Creates}  flushes={ProbeCache.Flushes}");

        Assert.That(ProbeCache.Gets + ProbeCache.Creates, Is.GreaterThan(0),
            "the chosen page cache was never asked for a page - the store built its own, which is the " +
            "defect Cache=lru had until 12.0.0");
    }

    /// <summary>
    /// A third-party <see cref="ICryptoProvider"/>, named in the connection string.
    /// </summary>
    /// <remarks>
    /// The second assertion is the one that matters: the rows must not be readable in the file. A crypto
    /// provider that is called and does nothing would satisfy a counter and leave the data in clear.
    /// </remarks>
    [Test]
    public void AThirdPartyCryptoProviderRunsADatabaseTest()
    {
        var dataSource = NewDataSource();
        var settings = $"Encryption={CRYPTO_KEY};Password=third-party-secret";

        using (var connection = new WitDbConnection($"Data Source={dataSource};{settings}"))
        {
            connection.Open();
            Write(connection);

            Assert.That(Scan(connection), Is.EqualTo(EXPECTED), "the third-party crypto provider answered differently");
        }

        TestContext.Out.WriteLine(
            $"THIRD-PARTY crypto  encrypts={ProbeCrypto.Encrypts}  decrypts={ProbeCrypto.Decrypts}");

        Assert.That(ProbeCrypto.Encrypts, Is.GreaterThan(0),
            "nothing was encrypted through the provider the connection string named");

        var raw = File.ReadAllBytes(dataSource);
        Assert.That(Encoding.UTF8.GetString(raw), Does.Not.Contain("row1"),
            "the provider was called and the rows are still readable in the file");

        // And it reads back through a second connection, so the transformation is reversible rather
        // than merely destructive.
        using var reopened = new WitDbConnection($"Data Source={dataSource};{settings}");
        reopened.Open();

        Assert.That(Scan(reopened), Is.EqualTo(EXPECTED), "the encrypted database did not read back");
    }

    /// <summary>
    /// A third-party <see cref="IStorage"/>, handed to the builder. Not reachable from a connection
    /// string - there is no keyword for it - so this is the builder API's claim rather than the
    /// provider's.
    /// </summary>
    [Test]
    public void AThirdPartyStorageRunsADatabaseTest()
    {
        var storage = new ProbeStorage(new StorageMemory(DatabaseConstants.DEFAULT_PAGE_SIZE));

        using (var database = new WitDatabaseBuilder().WithStorage(storage).WithBTree().Build())
        using (var engine = new WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Probe (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");

            for (var i = 1; i <= 8; i++)
                engine.Execute($"INSERT INTO Probe (Id, Name) VALUES ({i}, 'row{i}')");

            var rows = engine.Query("SELECT Id, Name FROM Probe ORDER BY Id");
            var scan = string.Join("|", rows.Select(r => $"{r["Id"].AsInt64()}:{r["Name"].AsString()}"));

            Assert.That(scan, Is.EqualTo(EXPECTED), "the third-party storage answered differently");
        }

        TestContext.Out.WriteLine(
            $"THIRD-PARTY storage  writes={ProbeStorage.Writes}  reads={ProbeStorage.Reads}");

        Assert.That(ProbeStorage.Writes, Is.GreaterThan(0),
            "the database ran and never wrote a page through the storage it was given");
    }

    #endregion

    #region The providers

    /// <summary>A third-party store: the registration is what is under test, not the storage.</summary>
    private sealed class ProbeStore : IKeyValueStore
    {
        private static int s_puts;
        private static int s_gets;

        private readonly StoreInMemory m_inner = new();

        public static int Puts => Volatile.Read(ref s_puts);
        public static int Gets => Volatile.Read(ref s_gets);

        public static void Reset()
        {
            Volatile.Write(ref s_puts, 0);
            Volatile.Write(ref s_gets, 0);
        }

        public byte[]? Get(ReadOnlySpan<byte> key)
        {
            Interlocked.Increment(ref s_gets);
            return m_inner.Get(key);
        }

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_gets);
            return m_inner.GetAsync(key, cancellationToken);
        }

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        {
            Interlocked.Increment(ref s_puts);
            m_inner.Put(key, value);
        }

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_puts);
            return m_inner.PutAsync(key, value, cancellationToken);
        }

        public bool Delete(ReadOnlySpan<byte> key) => m_inner.Delete(key);

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.DeleteAsync(key, cancellationToken);

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey) =>
            m_inner.Scan(startKey, endKey);

        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey,
            CancellationToken cancellationToken = default) => m_inner.ScanAsync(startKey, endKey, cancellationToken);

        public void Flush() => m_inner.Flush();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            m_inner.FlushAsync(cancellationToken);

        public string ProviderKey => STORE_KEY;

        public void Dispose() => m_inner.Dispose();
    }

    /// <summary>A third-party page cache, delegating the caching itself to a shipped one.</summary>
    private sealed class ProbeCache : IPageCache
    {
        private static int s_gets;
        private static int s_creates;
        private static int s_flushes;

        private readonly PageCacheShardedClock m_inner;

        public ProbeCache(IStorage storage, int capacity)
        {
            m_inner = new PageCacheShardedClock(storage, capacity);
        }

        public static int Gets => Volatile.Read(ref s_gets);
        public static int Creates => Volatile.Read(ref s_creates);
        public static int Flushes => Volatile.Read(ref s_flushes);

        public static void Reset()
        {
            Volatile.Write(ref s_gets, 0);
            Volatile.Write(ref s_creates, 0);
            Volatile.Write(ref s_flushes, 0);
        }

        public CachedPage GetPage(long pageNumber)
        {
            Interlocked.Increment(ref s_gets);
            return m_inner.GetPage(pageNumber);
        }

        public CachedPage CreatePage(long pageNumber)
        {
            Interlocked.Increment(ref s_creates);
            return m_inner.CreatePage(pageNumber);
        }

        public void MarkDirty(long pageNumber) => m_inner.MarkDirty(pageNumber);

        public void ReleasePage(long pageNumber) => m_inner.ReleasePage(pageNumber);

        public void Evict(long pageNumber) => m_inner.Evict(pageNumber);

        public void FlushAll()
        {
            Interlocked.Increment(ref s_flushes);
            m_inner.FlushAll();
        }

        public ValueTask FlushAllAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_flushes);
            return m_inner.FlushAllAsync(cancellationToken);
        }

        public void Clear() => m_inner.Clear();

        public int Count => m_inner.Count;

        public int DirtyCount => m_inner.DirtyCount;

        public string ProviderKey => CACHE_KEY;

        public void Dispose() => m_inner.Dispose();
    }

    /// <summary>
    /// A third-party crypto provider. It delegates the cryptography to the shipped AES-GCM one - what is
    /// under test is whether a provider registered under a new key is reached at all, not whether a
    /// test can write a cipher.
    /// </summary>
    private sealed class ProbeCrypto : ICryptoProvider
    {
        private static int s_encrypts;
        private static int s_decrypts;

        private readonly byte[] m_key;
        private readonly EncryptorProviderAesGcm m_inner;

        public ProbeCrypto(byte[] key)
        {
            m_key = key;
            m_inner = new EncryptorProviderAesGcm(key);
        }

        public static int Encrypts => Volatile.Read(ref s_encrypts);
        public static int Decrypts => Volatile.Read(ref s_decrypts);

        public static void Reset()
        {
            Volatile.Write(ref s_encrypts, 0);
            Volatile.Write(ref s_decrypts, 0);
        }

        public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
        {
            Interlocked.Increment(ref s_encrypts);
            m_inner.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
        {
            Interlocked.Increment(ref s_decrypts);
            return m_inner.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        public ICryptoProvider Clone() => new ProbeCrypto(m_key);

        public int NonceSize => m_inner.NonceSize;

        public int TagSize => m_inner.TagSize;

        public string ProviderKey => CRYPTO_KEY;

        public void Dispose() => m_inner.Dispose();
    }

    /// <summary>A third-party storage, handed to the builder as an instance.</summary>
    private sealed class ProbeStorage : IStorage
    {
        private static int s_writes;
        private static int s_reads;

        private readonly IStorage m_inner;

        public ProbeStorage(IStorage inner)
        {
            m_inner = inner;
        }

        public static int Writes => Volatile.Read(ref s_writes);
        public static int Reads => Volatile.Read(ref s_reads);

        public static void Reset()
        {
            Volatile.Write(ref s_writes, 0);
            Volatile.Write(ref s_reads, 0);
        }

        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            Interlocked.Increment(ref s_reads);
            m_inner.ReadPage(pageNumber, buffer);
        }

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_reads);
            return m_inner.ReadPageAsync(pageNumber, buffer, cancellationToken);
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            Interlocked.Increment(ref s_writes);
            m_inner.WritePage(pageNumber, buffer);
        }

        public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref s_writes);
            return m_inner.WritePageAsync(pageNumber, buffer, cancellationToken);
        }

        public void Flush() => m_inner.Flush();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            m_inner.FlushAsync(cancellationToken);

        public void SetSize(long pageCount) => m_inner.SetSize(pageCount);

        public int PageSize => m_inner.PageSize;

        public long PageCount => m_inner.PageCount;

        public bool IsReadOnly => m_inner.IsReadOnly;

        public string ProviderKey => "third-party-storage";

        public void Dispose() => m_inner.Dispose();
    }

    #endregion

    #region The workload

    private static void Write(WitDbConnection connection)
    {
        Execute(connection, "CREATE TABLE Probe (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");

        for (var i = 1; i <= 8; i++)
            Execute(connection, $"INSERT INTO Probe (Id, Name) VALUES ({i}, 'row{i}')");
    }

    private static string Scan(WitDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Probe ORDER BY Id";

        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();

        while (reader.Read())
        {
            if (builder.Length > 0)
                builder.Append('|');

            builder.Append($"{reader.GetInt64(0)}:{reader.GetString(1)}");
        }

        return builder.ToString();
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    #endregion

    #region Helpers

    private string NewDataSource()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "thirdparty.witdb");
    }

    #endregion
}
