# OutWit.Database.EntityFramework - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.EntityFramework.

---

## Version 2.0 - Planned Features

### Priority 1: High Value

| Feature | Description |
|---------|-------------|
| Compiled Queries | Pre-compiled LINQ queries for performance |
| Query Splitting | Split large queries to avoid cartesian explosion |
| Temporal Tables | System-versioned temporal tables |

### Priority 2: Enhancements

| Feature | Description |
|---------|-------------|
| Full-Text Search | MATCH/AGAINST support (when Core supports it) |
| Spatial Data | Basic geometry support |
| HierarchyId | Hierarchical data type support |
| Graph Queries | MATCH pattern for graph relationships |

### Priority 3: Tooling

| Feature | Description |
|---------|-------------|
| Design-Time Services | Better Visual Studio integration |
| Scaffold Improvements | Better handling of complex schemas |
| Migration Bundles | Self-contained migration executables |

---

## Implementation Details

### Compiled Queries (Priority 1)

Pre-compile LINQ queries for repeated execution:

```csharp
public static class WitDbCompiledQueries
{
    // Sync version
    public static Func<TContext, TParam, TResult> Compile<TContext, TParam, TResult>(
        Expression<Func<TContext, TParam, TResult>> query)
        where TContext : DbContext;
    
    // Async version
    public static Func<TContext, TParam, Task<TResult>> CompileAsync<TContext, TParam, TResult>(
        Expression<Func<TContext, TParam, TResult>> query)
        where TContext : DbContext;
}

// Usage
private static readonly Func<AppDbContext, int, User?> _getUserById =
    WitDbCompiledQueries.Compile((AppDbContext ctx, int id) =>
        ctx.Users.FirstOrDefault(u => u.Id == id));

public User? GetUser(int id) => _getUserById(context, id);
```

### Temporal Tables (Priority 1)

System-versioned temporal tables for audit history:

```csharp
// Model configuration
modelBuilder.Entity<Product>()
    .ToTable("Products", b => b.IsTemporal());

// Querying historical data
var productsAsOf = context.Products
    .TemporalAsOf(DateTime.UtcNow.AddDays(-7))
    .ToList();

var productHistory = context.Products
    .TemporalAll()
    .Where(p => p.Id == productId)
    .OrderBy(p => EF.Property<DateTime>(p, "PeriodStart"))
    .ToList();
```

### Query Splitting (Priority 1)

Split queries to avoid cartesian explosion with multiple collections:

```csharp
// Configuration
modelBuilder.Entity<Blog>()
    .Navigation(b => b.Posts)
    .AutoInclude()
    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

// Per-query
var blogs = context.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Tags)
    .AsSplitQuery()
    .ToList();
```

---

## Additional LINQ Translations

| Method | SQL Translation |
|--------|-----------------|
| `EF.Functions.Like()` | `LIKE` with escape |
| `EF.Functions.Collate()` | `COLLATE` |
| `string.Normalize()` | `NORMALIZE()` |

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
