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
/// When <c>source</c> is <c>null</c> (the EnsureCreated path), we build the create-table
/// operations directly from the target relational model instead of swallowing the exception.
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
            // Fresh database (EnsureCreated) — there are always differences
            if (source == null && target != null)
                return true;

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
            // EnsureCreated path: source is null, target has the full model.
            // Build create operations directly from the relational metadata.
            if (source == null && target != null)
                return BuildCreateOperations(target);

            return Array.Empty<MigrationOperation>();
        }
    }

    private static IReadOnlyList<MigrationOperation> BuildCreateOperations(IRelationalModel model)
    {
        var operations = new List<MigrationOperation>();

        foreach (var table in model.Tables)
        {
            var createTable = new CreateTableOperation
            {
                Name = table.Name,
                Schema = table.Schema
            };

            foreach (var column in table.Columns)
            {
                createTable.Columns.Add(new AddColumnOperation
                {
                    Name = column.Name,
                    Table = table.Name,
                    Schema = table.Schema,
                    ClrType = column.ProviderClrType,
                    ColumnType = column.StoreType,
                    IsNullable = column.IsNullable,
                    DefaultValue = column.DefaultValue,
                    DefaultValueSql = column.DefaultValueSql,
                    ComputedColumnSql = column.ComputedColumnSql,
                    IsStored = column.IsStored
                });
            }

            if (table.PrimaryKey != null)
            {
                createTable.PrimaryKey = new AddPrimaryKeyOperation
                {
                    Name = table.PrimaryKey.Name,
                    Table = table.Name,
                    Schema = table.Schema,
                    Columns = table.PrimaryKey.Columns.Select(c => c.Name).ToArray()
                };
            }

            // RuntimeModel may not expose these collections; skip gracefully.
            try
            {
                foreach (var uc in table.UniqueConstraints.Where(uc => uc != table.PrimaryKey))
                {
                    createTable.UniqueConstraints.Add(new AddUniqueConstraintOperation
                    {
                        Name = uc.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        Columns = uc.Columns.Select(c => c.Name).ToArray()
                    });
                }
            }
            catch (InvalidOperationException) { }

            try
            {
                foreach (var fk in table.ForeignKeyConstraints)
                {
                    createTable.ForeignKeys.Add(new AddForeignKeyOperation
                    {
                        Name = fk.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        Columns = fk.Columns.Select(c => c.Name).ToArray(),
                        PrincipalTable = fk.PrincipalTable.Name,
                        PrincipalSchema = fk.PrincipalTable.Schema,
                        PrincipalColumns = fk.PrincipalColumns.Select(c => c.Name).ToArray(),
                        OnDelete = fk.OnDeleteAction
                    });
                }
            }
            catch (InvalidOperationException) { }

            try
            {
                foreach (var cc in table.CheckConstraints)
                {
                    createTable.CheckConstraints.Add(new AddCheckConstraintOperation
                    {
                        Name = cc.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        Sql = cc.Sql
                    });
                }
            }
            catch (InvalidOperationException) { }

            operations.Add(createTable);
        }

        foreach (var table in model.Tables)
        {
            try
            {
                foreach (var index in table.Indexes)
                {
                    operations.Add(new CreateIndexOperation
                    {
                        Name = index.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        Columns = index.Columns.Select(c => c.Name).ToArray(),
                        IsUnique = index.IsUnique
                    });
                }
            }
            catch (InvalidOperationException) { }
        }

        return operations;
    }
}
#pragma warning restore EF1001
