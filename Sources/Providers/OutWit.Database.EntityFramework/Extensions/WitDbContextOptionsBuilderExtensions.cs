using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Extensions;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> to configure WitDatabase.
/// </summary>
public static class WitDbContextOptionsBuilderExtensions
{
    #region UseWitDb

    /// <summary>
    /// Configures the context to connect to a WitDatabase database using the specified connection string.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string of the database to connect to.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseWitDb(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var extension = (WitDbContextOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnectionString(connectionString);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        ConfigureWitDbOptions(optionsBuilder, witDbOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to a WitDatabase database using an existing connection.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">An existing <see cref="WitDbConnection"/> to be used to connect to the database.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseWitDb(
        this DbContextOptionsBuilder optionsBuilder,
        WitDbConnection connection,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        var extension = (WitDbContextOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnection(connection);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        ConfigureWitDbOptions(optionsBuilder, witDbOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to an in-memory WitDatabase database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseWitDbInMemory(
        this DbContextOptionsBuilder optionsBuilder,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = GetOrCreateExtension(optionsBuilder)
            .WithConnectionString("Data Source=:memory:")
            .WithInMemory(true);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        ConfigureWitDbOptions(optionsBuilder, witDbOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to a WitDatabase database using the specified connection string.
    /// </summary>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string of the database to connect to.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseWitDb<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseWitDb(connectionString, witDbOptionsAction);
        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to a WitDatabase database using an existing connection.
    /// </summary>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">An existing <see cref="WitDbConnection"/> to be used to connect to the database.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseWitDb<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        WitDbConnection connection,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseWitDb(connection, witDbOptionsAction);
        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to an in-memory WitDatabase database.
    /// </summary>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseWitDbInMemory<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseWitDbInMemory(witDbOptionsAction);
        return optionsBuilder;
    }

    #endregion

    #region Helpers

    private static WitDbContextOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.Options.FindExtension<WitDbContextOptionsExtension>()
            ?? new WitDbContextOptionsExtension();
    }

    private static void ConfigureWitDbOptions(
        DbContextOptionsBuilder optionsBuilder,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction)
    {
        if (witDbOptionsAction != null)
        {
            var witDbOptionsBuilder = new WitDbContextOptionsBuilder(optionsBuilder);
            witDbOptionsAction(witDbOptionsBuilder);
        }
    }

    #endregion
}
