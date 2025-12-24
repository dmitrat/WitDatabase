using Microsoft.JSInterop;
using Moq;

namespace OutWit.Database.Core.IndexedDb.Tests.Mocks;

/// <summary>
/// Mock IJSRuntime for testing IndexedDB operations without a browser.
/// Simulates IndexedDB behavior in memory.
/// </summary>
public sealed class MockJSRuntime : IJSRuntime
{
    #region Fields

    private readonly Dictionary<string, MockIndexedDatabase> m_databases = new();
    private readonly Lock m_lock = new();

    #endregion

    #region IJSRuntime

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (m_lock)
        {
            var result = ProcessCall(identifier, args);
            
            if (result is TValue typedResult)
            {
                return ValueTask.FromResult(typedResult);
            }
            
            // Handle null returns for nullable types
            if (result == null && default(TValue) == null)
            {
                return ValueTask.FromResult(default(TValue)!);
            }
            
            // Handle void returns (for InvokeVoidAsync)
            if (typeof(TValue) == typeof(object) && result == null)
            {
                return ValueTask.FromResult(default(TValue)!);
            }

            throw new InvalidOperationException(
                $"Expected return type {typeof(TValue).Name} but got {result?.GetType().Name ?? "null"}");
        }
    }

    #endregion

    #region Call Processing

    private object? ProcessCall(string identifier, object?[]? args)
    {
        return identifier switch
        {
            "witDb.open" => Open(args),
            "witDb.close" => Close(args),
            "witDb.readPage" => ReadPage(args),
            "witDb.writePage" => WritePage(args),
            "witDb.writePages" => WritePages(args),
            "witDb.getPageCount" => GetPageCount(args),
            "witDb.setPageCount" => SetPageCount(args),
            "witDb.getPageSize" => GetPageSize(args),
            "witDb.setPageSize" => SetPageSize(args),
            "witDb.deleteDatabase" => DeleteDatabase(args),
            "witDb.databaseExists" => DatabaseExists(args),
            "witDb.truncatePages" => TruncatePages(args),
            _ => throw new NotImplementedException($"Unknown JS call: {identifier}")
        };
    }

    private object? Open(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        GetOrCreateDatabase(dbName);
        return null;
    }

    private object? Close(object?[]? args)
    {
        // Just ignore close - data stays in memory
        return null;
    }

    private byte[]? ReadPage(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var pageNumber = GetArg<long>(args, 1);
        
        var db = GetOrCreateDatabase(dbName);
        return db.ReadPage(pageNumber);
    }

    private object? WritePage(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var pageNumber = GetArg<long>(args, 1);
        var data = GetArg<byte[]>(args, 2);
        
        var db = GetOrCreateDatabase(dbName);
        db.WritePage(pageNumber, data);
        return null;
    }

    private object? WritePages(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var pages = args?[1];
        
        // Pages come as array of anonymous objects
        // In real scenario this would be JSON, but Moq passes objects
        if (pages is System.Collections.IEnumerable enumerable)
        {
            var db = GetOrCreateDatabase(dbName);
            foreach (var page in enumerable)
            {
                var pageType = page.GetType();
                var pageNumber = (long)pageType.GetProperty("pageNumber")!.GetValue(page)!;
                var data = (byte[])pageType.GetProperty("data")!.GetValue(page)!;
                db.WritePage(pageNumber, data);
            }
        }
        
        return null;
    }

    private long GetPageCount(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var db = GetOrCreateDatabase(dbName);
        return db.PageCount;
    }

    private object? SetPageCount(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var count = GetArg<long>(args, 1);
        
        var db = GetOrCreateDatabase(dbName);
        db.PageCount = count;
        return null;
    }

    private int GetPageSize(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var db = GetOrCreateDatabase(dbName);
        return db.PageSize;
    }

    private object? SetPageSize(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var pageSize = GetArg<int>(args, 1);
        
        var db = GetOrCreateDatabase(dbName);
        db.PageSize = pageSize;
        return null;
    }

    private object? DeleteDatabase(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        m_databases.Remove(dbName);
        return null;
    }

    private bool DatabaseExists(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        return m_databases.ContainsKey(dbName);
    }

    private object? TruncatePages(object?[]? args)
    {
        var dbName = GetArg<string>(args, 0);
        var newPageCount = GetArg<long>(args, 1);
        
        var db = GetOrCreateDatabase(dbName);
        db.Truncate(newPageCount);
        return null;
    }

    #endregion

    #region Tools

    private MockIndexedDatabase GetOrCreateDatabase(string name)
    {
        if (!m_databases.TryGetValue(name, out var db))
        {
            db = new MockIndexedDatabase(name);
            m_databases[name] = db;
        }
        return db;
    }

    private static T GetArg<T>(object?[]? args, int index)
    {
        if (args == null || args.Length <= index)
            throw new ArgumentException($"Missing argument at index {index}");
        
        var value = args[index];
        
        if (value is T typedValue)
            return typedValue;
        
        // Handle numeric conversions
        if (typeof(T) == typeof(long) && value is int intValue)
            return (T)(object)(long)intValue;
        
        if (typeof(T) == typeof(int) && value is long longValue)
            return (T)(object)(int)longValue;
        
        throw new ArgumentException(
            $"Argument at index {index} is {value?.GetType().Name ?? "null"}, expected {typeof(T).Name}");
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the mock databases for inspection in tests.
    /// </summary>
    public IReadOnlyDictionary<string, MockIndexedDatabase> Databases => m_databases;

    #endregion
}

/// <summary>
/// Represents a mock IndexedDB database.
/// </summary>
public sealed class MockIndexedDatabase
{
    #region Fields

    private readonly Dictionary<long, byte[]> m_pages = new();

    #endregion

    #region Constructors

    public MockIndexedDatabase(string name)
    {
        Name = name;
    }

    #endregion

    #region Operations

    public byte[]? ReadPage(long pageNumber)
    {
        return m_pages.TryGetValue(pageNumber, out var data) ? data : null;
    }

    public void WritePage(long pageNumber, byte[] data)
    {
        m_pages[pageNumber] = (byte[])data.Clone();
    }

    public void Truncate(long newPageCount)
    {
        var keysToRemove = m_pages.Keys.Where(k => k >= newPageCount).ToList();
        foreach (var key in keysToRemove)
        {
            m_pages.Remove(key);
        }
        PageCount = newPageCount;
    }

    #endregion

    #region Properties

    public string Name { get; }
    public long PageCount { get; set; }
    public int PageSize { get; set; }

    /// <summary>
    /// Gets all stored pages for inspection.
    /// </summary>
    public IReadOnlyDictionary<long, byte[]> Pages => m_pages;

    #endregion
}
