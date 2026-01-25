using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update.Internal;

namespace OutWit.Database.EntityFramework.Migrations;

/// <summary>
/// Custom migrations model differ for WitDatabase that handles RuntimeModel limitations.
/// </summary>
/// <remarks>
/// EF Core's RuntimeModel doesn't store all annotations (like Collation) to optimize memory.
/// This causes issues when MigrationsModelDiffer tries to diff models and access these annotations.
/// This implementation catches and handles these cases gracefully.
/// </remarks>
#pragma warning disable EF1001 // Internal EF Core API usage
public class WitMigrationsModelDiffer : MigrationsModelDiffer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitMigrationsModelDiffer"/> class.
    /// </summary>
    public WitMigrationsModelDiffer(
        IRelationalTypeMappingSource typeMappingSource,
        IMigrationsAnnotationProvider migrationsAnnotationProvider,
        IRelationalAnnotationProvider relationalAnnotationProvider,
        IRowIdentityMapFactory rowIdentityMapFactory,
        CommandBatchPreparerDependencies commandBatchPreparerDependencies)
        : base(typeMappingSource, migrationsAnnotationProvider, relationalAnnotationProvider, rowIdentityMapFactory, commandBatchPreparerDependencies)
    {
    }

    /// <inheritdoc />
    public override bool HasDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        try
        {
            return base.HasDifferences(source, target);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("read-optimized model"))
        {
            // RuntimeModel doesn't have all annotations - assume no differences for validation
            // Real differences will be caught by actual migration operations
            return false;
        }
    }

    /// <inheritdoc />
    public override IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        try
        {
            return base.GetDifferences(source, target);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("read-optimized model"))
        {
            // RuntimeModel doesn't have all annotations - return empty list
            // This prevents crashes during runtime migrations validation
            return Array.Empty<MigrationOperation>();
        }
    }
}
#pragma warning restore EF1001
