using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace OutWit.Database.EntityFramework.Migrations;

/// <summary>
/// Generates SQL for migration operations for WitDatabase.
/// </summary>
public sealed class WitMigrationsSqlGenerator : MigrationsSqlGenerator
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitMigrationsSqlGenerator"/> class.
    /// </summary>
    /// <param name="dependencies">The migrations SQL generator dependencies.</param>
    public WitMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region Table Operations

    /// <inheritdoc/>
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // Use CREATE TABLE IF NOT EXISTS for idempotent migrations
        builder.Append("CREATE TABLE IF NOT EXISTS ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.AppendLine(" (");

        CreateTableColumns(operation, model, builder);
        CreateTableConstraints(operation, model, builder);

        builder.AppendLine();
        builder.Append(")");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("DROP TABLE IF EXISTS ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        RenameTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.Append(" RENAME TO ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName!));
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    #endregion

    #region Column Operations

    /// <inheritdoc/>
    protected override void Generate(
        AddColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" ADD COLUMN ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.Append(" ");
        builder.Append(operation.ColumnType ?? GetColumnType(operation.ClrType));

        // Handle computed columns
        if (!string.IsNullOrEmpty(operation.ComputedColumnSql))
        {
            builder.Append(" GENERATED ALWAYS AS (");
            builder.Append(operation.ComputedColumnSql);
            builder.Append(")");
            if (operation.IsStored == true)
            {
                builder.Append(" STORED");
            }
            else
            {
                builder.Append(" VIRTUAL");
            }
        }
        else
        {
            if (!operation.IsNullable)
            {
                builder.Append(" NOT NULL");
            }

            if (operation.DefaultValue != null)
            {
                builder.Append(" DEFAULT ");
                builder.Append(GenerateSqlLiteral(operation.DefaultValue));
            }
            else if (!string.IsNullOrEmpty(operation.DefaultValueSql))
            {
                builder.Append(" DEFAULT (");
                builder.Append(operation.DefaultValueSql);
                builder.Append(")");
            }
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" DROP COLUMN ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" RENAME COLUMN ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.Append(" TO ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName));
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // WitSQL's ALTER COLUMN reaches defaults and nullability and nothing else. A change it
        // cannot make used to produce no statement at all, which is the worst of the options: the
        // migration is recorded as applied and the column keeps its old type, so the model and the
        // database disagree from then on with nothing to show for it. EF Core's SQLite provider,
        // whose ALTER TABLE is just as limited, refuses the operation instead - and so does this.
        if (!string.Equals(operation.ColumnType, operation.OldColumn.ColumnType, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"WitDatabase cannot change the type of an existing column. Column "
                + $"'{operation.Name}' on table '{operation.Table}' would go from "
                + $"'{operation.OldColumn.ColumnType}' to '{operation.ColumnType}'. Create a new "
                + $"table with the type you want, copy the rows across, drop the old table and "
                + $"rename the new one.");
        }

        if (operation.DefaultValue != null || !string.IsNullOrEmpty(operation.DefaultValueSql))
        {
            builder.Append("ALTER TABLE ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
            builder.Append(" ALTER COLUMN ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" SET DEFAULT ");

            if (operation.DefaultValue != null)
            {
                builder.Append(GenerateSqlLiteral(operation.DefaultValue));
            }
            else
            {
                builder.Append("(");
                builder.Append(operation.DefaultValueSql);
                builder.Append(")");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        // Handle nullability change
        if (!operation.IsNullable && operation.OldColumn.IsNullable)
        {
            builder.Append("ALTER TABLE ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
            builder.Append(" ALTER COLUMN ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" SET NOT NULL");
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
        else if (operation.IsNullable && !operation.OldColumn.IsNullable)
        {
            builder.Append("ALTER TABLE ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
            builder.Append(" ALTER COLUMN ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" DROP NOT NULL");
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    #endregion

    #region Index Operations

    /// <summary>
    /// Whether the column at <paramref name="index"/> is indexed in descending order.
    /// </summary>
    /// <param name="operation">The index being created.</param>
    /// <param name="index">Position of the column in the index.</param>
    /// <remarks>
    /// EF Core's convention: null means every column ascends, and an *empty* list means every
    /// column descends - it is not the same as "no descending columns".
    /// </remarks>
    private static bool IsDescending(CreateIndexOperation operation, int index)
    {
        if (operation.IsDescending == null)
            return false;

        return operation.IsDescending.Length == 0 || operation.IsDescending[index];
    }

    /// <inheritdoc/>
    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("CREATE ");

        if (operation.IsUnique)
        {
            builder.Append("UNIQUE ");
        }

        builder.Append("INDEX ");

        if (operation.Name != null)
        {
            builder.Append("IF NOT EXISTS ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" ");
        }

        builder.Append("ON ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" (");
        builder.Append(string.Join(", ", operation.Columns.Select((column, index) =>
        {
            var identifier = Dependencies.SqlGenerationHelper.DelimitIdentifier(column);

            return IsDescending(operation, index) ? identifier + " DESC" : identifier;
        })));
        builder.Append(")");

        // A dropped filter is not a performance question. A filtered UNIQUE index that becomes a
        // full one enforces a STRICTER constraint than the model declares, so rows the application
        // is entitled to insert are rejected.
        if (!string.IsNullOrEmpty(operation.Filter))
        {
            builder.Append(" WHERE ");
            builder.Append(operation.Filter);
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("DROP INDEX IF EXISTS ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name!));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        RenameIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // A comment is not a rename. It left the migration recorded as applied with the index
        // still under its old name, so every later migration that referred to the new one failed
        // for a reason with no connection to the cause. EF Core's SQLite provider refuses the
        // operation outright, and being told is worth more than a comment nobody reads.
        throw new NotSupportedException(
            $"WitDatabase cannot rename an index. Index '{operation.Name}' on table "
            + $"'{operation.Table}' would become '{operation.NewName}'. Drop the index and create "
            + $"it again under the new name.");
    }

    #endregion

    #region Constraint Operations

    /// <inheritdoc/>
    protected override void Generate(
        AddPrimaryKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // Emitting a comment made the migration succeed while the constraint was never created.
        // A table without the key its model declares accepts duplicates in silence, which is worse
        // than a migration that stops. SQLite refuses this operation for the same reason.
        throw new NotSupportedException(
            $"WitDatabase cannot add a PRIMARY KEY to an existing table. Table '{operation.Table}', "
            + $"columns {string.Join(", ", operation.Columns)}. Declare the key when the table is "
            + $"created, or create a new table with it and copy the rows across.");
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropPrimaryKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        throw new NotSupportedException(
            $"WitDatabase cannot drop the PRIMARY KEY of an existing table. Table "
            + $"'{operation.Table}', constraint '{operation.Name}'. Create a new table without the "
            + $"key, copy the rows across, drop the old table and rename the new one.");
    }

    /// <inheritdoc/>
    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // WitDatabase supports ALTER TABLE ADD CONSTRAINT ... FOREIGN KEY
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" ADD ");
        
        if (!string.IsNullOrEmpty(operation.Name))
        {
            builder.Append("CONSTRAINT ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" ");
        }
        
        builder.Append("FOREIGN KEY (");
        builder.Append(string.Join(", ", operation.Columns.Select(c => 
            Dependencies.SqlGenerationHelper.DelimitIdentifier(c))));
        builder.Append(") REFERENCES ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PrincipalTable));
        builder.Append("(");
        builder.Append(string.Join(", ", operation.PrincipalColumns.Select(c => 
            Dependencies.SqlGenerationHelper.DelimitIdentifier(c))));
        builder.Append(")");

        // ON DELETE action
        if (operation.OnDelete != ReferentialAction.NoAction)
        {
            builder.Append(" ON DELETE ");
            builder.Append(GetReferentialActionSql(operation.OnDelete));
        }

        // ON UPDATE action  
        if (operation.OnUpdate != ReferentialAction.NoAction)
        {
            builder.Append(" ON UPDATE ");
            builder.Append(GetReferentialActionSql(operation.OnUpdate));
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" DROP CONSTRAINT ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name!));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    /// <inheritdoc/>
    protected override void Generate(
        AddUniqueConstraintOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // Implement as unique index for better compatibility
        Generate(new CreateIndexOperation
        {
            Name = operation.Name,
            Table = operation.Table,
            Columns = operation.Columns,
            IsUnique = true
        }, model, builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropUniqueConstraintOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        Generate(new DropIndexOperation
        {
            Name = operation.Name,
            Table = operation.Table
        }, model, builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        AddCheckConstraintOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // WitDatabase supports ALTER TABLE ADD CONSTRAINT ... CHECK
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" ADD ");
        
        if (!string.IsNullOrEmpty(operation.Name))
        {
            builder.Append("CONSTRAINT ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
            builder.Append(" ");
        }
        
        builder.Append("CHECK (");
        builder.Append(operation.Sql);
        builder.Append(")");
        
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropCheckConstraintOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("ALTER TABLE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));
        builder.Append(" DROP CONSTRAINT ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name!));
        
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    #endregion

    #region Sequence Operations

    /// <inheritdoc/>
    protected override void Generate(
        CreateSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("CREATE SEQUENCE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

        if (operation.StartValue != 1)
        {
            builder.Append(" START WITH ");
            builder.Append(operation.StartValue.ToString());
        }

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        DropSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("DROP SEQUENCE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    /// <inheritdoc/>
    protected override void Generate(
        AlterSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("ALTER SEQUENCE ");
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));
        builder.Append(" RESTART");
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    #endregion

    #region SQL Operation

    /// <inheritdoc/>
    protected override void Generate(
        SqlOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.AppendLine(operation.Sql);
        EndStatement(builder, suppressTransaction: operation.SuppressTransaction);
    }

    #endregion

    #region Schema Operations

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately emits nothing. WitDatabase has a single schema, so there is never a schema to
    /// create - but EF Core emits this operation as a matter of course, and the base implementation
    /// answered it with NotSupportedException, which failed migrations that were perfectly valid.
    /// EF Core's SQLite provider, which likewise has no schemas, ignores it in the same way.
    /// </remarks>
    protected override void Generate(
        EnsureSchemaOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
    }

    #endregion

    #region Column Definition

    /// <inheritdoc/>
    protected override void ColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name));
        builder.Append(" ");
        builder.Append(operation.ColumnType ?? GetColumnType(operation.ClrType));

        // Handle computed columns
        if (!string.IsNullOrEmpty(operation.ComputedColumnSql))
        {
            builder.Append(" GENERATED ALWAYS AS (");
            builder.Append(operation.ComputedColumnSql);
            builder.Append(")");
            if (operation.IsStored == true)
            {
                builder.Append(" STORED");
            }
            else
            {
                builder.Append(" VIRTUAL");
            }
        }
        else
        {
            if (!operation.IsNullable)
            {
                builder.Append(" NOT NULL");
            }

            if (operation.DefaultValue != null)
            {
                builder.Append(" DEFAULT ");
                builder.Append(GenerateSqlLiteral(operation.DefaultValue));
            }
            else if (!string.IsNullOrEmpty(operation.DefaultValueSql))
            {
                builder.Append(" DEFAULT (");
                builder.Append(operation.DefaultValueSql);
                builder.Append(")");
            }
        }
    }

    #endregion

    #region Primary Key Definition

    /// <inheritdoc/>
    protected override void PrimaryKeyConstraint(
        AddPrimaryKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder.Append("PRIMARY KEY (");
        builder.Append(string.Join(", ", operation.Columns.Select(c =>
            Dependencies.SqlGenerationHelper.DelimitIdentifier(c))));
        builder.Append(")");
    }

    #endregion

    #region CreateTableColumns

    /// <inheritdoc/>
    protected override void CreateTableColumns(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        for (var i = 0; i < operation.Columns.Count; i++)
        {
            var column = operation.Columns[i];
            
            if (i > 0)
            {
                builder.AppendLine(",");
            }

            builder.Append("    ");
            builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(column.Name));
            builder.Append(" ");
            builder.Append(column.ColumnType ?? GetColumnType(column.ClrType));

            // Handle computed columns
            if (!string.IsNullOrEmpty(column.ComputedColumnSql))
            {
                builder.Append(" GENERATED ALWAYS AS (");
                builder.Append(column.ComputedColumnSql);
                builder.Append(")");
                if (column.IsStored == true)
                {
                    builder.Append(" STORED");
                }
                else
                {
                    builder.Append(" VIRTUAL");
                }
            }
            else
            {
                // Check if this column is the primary key for AUTOINCREMENT
                var isPrimaryKey = operation.PrimaryKey?.Columns.Length == 1 &&
                    operation.PrimaryKey.Columns[0].Equals(column.Name, StringComparison.OrdinalIgnoreCase);
                
                if (isPrimaryKey)
                {
                    builder.Append(" PRIMARY KEY");
                    
                    // Check for AUTOINCREMENT (integer type PK with no default value)
                    if (IsAutoIncrement(column))
                    {
                        builder.Append(" AUTOINCREMENT");
                    }
                }
                
                if (!column.IsNullable && !isPrimaryKey)
                {
                    builder.Append(" NOT NULL");
                }

                if (column.DefaultValue != null)
                {
                    builder.Append(" DEFAULT ");
                    builder.Append(GenerateSqlLiteral(column.DefaultValue));
                }
                else if (!string.IsNullOrEmpty(column.DefaultValueSql))
                {
                    builder.Append(" DEFAULT (");
                    builder.Append(column.DefaultValueSql);
                    builder.Append(")");
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void CreateTableConstraints(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // Primary key (only if composite, single-column PK is inline)
        if (operation.PrimaryKey != null && operation.PrimaryKey.Columns.Length > 1)
        {
            builder.AppendLine(",");
            builder.Append("    ");
            PrimaryKeyConstraint(operation.PrimaryKey, model, builder);
        }

        // Unique constraints
        foreach (var uniqueConstraint in operation.UniqueConstraints)
        {
            builder.AppendLine(",");
            builder.Append("    ");
            
            if (!string.IsNullOrEmpty(uniqueConstraint.Name))
            {
                builder.Append("CONSTRAINT ");
                builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(uniqueConstraint.Name));
                builder.Append(" ");
            }
            
            builder.Append("UNIQUE (");
            builder.Append(string.Join(", ", uniqueConstraint.Columns.Select(c =>
                Dependencies.SqlGenerationHelper.DelimitIdentifier(c))));
            builder.Append(")");
        }

        // Foreign keys
        foreach (var foreignKey in operation.ForeignKeys)
        {
            builder.AppendLine(",");
            builder.Append("    ");
            ForeignKeyConstraint(foreignKey, model, builder);
        }

        // Check constraints
        foreach (var checkConstraint in operation.CheckConstraints)
        {
            builder.AppendLine(",");
            builder.Append("    ");
            
            if (!string.IsNullOrEmpty(checkConstraint.Name))
            {
                builder.Append("CONSTRAINT ");
                builder.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(checkConstraint.Name));
                builder.Append(" ");
            }
            
            builder.Append("CHECK (");
            builder.Append(checkConstraint.Sql);
            builder.Append(")");
        }
    }

    /// <summary>
    /// Determines if a column should use AUTOINCREMENT.
    /// </summary>
    private static bool IsAutoIncrement(AddColumnOperation column)
    {
        // Check for WitDb:Autoincrement annotation
        //if (column[Metadata.WitAnnotationProvider.AUTOINCREMENT] is true)
        //{
        //    return true;
        //}
        
        // Check if it's an integer type with no default value (likely auto-increment)
        var clrType = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        var isIntegerType = clrType == typeof(int) || clrType == typeof(long) || 
                           clrType == typeof(short) || clrType == typeof(byte) ||
                           clrType == typeof(uint) || clrType == typeof(ulong) ||
                           clrType == typeof(ushort) || clrType == typeof(sbyte);
        
        // If it's an integer type with no default value, assume auto-increment
        if (isIntegerType && 
            column.DefaultValue == null && 
            string.IsNullOrEmpty(column.DefaultValueSql))
        {
            return true;
        }
        
        return false;
    }

    #endregion

    #region Helpers

    private string GetColumnType(Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        return underlyingType switch
        {
            _ when underlyingType == typeof(bool) => "BOOLEAN",
            _ when underlyingType == typeof(byte) => "UTINYINT",
            _ when underlyingType == typeof(sbyte) => "TINYINT",
            _ when underlyingType == typeof(short) => "SMALLINT",
            _ when underlyingType == typeof(ushort) => "USMALLINT",
            _ when underlyingType == typeof(int) => "INT",
            _ when underlyingType == typeof(uint) => "UINT",
            _ when underlyingType == typeof(long) => "BIGINT",
            _ when underlyingType == typeof(ulong) => "UBIGINT",
            _ when underlyingType == typeof(float) => "FLOAT",
            _ when underlyingType == typeof(double) => "DOUBLE",
            _ when underlyingType == typeof(decimal) => "DECIMAL",
            _ when underlyingType == typeof(string) => "TEXT",
            _ when underlyingType == typeof(byte[]) => "BLOB",
            _ when underlyingType == typeof(DateTime) => "DATETIME",
            _ when underlyingType == typeof(DateTimeOffset) => "DATETIMEOFFSET",
            _ when underlyingType == typeof(DateOnly) => "DATE",
            _ when underlyingType == typeof(TimeOnly) => "TIME",
            _ when underlyingType == typeof(TimeSpan) => "INTERVAL",
            _ when underlyingType == typeof(Guid) => "GUID",
            _ when underlyingType.IsEnum => "INT",
            _ => "TEXT"
        };
    }

    private string GenerateSqlLiteral(object value)
    {
        return value switch
        {
            null => "NULL",
            bool b => b ? "TRUE" : "FALSE",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            Guid g => $"'{g}'",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            _ => value.ToString() ?? "NULL"
        };
    }

    private static string GetReferentialActionSql(ReferentialAction action)
    {
        return action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.Restrict => "RESTRICT",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.NoAction => "NO ACTION",
            _ => "NO ACTION"
        };
    }

    #endregion
}
