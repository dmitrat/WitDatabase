using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Visitor;

internal sealed partial class WitSqlVisitor
{
    #region CREATE TABLE Statement

    public override WitSqlStatementCreateTable VisitCreateTableStatement(WitSqlParser.CreateTableStatementContext context)
    {
        var columns = new List<WitSqlColumn>();
        var constraints = new List<TableConstraint>();

        foreach (var element in context.tableElement())
        {
            if (element.columnDefinition() is { } colDef)
            {
                columns.Add(VisitColumnDefinition(colDef));
            }
            else if (element.tableConstraint() is { } tableCons)
            {
                constraints.Add(VisitTableConstraint(tableCons));
            }
        }

        return new WitSqlStatementCreateTable
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            IfNotExists = context.EXISTS() != null,
            Columns = columns,
            Constraints = constraints.Count > 0 ? constraints : null
        };
    }

    public override WitSqlColumn VisitColumnDefinition(WitSqlParser.ColumnDefinitionContext context)
    {
        return new WitSqlColumn
        {
            Name = context.columnName().GetText(),
            DataType = VisitDataType(context.dataType()),
            Constraints = context.columnConstraint()?.Select(VisitColumnConstraint).ToList()
        };
    }

    public override WitSqlDataType VisitDataType(WitSqlParser.DataTypeContext context)
    {
        var typeName = context.typeName().GetText().ToUpperInvariant();
        int? length = null;
        int? precision = null;
        int? scale = null;

        var typeParams = context.typeParam();
        if (typeParams.Length >= 1)
        {
            var firstParam = typeParams[0].GetText();
            if (firstParam.Equals("MAX", StringComparison.OrdinalIgnoreCase))
                length = int.MaxValue;
            else if (int.TryParse(firstParam, out var len))
                length = len;
        }
        if (typeParams.Length >= 2 && int.TryParse(typeParams[1].GetText(), out var s))
        {
            precision = length;
            scale = s;
            length = null;
        }

        return new WitSqlDataType
        {
            TypeName = typeName,
            Length = length,
            Precision = precision,
            Scale = scale
        };
    }

    private ColumnConstraint VisitColumnConstraint(WitSqlParser.ColumnConstraintContext context)
    {
        return context switch
        {
            WitSqlParser.NullConstraintContext nullCtx => new ColumnConstraintNotNull
            {
                IsNotNull = nullCtx.NOT() != null
            },
            WitSqlParser.PrimaryKeyConstraintContext pk => new ColumnConstraintPrimaryKey
            {
                AutoIncrement = pk.AUTOINCREMENT() != null
            },
            WitSqlParser.UniqueConstraintContext => new ColumnConstraintUnique(),
            WitSqlParser.DefaultConstraintContext def => new ColumnConstraintDefault
            {
                Value = def.expression() != null
                    ? VisitExpression(def.expression())
                    : VisitLiteral(def.literal())
            },
            WitSqlParser.CheckConstraintContext check => new ColumnConstraintCheck
            {
                Condition = VisitExpression(check.expression())
            },
            WitSqlParser.ReferencesConstraintContext refs => new ColumnConstraintReferences
            {
                ForeignTable = refs.tableName().GetText(),
                ForeignColumn = refs.columnName()?.GetText(),
                OnDelete = GetReferenceAction(refs.referenceOption(), isDelete: true),
                OnUpdate = GetReferenceAction(refs.referenceOption(), isDelete: false)
            },
            _ => throw new InvalidOperationException($"Unknown column constraint: {context.GetType()}")
        };
    }

    private static ReferenceActionType GetReferenceAction(WitSqlParser.ReferenceOptionContext[] options, bool isDelete)
    {
        foreach (var opt in options)
        {
            bool matches = isDelete ? opt.DELETE() != null : opt.UPDATE() != null;
            if (!matches) continue;

            var action = opt.referenceAction();
            if (action.CASCADE() != null) return ReferenceActionType.Cascade;
            if (action.RESTRICT() != null) return ReferenceActionType.Restrict;
            if (action.NULL() != null) return ReferenceActionType.SetNull;
            if (action.DEFAULT() != null) return ReferenceActionType.SetDefault;
        }
        return ReferenceActionType.NoAction;
    }

    private TableConstraint VisitTableConstraint(WitSqlParser.TableConstraintContext context)
    {
        return context switch
        {
            WitSqlParser.TablePrimaryKeyContext pk => new TableConstraintPrimaryKey
            {
                Columns = pk.columnName().Select(c => c.GetText()).ToList()
            },
            WitSqlParser.TableUniqueContext uniq => new TableConstraintUnique
            {
                Columns = uniq.columnName().Select(c => c.GetText()).ToList()
            },
            WitSqlParser.TableForeignKeyContext fk => ParseTableForeignKey(fk),
            WitSqlParser.TableCheckContext check => new TableConstraintCheck
            {
                Condition = VisitExpression(check.expression())
            },
            _ => throw new InvalidOperationException($"Unknown table constraint: {context.GetType()}")
        };
    }

    private TableConstraintForeignKey ParseTableForeignKey(WitSqlParser.TableForeignKeyContext fk)
    {
        // The grammar:
        // FOREIGN KEY LPAREN columnName (COMMA columnName)* RPAREN
        //     REFERENCES tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
        //
        // We need to find where the REFERENCES keyword is to split local vs foreign columns.
        // All columnName tokens are in a flat list, but local columns come before REFERENCES,
        // and foreign columns come after the tableName.

        var allColumnNames = fk.columnName();
        var referencesToken = fk.REFERENCES();

        // Find which columnNames come before REFERENCES and which after
        var localColumns = new List<string>();
        var foreignColumns = new List<string>();

        int referencesPos = referencesToken.Symbol.StartIndex;

        foreach (var col in allColumnNames)
        {
            if (col.Stop.StopIndex < referencesPos)
            {
                localColumns.Add(col.GetText());
            }
            else
            {
                foreignColumns.Add(col.GetText());
            }
        }

        return new TableConstraintForeignKey
        {
            Columns = localColumns,
            ForeignTable = fk.tableName().GetText(),
            ForeignColumns = foreignColumns.Count > 0 ? foreignColumns : null,
            OnDelete = GetReferenceAction(fk.referenceOption(), isDelete: true),
            OnUpdate = GetReferenceAction(fk.referenceOption(), isDelete: false)
        };
    }

    #endregion

    #region DROP TABLE Statement

    public override WitSqlStatementDropTable VisitDropTableStatement(WitSqlParser.DropTableStatementContext context)
    {
        return new WitSqlStatementDropTable
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region ALTER TABLE Statement

    public override WitSqlStatementAlterTable VisitAlterTableStatement(WitSqlParser.AlterTableStatementContext context)
    {
        return new WitSqlStatementAlterTable
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText(),
            Action = VisitAlterAction(context.alterAction())
        };
    }

    private AlterAction VisitAlterAction(WitSqlParser.AlterActionContext context)
    {
        return context switch
        {
            WitSqlParser.AlterAddColumnContext add => new AlterActionAddColumn
            {
                WitSqlColumn = VisitColumnDefinition(add.columnDefinition())
            },
            WitSqlParser.AlterDropColumnContext drop => new AlterActionDropColumn
            {
                ColumnName = drop.columnName().GetText()
            },
            WitSqlParser.AlterRenameTableContext rename => new AlterActionRenameTable
            {
                NewName = rename.tableName().GetText()
            },
            WitSqlParser.AlterRenameColumnContext renameCol => new AlterActionRenameColumn
            {
                OldName = renameCol.columnName(0).GetText(),
                NewName = renameCol.columnName(1).GetText()
            },
            WitSqlParser.AlterAlterColumnContext alterCol => ParseAlterColumnAction(alterCol),
            _ => throw new InvalidOperationException($"Unknown alter action: {context.GetType()}")
        };
    }

    private AlterActionAlterColumn ParseAlterColumnAction(WitSqlParser.AlterAlterColumnContext context)
    {
        var columnName = context.columnName().GetText();
        var actionContext = context.alterColumnAction();

        return actionContext switch
        {
            WitSqlParser.AlterColumnTypeContext tc => new AlterActionAlterColumn
            {
                ColumnName = columnName,
                NewType = VisitDataType(tc.dataType())
            },
            WitSqlParser.AlterColumnSetDefaultContext sd => new AlterActionAlterColumn
            {
                ColumnName = columnName,
                NewDefault = VisitExpression(sd.expression())
            },
            WitSqlParser.AlterColumnDropDefaultContext => new AlterActionAlterColumn
            {
                ColumnName = columnName,
                DropDefault = true
            },
            WitSqlParser.AlterColumnSetNotNullContext => new AlterActionAlterColumn
            {
                ColumnName = columnName,
                SetNotNull = true
            },
            WitSqlParser.AlterColumnDropNotNullContext => new AlterActionAlterColumn
            {
                ColumnName = columnName,
                SetNotNull = false
            },
            _ => throw new InvalidOperationException($"Unknown alter column action: {actionContext.GetType()}")
        };
    }

    #endregion

    #region CREATE/DROP INDEX Statements

    public override WitSqlStatementCreateIndex VisitCreateIndexStatement(WitSqlParser.CreateIndexStatementContext context)
    {
        return new WitSqlStatementCreateIndex
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IndexName = context.indexName().GetText(),
            TableName = context.tableName().GetText(),
            IsUnique = context.UNIQUE() != null,
            IfNotExists = context.EXISTS() != null,
            Columns = context.indexColumn().Select(c => new ClauseIndexColumn
            {
                ColumnName = c.columnName().GetText(),
                Descending = c.DESC() != null
            }).ToList()
        };
    }

    public override WitSqlStatementDropIndex VisitDropIndexStatement(WitSqlParser.DropIndexStatementContext context)
    {
        return new WitSqlStatementDropIndex
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IndexName = context.indexName().GetText(),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region CREATE/DROP VIEW Statements

    public override WitSqlStatementCreateView VisitCreateViewStatement(WitSqlParser.CreateViewStatementContext context)
    {
        var columnNames = context.columnName()?.Select(c => c.GetText()).ToList();

        return new WitSqlStatementCreateView
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            ViewName = context.viewName().GetText(),
            IfNotExists = context.EXISTS() != null,
            ColumnNames = columnNames,
            Query = VisitQueryExpression(context.queryExpression())
        };
    }

    public override WitSqlStatementDropView VisitDropViewStatement(WitSqlParser.DropViewStatementContext context)
    {
        return new WitSqlStatementDropView
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            ViewName = context.viewName().GetText(),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region CREATE/DROP TRIGGER Statements

    public override WitSqlStatementCreateTrigger VisitCreateTriggerStatement(WitSqlParser.CreateTriggerStatementContext context)
    {
        var timing = context.triggerTime().BEFORE() != null ? TriggerTimingType.Before :
                     context.triggerTime().AFTER() != null ? TriggerTimingType.After :
                     TriggerTimingType.InsteadOf;

        var evt = context.triggerEvent().INSERT() != null ? TriggerEventType.Insert :
                  context.triggerEvent().DELETE() != null ? TriggerEventType.Delete :
                  TriggerEventType.Update;

        var updateColumns = context.triggerEvent().UPDATE() != null && context.triggerEvent().columnName().Length > 0
            ? context.triggerEvent().columnName().Select(c => c.GetText()).ToList()
            : null;

        var body = new List<WitSqlStatement>();
        var bodyStatements = context.statement();
        foreach (var stmtCtx in bodyStatements)
        {
            var stmt = VisitStatement(stmtCtx);
            if (stmt != null)
                body.Add(stmt);
        }

        return new WitSqlStatementCreateTrigger
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TriggerName = context.triggerName().GetText(),
            IfNotExists = context.EXISTS() != null,
            Time = timing,
            Event = evt,
            UpdateColumns = updateColumns,
            TableName = context.tableName().GetText(),
            ForEachRow = context.ROW() != null,
            WhenCondition = context.expression() != null ? VisitExpression(context.expression()) : null,
            Body = body
        };
    }

    public override WitSqlStatementDropTrigger VisitDropTriggerStatement(WitSqlParser.DropTriggerStatementContext context)
    {
        return new WitSqlStatementDropTrigger
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TriggerName = context.triggerName().GetText(),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region CREATE/DROP/ALTER SEQUENCE Statements

    public override WitSqlStatementCreateSequence VisitCreateSequenceStatement(WitSqlParser.CreateSequenceStatementContext context)
    {
        long startWith = 1;
        if (context.INTEGER_LITERAL() != null)
        {
            startWith = long.Parse(context.INTEGER_LITERAL().GetText());
        }

        return new WitSqlStatementCreateSequence
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            SequenceName = context.sequenceName().GetText(),
            IfNotExists = context.EXISTS() != null,
            StartWith = startWith
        };
    }

    public override WitSqlStatementDropSequence VisitDropSequenceStatement(WitSqlParser.DropSequenceStatementContext context)
    {
        return new WitSqlStatementDropSequence
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            SequenceName = context.sequenceName().GetText(),
            IfExists = context.EXISTS() != null
        };
    }

    public override WitSqlStatementAlterSequence VisitAlterSequenceStatement(WitSqlParser.AlterSequenceStatementContext context)
    {
        long? restartWith = null;
        if (context.INTEGER_LITERAL() != null)
        {
            restartWith = long.Parse(context.INTEGER_LITERAL().GetText());
        }

        return new WitSqlStatementAlterSequence
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            SequenceName = context.sequenceName().GetText(),
            RestartWith = restartWith
        };
    }

    #endregion

    #region TRUNCATE TABLE Statement

    public override WitSqlStatementTruncate VisitTruncateTableStatement(WitSqlParser.TruncateTableStatementContext context)
    {
        return new WitSqlStatementTruncate
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            TableName = context.tableName().GetText()
        };
    }

    #endregion
}
