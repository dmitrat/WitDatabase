using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

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

        foreach (var colDef in createTable.Columns)
        {
            var col = BuildColumnDefinition(colDef, primaryKeyColumns);
            columns.Add(col);
        }

        // Process table-level constraints
        if (createTable.Constraints != null)
        {
            ProcessTableConstraints(createTable.Constraints, columns, primaryKeyColumns, 
                tableChecks, tableForeignKeys, tableUniqueConstraints);
        }

        var metadata = new DefinitionTable
        {
            Name = createTable.TableName,
            Columns = columns,
            PrimaryKey = primaryKeyColumns.Count > 0 ? primaryKeyColumns : null,
            UniqueConstraints = tableUniqueConstraints.Count > 0 ? tableUniqueConstraints : null,
            CheckExpressions = tableChecks.Count > 0 ? tableChecks : null,
            ForeignKeys = tableForeignKeys.Count > 0 ? tableForeignKeys : null
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
        List<IReadOnlyList<string>> tableUniqueConstraints)
    {
        foreach (var constraint in constraints)
        {
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

            default:
                throw new NotSupportedException($"ALTER TABLE action not supported: {alterTable.Action.GetType().Name}");
        }

        return new WitSqlResult();
    }

    private void ExecuteAddColumn(string tableName, AlterActionAddColumn addColumn)
    {
        var colDef = addColumn.WitSqlColumn;

        var col = new DefinitionColumn
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
                        col.Nullable = !notNull.IsNotNull;
                        break;

                    case ColumnConstraintDefault def:
                        col.DefaultValue = WitSqlExpressionSerializer.Serialize(def.Value);
                        break;
                }
            }
        }

        m_context.Database.AddColumn(tableName, col);
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

    #endregion

    #region CREATE/DROP INDEX

    private WitSqlResult ExecuteCreateIndex(WitSqlStatementCreateIndex createIndex)
    {
        // Check if table exists
        var table = m_context.Database.GetTable(createIndex.TableName);
        if (table == null)
        {
            throw new InvalidOperationException($"Table '{createIndex.TableName}' not found");
        }

        // Check if index already exists when IF NOT EXISTS is specified
        if (createIndex.IfNotExists)
        {
            var existingIndex = m_context.Database.GetIndex(createIndex.IndexName);
            if (existingIndex != null)
            {
                return new WitSqlResult(); // Index already exists, do nothing
            }
        }

        var metadata = new DefinitionIndex
        {
            Name = createIndex.IndexName,
            TableName = createIndex.TableName,
            Columns = createIndex.Elements
                .Where(e => e.ColumnName != null)
                .Select(e => e.ColumnName!)
                .ToList(),
            IsUnique = createIndex.IsUnique,
            ColumnDescending = createIndex.Elements.Select(e => e.Descending).ToList(),
            WhereExpression = createIndex.WhereClause != null 
                ? WitSqlExpressionSerializer.Serialize(createIndex.WhereClause) 
                : null,
            IncludeColumns = createIndex.IncludeColumns,
            ExpressionColumns = createIndex.Elements
                .Select(e => e.Expression != null 
                    ? WitSqlExpressionSerializer.Serialize(e.Expression) 
                    : null)
                .ToList()
        };

        m_context.Database.CreateIndex(metadata);
        return new WitSqlResult();
    }

    private WitSqlResult ExecuteDropIndex(WitSqlStatementDropIndex dropIndex)
    {
        // Check IF EXISTS
        if (dropIndex.IfExists)
        {
            var existingIndex = m_context.Database.GetIndex(dropIndex.IndexName);
            if (existingIndex == null)
            {
                return new WitSqlResult(); // Index doesn't exist, do nothing
            }
        }

        m_context.Database.DropIndex(dropIndex.IndexName);
        return new WitSqlResult();
    }

    #endregion

    #region CREATE/DROP VIEW

    private WitSqlResult ExecuteCreateView(WitSqlStatementCreateView createView)
    {
        // Check if view already exists
        if (m_context.Database.GetView(createView.ViewName) != null)
        {
            if (createView.IfNotExists)
                return new WitSqlResult();
            throw new InvalidOperationException($"View '{createView.ViewName}' already exists");
        }

        // Serialize the SELECT statement back to SQL for storage
        var selectSql = WitSqlStatementSerializer.Serialize(createView.Query);

        m_context.Database.CreateView(createView.ViewName, selectSql, createView.ColumnNames);
        return new WitSqlResult();
    }

    private WitSqlResult ExecuteDropView(WitSqlStatementDropView dropView)
    {
        var view = m_context.Database.GetView(dropView.ViewName);
        if (view == null)
        {
            if (dropView.IfExists)
                return new WitSqlResult();
            throw new InvalidOperationException($"View '{dropView.ViewName}' does not exist");
        }

        m_context.Database.DropView(dropView.ViewName);
        return new WitSqlResult();
    }

    #endregion

    #region CREATE/DROP TRIGGER

    private WitSqlResult ExecuteCreateTrigger(WitSqlStatementCreateTrigger createTrigger)
    {
        // Check IF NOT EXISTS
        if (createTrigger.IfNotExists && m_context.Database.GetTrigger(createTrigger.TriggerName) != null)
            return new WitSqlResult();

        // Serialize WHEN condition if present
        string? whenConditionSql = createTrigger.WhenCondition != null 
            ? WitSqlExpressionSerializer.Serialize(createTrigger.WhenCondition) 
            : null;

        // Use original body SQL if available, otherwise serialize
        var bodySql = createTrigger.BodyWitSql ?? SerializeTriggerBody(createTrigger.Body);

        var triggerMetadata = new DefinitionTrigger
        {
            Name = createTrigger.TriggerName,
            TableName = createTrigger.TableName,
            Time = MapTriggerTiming(createTrigger.Time),
            Event = MapTriggerEvent(createTrigger.Event),
            UpdateColumns = createTrigger.UpdateColumns,
            ForEachRow = createTrigger.ForEachRow,
            WhenCondition = whenConditionSql,
            Body = bodySql
        };

        m_context.Database.CreateTrigger(triggerMetadata);
        return new WitSqlResult();
    }

    private WitSqlResult ExecuteDropTrigger(WitSqlStatementDropTrigger dropTrigger)
    {
        if (dropTrigger.IfExists && m_context.Database.GetTrigger(dropTrigger.TriggerName) == null)
            return new WitSqlResult();

        m_context.Database.DropTrigger(dropTrigger.TriggerName);
        return new WitSqlResult();
    }

    private static string SerializeTriggerBody(IReadOnlyList<WitSqlStatement> statements)
    {
        var parts = statements.Select(WitSqlStatementSerializer.Serialize);
        return string.Join("; ", parts);
    }

    #endregion

    #region CREATE/DROP/ALTER SEQUENCE

    private WitSqlResult ExecuteCreateSequence(WitSqlStatementCreateSequence createSequence)
    {
        if (createSequence.IfNotExists && m_context.Database.GetSequence(createSequence.SequenceName) != null)
            return new WitSqlResult();

        m_context.Database.CreateSequence(createSequence.SequenceName, createSequence.StartWith);
        return new WitSqlResult();
    }

    private WitSqlResult ExecuteDropSequence(WitSqlStatementDropSequence dropSequence)
    {
        if (dropSequence.IfExists && m_context.Database.GetSequence(dropSequence.SequenceName) == null)
            return new WitSqlResult();

        m_context.Database.DropSequence(dropSequence.SequenceName);
        return new WitSqlResult();
    }

    private WitSqlResult ExecuteAlterSequence(WitSqlStatementAlterSequence alterSequence)
    {
        m_context.Database.RestartSequence(alterSequence.SequenceName, alterSequence.RestartWith);
        return new WitSqlResult();
    }

    #endregion

    #region Type Mapping

    private static WitDataType MapDataType(WitSqlDataType dataType)
    {
        return dataType.TypeName.ToUpperInvariant() switch
        {
            "TINYINT" or "INT8" => WitDataType.Int8,
            "UTINYINT" or "UINT8" or "BYTE" => WitDataType.UInt8,
            "SMALLINT" or "INT16" or "SHORT" => WitDataType.Int16,
            "USMALLINT" or "UINT16" or "USHORT" => WitDataType.UInt16,
            "INT" or "INT32" or "INTEGER" => WitDataType.Int32,
            "UINT" or "UINT32" => WitDataType.UInt32,
            "BIGINT" or "INT64" or "LONG" => WitDataType.Int64,
            "UBIGINT" or "UINT64" or "ULONG" => WitDataType.UInt64,
            "FLOAT16" or "HALF" => WitDataType.Float16,
            "FLOAT" or "FLOAT32" or "REAL" => WitDataType.Float32,
            "DOUBLE" or "FLOAT64" => WitDataType.Float64,
            "DECIMAL" or "NUMERIC" or "MONEY" => WitDataType.Decimal,
            "BOOLEAN" or "BOOL" or "BIT" => WitDataType.Boolean,
            "DATE" or "DATEONLY" => WitDataType.DateOnly,
            "TIME" or "TIMEONLY" => WitDataType.TimeOnly,
            "DATETIME" or "TIMESTAMP" or "DATETIME2" => WitDataType.DateTime,
            "DATETIMEOFFSET" => WitDataType.DateTimeOffset,
            "TIMESPAN" or "DURATION" or "INTERVAL" => WitDataType.TimeSpan,
            "GUID" or "UUID" or "UNIQUEIDENTIFIER" => WitDataType.Guid,
            "CHAR" or "NCHAR" => WitDataType.StringFixed,
            "VARCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or "STRING" => WitDataType.StringVariable,
            "BINARY" => WitDataType.BinaryFixed,
            "VARBINARY" or "BLOB" => WitDataType.BinaryVariable,
            "ROWVERSION" => WitDataType.RowVersion,
            "JSON" or "JSONB" => WitDataType.Json,
            _ => WitDataType.StringVariable
        };
    }

    private static ReferenceAction MapReferenceAction(ReferenceActionType action)
    {
        return action switch
        {
            ReferenceActionType.NoAction => ReferenceAction.NoAction,
            ReferenceActionType.Restrict => ReferenceAction.Restrict,
            ReferenceActionType.Cascade => ReferenceAction.Cascade,
            ReferenceActionType.SetNull => ReferenceAction.SetNull,
            ReferenceActionType.SetDefault => ReferenceAction.SetDefault,
            _ => ReferenceAction.NoAction
        };
    }

    private static TriggerTime MapTriggerTiming(TriggerTimingType timing)
    {
        return timing switch
        {
            TriggerTimingType.Before => TriggerTime.Before,
            TriggerTimingType.After => TriggerTime.After,
            TriggerTimingType.InsteadOf => TriggerTime.InsteadOf,
            _ => TriggerTime.After
        };
    }

    private static TriggerEvent MapTriggerEvent(TriggerEventType evt)
    {
        return evt switch
        {
            TriggerEventType.Insert => TriggerEvent.Insert,
            TriggerEventType.Update => TriggerEvent.Update,
            TriggerEventType.Delete => TriggerEvent.Delete,
            _ => TriggerEvent.Insert
        };
    }

    #endregion
}
