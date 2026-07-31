using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Model;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// DDL execution for TABLE operations (CREATE, DROP, ALTER).
/// </summary>
public sealed partial class StatementExecutor
{
    #region CREATE TABLE

    private WitSqlResult ExecuteCreateTable(WitSqlStatementCreateTable createTable)
    {
        // Check IF NOT EXISTS
        if (createTable.IfNotExists)
        {
            var existingTable = m_context.Database.GetTable(createTable.TableName);
            if (existingTable != null)
            {
                return new WitSqlResult(); // Table already exists, do nothing
            }
        }

        var columns = new List<DefinitionColumn>();
        var primaryKeyColumns = new List<string>();
        var tableChecks = new List<string>();
        var tableForeignKeys = new List<DefinitionForeignKey>();
        var tableUniqueConstraints = new List<IReadOnlyList<string>>();
        var namedConstraints = new List<DefinitionNamedConstraint>();

        foreach (var colDef in createTable.Columns)
        {
            var col = BuildColumnDefinition(colDef, primaryKeyColumns);
            columns.Add(col);
        }

        // Process table-level constraints
        if (createTable.Constraints != null)
        {
            ProcessTableConstraints(createTable.Constraints, columns, primaryKeyColumns,
                tableChecks, tableForeignKeys, tableUniqueConstraints, namedConstraints);
        }

        var metadata = new DefinitionTable
        {
            Name = createTable.TableName,
            Columns = columns,
            PrimaryKey = primaryKeyColumns.Count > 0 ? primaryKeyColumns : null,
            UniqueConstraints = tableUniqueConstraints.Count > 0 ? tableUniqueConstraints : null,
            CheckExpressions = tableChecks.Count > 0 ? tableChecks : null,
            ForeignKeys = tableForeignKeys.Count > 0 ? tableForeignKeys : null,
            NamedConstraints = namedConstraints.Count > 0 ? namedConstraints : null
        };

        m_context.Database.CreateTable(metadata);
        return new WitSqlResult();
    }

    private DefinitionColumn BuildColumnDefinition(WitSqlColumn colDef, List<string> primaryKeyColumns)
    {
        var col = new DefinitionColumn
        {
            Name = colDef.Name,
            Type = colDef.DataType != null ? MapDataType(colDef.DataType) : WitDataType.StringVariable,
            Nullable = true,
            ComputedExpression = colDef.ComputedExpression != null 
                ? WitSqlExpressionSerializer.Serialize(colDef.ComputedExpression) 
                : null,
            IsStored = colDef.ComputedType == ComputedColumnType.Stored
        };

        if (colDef.Constraints == null) 
            return col;

        foreach (var constraint in colDef.Constraints)
        {
            switch (constraint)
            {
                case ColumnConstraintNotNull notNull:
                    col.Nullable = !notNull.IsNotNull;
                    break;

                case ColumnConstraintPrimaryKey pk:
                    col.IsPrimaryKey = true;
                    // INTEGER PRIMARY KEY is autoincrement by default (SQLite behavior)
                    col.IsAutoIncrement = pk.AutoIncrement || col.Type == WitDataType.Int64;
                    primaryKeyColumns.Add(col.Name);
                    break;

                case ColumnConstraintUnique:
                    col.IsUnique = true;
                    break;

                case ColumnConstraintDefault def:
                    col.DefaultValue = WitSqlExpressionSerializer.Serialize(def.Value);
                    break;

                case ColumnConstraintCheck check:
                    col.CheckExpression = WitSqlExpressionSerializer.Serialize(check.Condition);
                    break;

                case ColumnConstraintReferences refs:
                    col.ForeignKey = new DefinitionForeignKey
                    {
                        Columns = [col.Name],
                        ForeignTable = refs.ForeignTable,
                        ForeignColumns = refs.ForeignColumn != null ? [refs.ForeignColumn] : null,
                        OnDelete = MapReferenceAction(refs.OnDelete),
                        OnUpdate = MapReferenceAction(refs.OnUpdate)
                    };
                    break;
            }
        }

        return col;
    }

    private static void ProcessTableConstraints(
        IReadOnlyList<TableConstraint> constraints,
        List<DefinitionColumn> columns,
        List<string> primaryKeyColumns,
        List<string> tableChecks,
        List<DefinitionForeignKey> tableForeignKeys,
        List<IReadOnlyList<string>> tableUniqueConstraints,
        List<DefinitionNamedConstraint> namedConstraints)
    {
        foreach (var constraint in constraints)
        {
            // A constraint declared with a name keeps it. Recorded alongside the enforcement structures
            // rather than instead of them: enforcement already worked, and only the name was lost.
            if (!string.IsNullOrEmpty(constraint.Name))
                namedConstraints.Add(BuildNamedConstraint(constraint, constraint.Name));

            switch (constraint)
            {
                case TableConstraintPrimaryKey pkc:
                    primaryKeyColumns.Clear();
                    foreach (var pkColName in pkc.Columns)
                    {
                        primaryKeyColumns.Add(pkColName);
                        var pkCol = columns.FirstOrDefault(c => 
                            c.Name.Equals(pkColName, StringComparison.OrdinalIgnoreCase));
                        if (pkCol != null)
                        {
                            pkCol.IsPrimaryKey = true;
                            if (pkc.Columns.Count == 1)
                            {
                                pkCol.IsAutoIncrement = pkCol.Type == WitDataType.Int64;
                            }
                        }
                    }
                    break;

                case TableConstraintUnique uc:
                    tableUniqueConstraints.Add(uc.Columns.ToList());
                    // Only mark as column-level unique if single column
                    if (uc.Columns.Count == 1)
                    {
                        var ucCol = columns.FirstOrDefault(c => 
                            c.Name.Equals(uc.Columns[0], StringComparison.OrdinalIgnoreCase));
                        if (ucCol != null)
                        {
                            ucCol.IsUnique = true;
                        }
                    }
                    break;

                case TableConstraintCheck tc:
                    tableChecks.Add(WitSqlExpressionSerializer.Serialize(tc.Condition));
                    break;

                case TableConstraintForeignKey fk:
                    tableForeignKeys.Add(new DefinitionForeignKey
                    {
                        Columns = fk.Columns.ToList(),
                        ForeignTable = fk.ForeignTable,
                        ForeignColumns = fk.ForeignColumns?.ToList(),
                        OnDelete = MapReferenceAction(fk.OnDelete),
                        OnUpdate = MapReferenceAction(fk.OnUpdate)
                    });
                    break;
            }
        }
    }

    #endregion

    #region DROP TABLE

    private WitSqlResult ExecuteDropTable(WitSqlStatementDropTable dropTable)
    {
        var table = m_context.Database.GetTable(dropTable.TableName);
        if (table == null && !dropTable.IfExists)
        {
            throw new InvalidOperationException($"Table '{dropTable.TableName}' not found");
        }

        if (table != null)
        {
            m_context.Database.DropTable(dropTable.TableName);
        }

        return new WitSqlResult();
    }

    #endregion

    #region ALTER TABLE

    private WitSqlResult ExecuteAlterTable(WitSqlStatementAlterTable alterTable)
    {
        switch (alterTable.Action)
        {
            case AlterActionAddColumn addColumn:
                ExecuteAddColumn(alterTable.TableName, addColumn);
                break;

            case AlterActionDropColumn dropColumn:
                m_context.Database.DropColumn(alterTable.TableName, dropColumn.ColumnName);
                break;

            case AlterActionRenameTable renameTable:
                m_context.Database.RenameTable(alterTable.TableName, renameTable.NewName);
                break;

            case AlterActionRenameColumn renameColumn:
                m_context.Database.RenameColumn(alterTable.TableName, renameColumn.OldName, renameColumn.NewName);
                break;

            case AlterActionAlterColumn alterColumn:
                ExecuteAlterColumn(alterTable.TableName, alterColumn);
                break;

            case AlterActionAddConstraint addConstraint:
                ExecuteAddConstraint(alterTable.TableName, addConstraint);
                break;

            case AlterActionDropConstraint dropConstraint:
                m_context.Database.DropConstraint(alterTable.TableName, dropConstraint.ConstraintName);
                break;

            default:
                throw new NotSupportedException($"ALTER TABLE action not supported: {alterTable.Action.GetType().Name}");
        }

        return new WitSqlResult();
    }

    private void ExecuteAddColumn(string tableName, AlterActionAddColumn addColumn)
    {
        var colDef = addColumn.WitSqlColumn;

        // Check if this is a computed column
        if (colDef.IsComputed)
        {
            var col = new DefinitionColumn
            {
                Name = colDef.Name,
                // For computed columns, infer type from expression or use default
                Type = colDef.DataType != null ? MapDataType(colDef.DataType) : WitDataType.StringVariable,
                Nullable = true,
                ComputedExpression = WitSqlExpressionSerializer.Serialize(colDef.ComputedExpression!),
                IsStored = colDef.ComputedType == ComputedColumnType.Stored
            };

            m_context.Database.AddComputedColumn(tableName, col);
            return;
        }

        // Regular column
        var regularCol = new DefinitionColumn
        {
            Name = colDef.Name,
            Type = colDef.DataType != null ? MapDataType(colDef.DataType) : WitDataType.StringVariable,
            Nullable = true
        };

        // Process constraints
        if (colDef.Constraints != null)
        {
            foreach (var constraint in colDef.Constraints)
            {
                switch (constraint)
                {
                    case ColumnConstraintNotNull notNull:
                        regularCol.Nullable = !notNull.IsNotNull;
                        break;

                    case ColumnConstraintDefault def:
                        regularCol.DefaultValue = WitSqlExpressionSerializer.Serialize(def.Value);
                        break;
                }
            }
        }

        m_context.Database.AddColumn(tableName, regularCol);
    }

    private void ExecuteAlterColumn(string tableName, AlterActionAlterColumn action)
    {
        // Handle TYPE change
        if (action.NewType != null)
        {
            m_context.Database.AlterColumnType(tableName, action.ColumnName, MapDataType(action.NewType));
        }

        // Handle SET DEFAULT
        if (action.NewDefault != null)
        {
            var evaluator = new ExpressionEvaluator(m_context);
            var defaultValue = evaluator.Evaluate(action.NewDefault, new WitSqlRow([], []));
            m_context.Database.SetColumnDefault(tableName, action.ColumnName, defaultValue);
        }

        // Handle DROP DEFAULT
        if (action.DropDefault)
        {
            m_context.Database.DropColumnDefault(tableName, action.ColumnName);
        }

        // Handle SET/DROP NOT NULL
        if (action.SetNotNull.HasValue)
        {
            m_context.Database.SetColumnNotNull(tableName, action.ColumnName, action.SetNotNull.Value);
        }
    }

    /// <summary>
    /// Builds the catalog's record of a named constraint from the parsed one.
    /// </summary>
    /// <remarks>
    /// Shared by <c>ALTER TABLE ADD CONSTRAINT</c> and by <c>CREATE TABLE</c>. It was only ever reachable
    /// from the first, which is why a name given inline in <c>CREATE TABLE</c> never reached the catalog:
    /// the constraint was enforced, and anonymous, so it could be neither listed in
    /// <c>INFORMATION_SCHEMA.TABLE_CONSTRAINTS</c> nor removed by <c>DROP CONSTRAINT</c>. This records a
    /// name that was previously discarded; it does not change what is enforced.
    /// </remarks>
    private static DefinitionNamedConstraint BuildNamedConstraint(TableConstraint constraint, string name) =>
        constraint switch
        {
            TableConstraintCheck check => new DefinitionNamedConstraint
            {
                Name = name,
                Type = ConstraintType.Check,
                CheckExpression = WitSqlExpressionSerializer.Serialize(check.Condition)
            },
            TableConstraintUnique unique => new DefinitionNamedConstraint
            {
                Name = name,
                Type = ConstraintType.Unique,
                Columns = unique.Columns.ToList()
            },
            TableConstraintForeignKey fk => new DefinitionNamedConstraint
            {
                Name = name,
                Type = ConstraintType.ForeignKey,
                Columns = fk.Columns.ToList(),
                ForeignKey = new DefinitionForeignKey
                {
                    Columns = fk.Columns.ToList(),
                    ForeignTable = fk.ForeignTable,
                    ForeignColumns = fk.ForeignColumns?.ToList(),
                    OnDelete = MapReferenceAction(fk.OnDelete),
                    OnUpdate = MapReferenceAction(fk.OnUpdate)
                }
            },
            TableConstraintPrimaryKey pk => new DefinitionNamedConstraint
            {
                Name = name,
                Type = ConstraintType.PrimaryKey,
                Columns = pk.Columns.ToList()
            },
            _ => throw new NotSupportedException($"Constraint type not supported: {constraint.GetType().Name}")
        };

    private void ExecuteAddConstraint(string tableName, AlterActionAddConstraint addConstraint)
    {
        if (addConstraint.Constraint == null)
        {
            throw new InvalidOperationException("ADD CONSTRAINT requires a constraint definition");
        }

        var constraint = addConstraint.Constraint;
        var constraintName = constraint.Name 
            ?? throw new InvalidOperationException("ADD CONSTRAINT requires a constraint name");

        if (constraint is TableConstraintPrimaryKey)
        {
            throw new NotSupportedException(
                "Adding PRIMARY KEY constraint to existing table is not supported");
        }

        m_context.Database.AddConstraint(tableName, BuildNamedConstraint(constraint, constraintName));
    }

    #endregion
}
