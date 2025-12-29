using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Diagnostics;
using OutWit.Database.EntityFramework.Infrastructure;
using OutWit.Database.EntityFramework.Metadata;
using OutWit.Database.EntityFramework.Migrations;
using OutWit.Database.EntityFramework.Query;
using OutWit.Database.EntityFramework.Storage;
using OutWit.Database.EntityFramework.Update;

namespace OutWit.Database.EntityFramework.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to add WitDatabase Entity Framework services.
/// </summary>
public static class WitDbServiceCollectionExtensions
{
    #region Extension Methods

    /// <summary>
    /// Adds the services required by the WitDatabase database provider for Entity Framework Core.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same service collection for method chaining.</returns>
    public static IServiceCollection AddEntityFrameworkWitDb(this IServiceCollection services)
    {
        var builder = new EntityFrameworkRelationalServicesBuilder(services);

        builder
            // Core services
            .TryAdd<LoggingDefinitions, WitLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, WitDatabaseProvider>()
            
            // Connection and type mapping
            .TryAdd<IRelationalTypeMappingSource, WitTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, WitSqlGenerationHelper>()
            .TryAdd<IRelationalConnection, WitRelationalConnection>()
            
            // Query generation
            .TryAdd<IQuerySqlGeneratorFactory, WitQuerySqlGeneratorFactory>()
            
            // Update pipeline
            .TryAdd<IUpdateSqlGenerator, WitUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, WitModificationCommandBatchFactory>()
            
            // Model building
            .TryAdd<IRelationalAnnotationProvider, WitAnnotationProvider>()
            .TryAdd<IModelValidator, WitModelValidator>()
            
            // Migrations
            .TryAdd<IMigrationsSqlGenerator, WitMigrationsSqlGenerator>()
            .TryAdd<IHistoryRepository, WitHistoryRepository>()
            
            // Database creation
            .TryAdd<IRelationalDatabaseCreator, WitDatabaseCreator>();

        builder.TryAddCoreServices();

        return services;
    }

    #endregion
}
