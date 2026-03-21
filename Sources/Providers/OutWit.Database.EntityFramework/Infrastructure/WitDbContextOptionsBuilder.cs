using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Database.AdoNet;

namespace OutWit.Database.EntityFramework.Infrastructure;

/// <summary>
/// Allows WitDatabase specific configuration to be performed on <see cref="DbContextOptions"/>.
/// </summary>
public sealed class WitDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<WitDbContextOptionsBuilder, WitDbContextOptionsExtension>
{

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbContextOptionsBuilder"/> class.
    /// </summary>
    /// <param name="optionsBuilder">The underlying options builder.</param>
    public WitDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }

    #endregion

    #region Functions

    /// <summary>
    /// Configures the command timeout (in seconds) for commands executed against the database.
    /// </summary>
    /// <param name="commandTimeout">The timeout in seconds, or null to use the default.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder CommandTimeout(int? commandTimeout)
    {
        base.CommandTimeout(commandTimeout);
        return this;
    }

    /// <summary>
    /// Enables sensitive data logging.
    /// </summary>
    /// <param name="sensitiveDataLoggingEnabled">True to enable sensitive data logging.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder EnableSensitiveDataLogging(bool sensitiveDataLoggingEnabled = true)
    {
        OptionsBuilder.EnableSensitiveDataLogging(sensitiveDataLoggingEnabled);
        return this;
    }

    /// <summary>
    /// Enables detailed errors.
    /// </summary>
    /// <param name="detailedErrorsEnabled">True to enable detailed errors.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder EnableDetailedErrors(bool detailedErrorsEnabled = true)
    {
        OptionsBuilder.EnableDetailedErrors(detailedErrorsEnabled);
        return this;
    }

    /// <summary>
    /// Configures the query splitting behavior for queries against the database.
    /// </summary>
    /// <param name="querySplittingBehavior">The query splitting behavior to use.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder UseQuerySplittingBehavior(QuerySplittingBehavior querySplittingBehavior)
    {
        var extension = GetOrCreateExtension();
        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder).AddOrUpdateExtension(extension);
        return this;
    }

    /// <summary>
    /// Enables parallel write mode with automatic selection.
    /// This provides better write throughput for multi-threaded scenarios.
    /// </summary>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder UseParallelWrites()
    {
        return UseParallelWrites(WitDbParallelMode.Auto);
    }

    /// <summary>
    /// Enables parallel write mode with the specified mode.
    /// </summary>
    /// <param name="mode">The parallel mode to use.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder UseParallelWrites(WitDbParallelMode mode)
    {
        var extension = GetOrCreateExtension().WithParallelMode(mode);
        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder).AddOrUpdateExtension(extension);
        return this;
    }

    /// <summary>
    /// Configures the maximum number of parallel writers.
    /// Only applicable when parallel mode is enabled.
    /// </summary>
    /// <param name="maxWriters">The maximum number of parallel writers.</param>
    /// <returns>The same builder instance for method chaining.</returns>
    public WitDbContextOptionsBuilder MaxWriters(int maxWriters)
    {
        if (maxWriters < 1)
            throw new ArgumentOutOfRangeException(nameof(maxWriters), "Max writers must be at least 1");

        var extension = GetOrCreateExtension().WithMaxWriters(maxWriters);
        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder).AddOrUpdateExtension(extension);
        return this;
    }

    #endregion

    #region Helpers

    private WitDbContextOptionsExtension GetOrCreateExtension()
    {
        return OptionsBuilder.Options.FindExtension<WitDbContextOptionsExtension>()
            ?? new WitDbContextOptionsExtension();
    }

    #endregion
}
