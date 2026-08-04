using System.Text;
using OutWit.Database.Core;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 follow-up - the two provider packages no instrument had ever touched:
/// <c>OutWit.Database.Core.BouncyCastle</c> and <c>OutWit.Database.Core.IndexedDb</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both ship as packages, both register providers, and neither appeared in the census, the matrix or
/// the mismatch grid. They are answered differently here, because only one of them can be run on a
/// build machine:
/// </para>
/// <list type="bullet">
/// <item><b>BouncyCastle</b> is a crypto provider and needs nothing but a connection string, so it gets
/// the phase's ordinary treatment - the workload, a reopen, and the two questions encryption has to
/// answer: is the file unreadable without the password, and does the right password read it back.</item>
/// <item><b>IndexedDb</b> is a browser storage and cannot run here at all: <c>StorageIndexedDb</c> talks
/// to JavaScript through <c>IJSRuntime</c>. What it rests on, though, can be run - the claim that a
/// database can be built and used over a storage that refuses <b>every</b> synchronous operation. That
/// is what the stand-in below is: an <see cref="IAsyncOnlyStorage"/> whose synchronous methods throw.
/// If the asynchronous build route calls one, the WASM story is broken whatever the interop does, and
/// this fixture says so without a browser.</item>
/// </list>
/// <para>
/// The stand-in is deliberately not a mock of IndexedDb. It measures the engine's side of the contract,
/// which is the half this repository owns.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class AlternativeProviderPackagesTests
{
    #region Constants

    private const string EXPECTED = "1:row1|2:row2|3:row3|4:row4|5:row5|6:row6|7:row7|8:row8";

    /// <summary>The provider key <c>OutWit.Database.Core.BouncyCastle</c> registers.</summary>
    private const string CHACHA = "chacha20-poly1305";

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    /// <summary>
    /// Loads the BouncyCastle package's registration deliberately, because referencing the assembly is
    /// <b>not</b> enough - measured, and it is the fixture's first finding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The package registers its provider from a <c>[ModuleInitializer]</c>, and the CLR runs one when
    /// the assembly is <i>loaded</i> - which it is not, if nothing ever touches a type in it. A project
    /// or package reference alone therefore registers nothing: on the first run of this fixture,
    /// <c>Encryption=chacha20-poly1305</c> was refused with
    /// <c>Encryption provider 'chacha20-poly1305' is not registered. Available: aes-gcm</c>.
    /// </para>
    /// <para>
    /// That refusal is legible and correct - the phase's rule is satisfied - but the connection-string
    /// documentation offers the key as if referencing the package were enough. The route the package's
    /// own README documents, <c>WithBouncyCastleEncryption(...)</c>, is an extension method on a type in
    /// the assembly, so it loads it as a side effect and works. A consumer who only writes connection
    /// strings has no such side effect, and now has a documented step instead: call
    /// <c>BouncyCastleProviderRegistration.EnsureRegistered()</c> once at startup.
    /// </para>
    /// </remarks>
    [OneTimeSetUp]
    public void EnsureBouncyCastleIsRegistered()
    {
        Core.BouncyCastle.BouncyCastleProviderRegistration.EnsureRegistered();
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_altpkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;
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

    #region BouncyCastle

    /// <summary>
    /// Control: the provider is registered once the package has been loaded on purpose. If this fails,
    /// every ChaCha verdict below is about a missing registration rather than about the provider.
    /// </summary>
    [Test]
    public void ControlTheBouncyCastleProviderIsRegisteredTest()
    {
        Assert.That(Core.Providers.ProviderRegistry.Instance.IsRegistered<ICryptoProvider>(CHACHA), Is.True,
            $"'{CHACHA}' is not registered even after EnsureRegistered() - the tests below would be " +
            "measuring the wrong thing");
    }

    /// <summary>
    /// The workload through ChaCha20-Poly1305, named in the connection string exactly as AES-GCM is.
    /// </summary>
    [Test]
    public void ChaCha20Poly1305RunsADatabaseTest()
    {
        var dataSource = NewDataSource();
        var settings = $"Encryption={CHACHA};Password=bouncy-secret";

        using (var connection = new WitDbConnection($"Data Source={dataSource};{settings}"))
        {
            connection.Open();
            Write(connection);

            Assert.That(Scan(connection), Is.EqualTo(EXPECTED), "ChaCha20-Poly1305 answered differently");
        }

        // Reopened, because encryption that cannot be read back is not encryption, it is loss.
        using (var reopened = new WitDbConnection($"Data Source={dataSource};{settings}"))
        {
            reopened.Open();
            Assert.That(Scan(reopened), Is.EqualTo(EXPECTED), "the ChaCha database did not survive a reopen");
        }

        var raw = File.ReadAllBytes(dataSource);
        Assert.That(Encoding.UTF8.GetString(raw), Does.Not.Contain("row1"),
            "the rows are readable in the file - the provider was selected and encrypted nothing");
    }

    /// <summary>
    /// And the other direction: the wrong password must not read it, and neither must no password.
    /// </summary>
    [Test]
    public void AChaChaDatabaseIsNotReadableWithoutItsPasswordTest()
    {
        var dataSource = NewDataSource();

        using (var connection = new WitDbConnection($"Data Source={dataSource};Encryption={CHACHA};Password=bouncy-secret"))
        {
            connection.Open();
            Write(connection);
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => Open($"Data Source={dataSource};Encryption={CHACHA};Password=wrong-secret"),
                Throws.Exception, "the wrong password opened a ChaCha database");

            Assert.That(() => Open($"Data Source={dataSource}"),
                Throws.Exception, "no password at all opened a ChaCha database");
        });
    }

    #endregion

    #region A storage that has no synchronous operations at all

    /// <summary>
    /// Control: the stand-in really does refuse synchronous work, so the test below is measuring
    /// something. A stand-in that quietly allowed a synchronous read would make the asynchronous route
    /// look clean whatever it did.
    /// </summary>
    [Test]
    public void ControlTheAsyncOnlyStorageRefusesSynchronousWorkTest()
    {
        using var storage = new AsyncOnlyStorage();

        Assert.Multiple(() =>
        {
            Assert.That(() => storage.ReadPage(0, new byte[storage.PageSize]), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => storage.WritePage(0, new byte[storage.PageSize]), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => storage.Flush(), Throws.TypeOf<NotSupportedException>());
        });
    }

    /// <summary>
    /// Control: the synchronous build refuses such a storage rather than throwing from somewhere deep
    /// inside it.
    /// </summary>
    [Test]
    public void TheSynchronousBuildRefusesAnAsyncOnlyStorageTest()
    {
        var storage = new AsyncOnlyStorage();

        try
        {
            Assert.That(() => new WitDatabaseBuilder().WithStorage(storage).WithBTree().Build(),
                Throws.InvalidOperationException.With.Message.Contains("BuildAsync"));
        }
        finally
        {
            storage.Dispose();
        }
    }

    /// <summary>
    /// The half of the claim that holds: a database can be <b>built</b> over a storage with no
    /// synchronous operations at all, and the build writes only asynchronously.
    /// </summary>
    [Test]
    public async Task ADatabaseCanBeBuiltOverAStorageWithNoSynchronousOperationsTest()
    {
        var storage = new AsyncOnlyStorage();

        var database = await new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .BuildAsync();

        TestContext.Out.WriteLine(
            $"ASYNC-ONLY STORAGE  build: async reads={storage.AsyncReads}  async writes={storage.AsyncWrites}");

        Assert.That(storage.IsInitialized, Is.True, "the build never initialised the storage asynchronously");

        Assert.That(storage.AsyncWrites, Is.GreaterThan(0),
            "the build wrote nothing at all through the storage, so this probe is not exercising the " +
            "path it exists for");

        // Not disposed: closing is the pinned defect below, and a Dispose here would throw and hide
        // what this test measured.
        GC.KeepAlive(database);
    }

    /// <summary>
    /// Probe: the other half does not hold. The database can be built and a table can be created; the
    /// first <b>row</b> throws, and so does closing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PINS A DEFECT, NOT CORRECT BEHAVIOUR.</b> <c>OutWit.Database.Core.IndexedDb</c> exists so a
    /// database can live in a browser, where no synchronous I/O is available. Measured, the boundary is
    /// exactly one statement wide: the build is asynchronous throughout and <c>CREATE TABLE</c>
    /// survives - it survives because it writes <b>nothing</b>, the storage's write count is 1 before it
    /// and 1 after - and the <b>first INSERT</b> throws. Its implicit per-statement transaction commits, the
    /// commit flushes, and <c>PageManager.Flush</c> writes the header through
    /// <c>IStorage.WritePage</c> - the synchronous one - before calling <c>IStorage.Flush</c>, also
    /// synchronous. Every close ends in the same place.
    /// </para>
    /// <para>
    /// <b>The chain has four missing links</b>, which is why this is a hand-forward rather than a patch:
    /// <c>PageManager</c> has <c>FlushAsync</c> but no <c>DisposeAsync</c>, and its flush writes the
    /// header synchronously; <c>StoreBTree.DisposeAsync</c> calls that synchronous <c>Dispose</c>, under
    /// a comment claiming it is safe because the cache flush "uses async internally";
    /// <c>BTreeConcurrentStore</c> - which since 12.0.0 wraps <b>every</b> B+Tree store - implements no
    /// <c>IAsyncDisposable</c>, so an asynchronous disposal degrades to a synchronous one at that link;
    /// and neither does <c>MvccTransactionalStore</c>, the default transaction model. Above all of them
    /// <c>WitSqlEngine</c> is <c>IDisposable</c> only, so a consumer has no asynchronous close to call.
    /// </para>
    /// <para>
    /// <b>How this was measured, and one reading was wrong on the way.</b> The first version of the test
    /// closed the database in a <c>finally</c>, so the exception from the close replaced the exception
    /// from the workload and the run reported the <i>statement</i> failing. Re-measured with the cleanup
    /// removed, the statement succeeds - <b>five whole-fixture runs out of five, and three in
    /// isolation</b> - and the close is what throws. A cleanup that can throw hides what the test came
    /// to measure.
    /// </para>
    /// <para>
    /// When the chain is built, invert the assertion: the close should succeed.
    /// </para>
    /// </remarks>
    [Test]
    public async Task WritingToADatabaseOverAnAsyncOnlyStorageThrowsTest()
    {
        var storage = new AsyncOnlyStorage();

        var database = await new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .BuildAsync();

        var engine = new WitSqlEngine(database, ownsStore: true);

        var afterBuild = storage.AsyncWrites;

        // The table is created without a synchronous write - measured, and it is why the boundary has
        // to be stated precisely rather than as "it does not work".
        engine.Execute("CREATE TABLE Probe (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");

        TestContext.Out.WriteLine(
            $"ASYNC-ONLY STORAGE  after CREATE TABLE: async writes {afterBuild} -> {storage.AsyncWrites}, " +
            $"async reads={storage.AsyncReads}");

        // PINS A DEFECT, NOT CORRECT BEHAVIOUR. The engine has no asynchronous execution path at all -
        // WitSqlEngine offers Execute and Query and nothing else, and the ADO layer's
        // ExecuteNonQueryAsync is Task.Run around the synchronous one, which in a browser is worse than
        // useless. Until that exists, a write cannot avoid the synchronous flush its commit performs.
        Assert.That(() => engine.Execute("INSERT INTO Probe (Id, Name) VALUES (1, 'row1')"),
            Throws.TypeOf<NotSupportedException>(),
            "a synchronous write no longer reaches a synchronous storage call - re-measure and invert this pin");
    }

    /// <summary>
    /// Closing a database over such a storage asynchronously must not touch a synchronous storage
    /// member - the stand-in throws on every one of them, so success here is proof rather than an
    /// absence of evidence.
    /// </summary>
    /// <remarks>
    /// The chain this exercises has six links, all of which had to be built: both page caches flush
    /// synchronously in <c>Dispose</c>; <c>PageManager</c> had no <c>DisposeAsync</c> and its
    /// synchronous one writes the header through <c>IStorage.WritePage</c>;
    /// <c>StoreBTree.DisposeAsync</c> called that synchronous <c>Dispose</c> under a comment claiming it
    /// was safe; <c>BTreeConcurrentStore</c> - which since 12.0.0 wraps every B+Tree store - implemented
    /// no <c>IAsyncDisposable</c>, so an asynchronous disposal degraded at that link; nor did
    /// <c>MvccTransactionalStore</c>, the default transaction model; and <c>WitSqlEngine</c> was
    /// <c>IDisposable</c> only, so a consumer had nothing asynchronous to call.
    /// </remarks>
    [Test]
    public async Task ClosingADatabaseOverAnAsyncOnlyStorageWorksTest()
    {
        var storage = new AsyncOnlyStorage();

        var database = await new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .BuildAsync();

        Assert.That(async () => await database.DisposeAsync(), Throws.Nothing,
            "closing a database over a storage with no synchronous operations still reaches one");

        TestContext.Out.WriteLine(
            $"ASYNC-ONLY STORAGE  after close: async writes={storage.AsyncWrites}, reads={storage.AsyncReads}");
    }

    /// <summary>
    /// The same through the surface a consumer actually holds: the engine, which owns the database.
    /// </summary>
    /// <remarks>
    /// The last link, and the one that made all the others unreachable - <c>WitSqlEngine</c> was
    /// <c>IDisposable</c> only, so however asynchronous everything below it became, a consumer had
    /// nothing asynchronous to call.
    /// </remarks>
    [Test]
    public async Task ClosingThroughTheEngineOverAnAsyncOnlyStorageWorksTest()
    {
        var storage = new AsyncOnlyStorage();

        var database = await new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .BuildAsync();

        var engine = new WitSqlEngine(database, ownsStore: true);

        Assert.That(async () => await engine.DisposeAsync(), Throws.Nothing,
            "closing through the engine over a storage with no synchronous operations still reaches one");
    }

    /// <summary>
    /// The same, with the default transaction model rather than the lock-based one, because
    /// <c>MvccTransactionalStore</c> is a separate link in the chain and the two must not diverge.
    /// </summary>
    [Test]
    public async Task ClosingAnMvccDatabaseOverAnAsyncOnlyStorageWorksTest()
    {
        var storage = new AsyncOnlyStorage();

        var database = await new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .WithMvcc()
            .BuildAsync();

        Assert.That(async () => await database.DisposeAsync(), Throws.Nothing,
            "closing an MVCC database over a storage with no synchronous operations still reaches one");
    }

    #endregion

    #region The stand-in

    /// <summary>
    /// A storage that can only be used asynchronously - the shape <c>StorageIndexedDb</c> has, without
    /// the browser. Every synchronous member throws, so a build route that takes one is caught here
    /// rather than in a WASM host.
    /// </summary>
    private sealed class AsyncOnlyStorage : IStorage, IAsyncOnlyStorage, IAsyncInitializable
    {
        private readonly StorageMemory m_inner = new(DatabaseConstants.DEFAULT_PAGE_SIZE);

        private int m_asyncReads;
        private int m_asyncWrites;

        public int AsyncReads => Volatile.Read(ref m_asyncReads);
        public int AsyncWrites => Volatile.Read(ref m_asyncWrites);

        public bool RequiresAsyncOperations => true;

        public bool IsInitialized { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            IsInitialized = true;
            return ValueTask.CompletedTask;
        }

        public void ReadPage(long pageNumber, Span<byte> buffer) =>
            throw new NotSupportedException("This storage has no synchronous read.");

        public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_asyncReads);
            return m_inner.ReadPageAsync(pageNumber, buffer, cancellationToken);
        }

        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer) =>
            throw new NotSupportedException("This storage has no synchronous write.");

        public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_asyncWrites);
            return m_inner.WritePageAsync(pageNumber, buffer, cancellationToken);
        }

        public void Flush() => throw new NotSupportedException("This storage has no synchronous flush.");

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            m_inner.FlushAsync(cancellationToken);

        public void SetSize(long pageCount) =>
            throw new NotSupportedException("This storage has no synchronous resize.");

        public ValueTask SetSizeAsync(long pageCount, CancellationToken cancellationToken = default)
        {
            // The inner storage is an ordinary in-memory one; only the OUTER surface is asynchronous,
            // which is the whole point - what is under test is what the engine calls, not what a
            // stand-in does behind it.
            m_inner.SetSize(pageCount);
            return ValueTask.CompletedTask;
        }

        public int PageSize => m_inner.PageSize;

        public long PageCount => m_inner.PageCount;

        public bool IsReadOnly => m_inner.IsReadOnly;

        public string ProviderKey => "async-only";

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

    private static string Open(string connectionString)
    {
        using var connection = new WitDbConnection(connectionString);
        connection.Open();

        return Scan(connection);
    }

    #endregion

    #region Helpers

    private string NewDataSource()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "altpkg.witdb");
    }

    #endregion
}
