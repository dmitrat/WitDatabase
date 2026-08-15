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
    #region Constants

    /// <summary>
    /// What an in-memory fixture connects to. The provider recognises this data source, and so does
    /// <c>WitDatabaseCreator</c>, which decides "in memory" by reading the connection string.
    /// </summary>
    private const string IN_MEMORY_CONNECTION_STRING = "Data Source=:memory:";

    #endregion

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
    /// <remarks>
    /// <para>
    /// <b>The connection is created here and left OPEN, and that is what makes the fixture work.</b>
    /// An in-memory database is private to its connection - it has no file to go back to - and Entity
    /// Framework opens and closes a connection for every operation. Handing it a connection STRING
    /// therefore gave each operation a fresh empty database: <c>EnsureCreated</c> returned true and
    /// the very next <c>SaveChanges</c> failed with <i>Table 'X' not found</i>. Handing it an already
    /// open connection is the same recipe SQLite's provider documents, and for the same reason: EF
    /// does not close a connection it did not open.
    /// </para>
    /// <para>
    /// <b>Nothing disposes that connection</b>, so the database lives as long as the options object
    /// and is collected with it. That is the right lifetime for a test fixture and the wrong one for
    /// anything long-lived; when the lifetime has to be in your hands, open a
    /// <see cref="WitDbConnection"/> yourself and use the overload that takes one.
    /// </para>
    /// <para>
    /// Every call makes its OWN database, so two fixtures in one suite cannot see each other.
    /// </para>
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseWitDbInMemory(
        this DbContextOptionsBuilder optionsBuilder,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        return optionsBuilder.UseWitDbInMemory(new WitDbConnection(IN_MEMORY_CONNECTION_STRING),
            witDbOptionsAction);
    }

    /// <summary>
    /// Configures the context to connect to an in-memory WitDatabase database over a connection the
    /// caller owns.
    /// </summary>
    /// <remarks>
    /// The connection is opened here if it is not open already, because an in-memory database exists
    /// only while a connection to it does. Closing or disposing it destroys the database, which is
    /// the point of taking it as a parameter: the lifetime is yours.
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">The connection to use. Opened if it is not open.</param>
    /// <param name="witDbOptionsAction">An optional action to allow additional WitDatabase specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseWitDbInMemory(
        this DbContextOptionsBuilder optionsBuilder,
        WitDbConnection connection,
        Action<WitDbContextOptionsBuilder>? witDbOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();

        var extension = ((WitDbContextOptionsExtension)GetOrCreateExtension(optionsBuilder)
                .WithConnection(connection))
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
