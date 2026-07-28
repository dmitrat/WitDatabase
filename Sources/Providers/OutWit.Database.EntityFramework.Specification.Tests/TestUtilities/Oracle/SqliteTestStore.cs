using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// The same conformance suites, run against SQLite.
///
/// Its purpose is attribution, not coverage. A suite failing on WitDatabase says nothing on its own
/// about WitDatabase: several of EF Core's specification models need provider capabilities that no
/// file-backed provider has, and they fail on SQLite too. Only a test that passes here and fails on
/// WitDatabase is a WitDatabase defect - the first finding taken from this suite was reported before
/// this oracle existed, and it turned out SQLite behaved identically.
/// </summary>
public class SqliteTestStore : RelationalTestStore
{
    // Per process, for the same reason as WitTestStore: the project's two target frameworks run in
    // parallel and must not share a store file.
    private static readonly string BaseDirectory = Path.Combine(
        Path.GetTempPath(), $"witdb-specification-oracle-{Environment.ProcessId}");

    public static SqliteTestStore GetOrCreate(string name) => new(name, shared: true);

    public static SqliteTestStore Create(string name) => new(name, shared: false);

    private SqliteTestStore(string name, bool shared)
        : base(name, shared, new SqliteConnection(BuildConnectionString(name, shared)))
    {
        FilePath = ResolveFilePath(name, shared);
    }

    public string FilePath { get; }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => builder.UseSqlite((SqliteConnection)Connection);

    protected override async Task InitializeAsync(
        Func<DbContext> createContext,
        Func<DbContext, Task>? seed,
        Func<DbContext, Task>? clean)
    {
        await using var context = createContext();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        if (seed != null)
        {
            await seed(context);
        }
    }

    public override async Task CleanAsync(DbContext context)
    {
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

#if NET10_0_OR_GREATER
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteFile();
    }
#else
    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteFile();
    }

    public override void Dispose()
    {
        base.Dispose();
        DeleteFile();
    }
#endif

    private void DeleteFile()
    {
        try
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string BuildConnectionString(string name, bool shared)
        => $"Data Source={ResolveFilePath(name, shared)}";

    private static string ResolveFilePath(string name, bool shared)
    {
        Directory.CreateDirectory(BaseDirectory);

        var fileName = shared ? name : $"{name}-{Guid.NewGuid():N}";

        return Path.Combine(BaseDirectory, $"{fileName}.db");
    }
}

/// <summary>
/// The hook the oracle's fixtures use to obtain a SQLite store.
/// </summary>
public class SqliteTestStoreFactory : RelationalTestStoreFactory
{
    public static SqliteTestStoreFactory Instance { get; } = new();

    protected SqliteTestStoreFactory()
    {
    }

    public override TestStore Create(string storeName)
        => SqliteTestStore.Create(storeName);

    public override TestStore GetOrCreate(string storeName)
        => SqliteTestStore.GetOrCreate(storeName);

    public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
        => serviceCollection.AddEntityFrameworkSqlite();
}
