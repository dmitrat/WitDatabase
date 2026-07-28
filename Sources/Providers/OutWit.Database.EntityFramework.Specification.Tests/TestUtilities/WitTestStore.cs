using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.AdoNet;
using OutWit.Database.Core.Utils;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;

/// <summary>
/// The database a specification fixture runs against. Backed by a file under the test run's own
/// directory rather than by the in-memory store: the suite opens several contexts over one store and
/// asserts what survives between them, which an in-memory store scoped to a connection cannot show.
/// </summary>
public class WitTestStore : RelationalTestStore
{
    private static readonly string BaseDirectory = Path.Combine(
        Path.GetTempPath(), "witdb-specification-tests");

    public static WitTestStore GetOrCreate(string name) => new(name, shared: true);

    public static WitTestStore Create(string name) => new(name, shared: false);

    private WitTestStore(string name, bool shared)
        : base(name, shared, new WitDbConnection(BuildConnectionString(name, shared)))
    {
        FilePath = ResolveFilePath(name, shared);
    }

    /// <summary>
    /// The file the store lives in. Held separately because deleting the database means deleting
    /// this file - there is no DROP DATABASE to issue over the connection.
    /// </summary>
    public string FilePath { get; }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => builder.UseWitDb((WitDbConnection)Connection);

    /// <summary>
    /// Starts every store from nothing. A shared store is keyed by name and reused across the
    /// fixtures in a class, so leaving a previous run's file in place would let one run's rows
    /// decide another run's assertions.
    /// </summary>
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
        // The whole database, not just the data file: an index directory left behind is picked up
        // by the next run under the same store name, and its entries then reject that run's rows.
        // Best effort - a test run must not fail because the previous run's file was still locked.
        try
        {
            DatabaseFiles.Delete(FilePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string BuildConnectionString(string name, bool shared)
        => $"Data Source={ResolveFilePath(name, shared)}";

    private static string ResolveFilePath(string name, bool shared)
    {
        Directory.CreateDirectory(BaseDirectory);

        // A non-shared store belongs to exactly one fixture, so it gets a unique file: two fixtures
        // asking for the same store name must not land on the same database.
        var fileName = shared ? name : $"{name}-{Guid.NewGuid():N}";

        return Path.Combine(BaseDirectory, $"{fileName}.witdb");
    }
}
