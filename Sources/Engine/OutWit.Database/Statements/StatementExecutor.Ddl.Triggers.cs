using OutWit.Database.Definitions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;

namespace OutWit.Database.Statements;

/// <summary>
/// DDL execution for TRIGGER operations (CREATE, DROP).
/// </summary>
public sealed partial class StatementExecutor
{
    #region CREATE TRIGGER

    private WitSqlResult ExecuteCreateTrigger(WitSqlStatementCreateTrigger createTrigger)
    {
        // Check IF NOT EXISTS
        if (createTrigger.IfNotExists && m_context.Database.GetTrigger(createTrigger.TriggerName) != null)
            return new WitSqlResult();

        RefuseNonDmlBody(createTrigger);

        // Both of these are descriptions for INFORMATION_SCHEMA, not the trigger itself - the
        // trigger is the When/Statements trees stored below.
        var triggerMetadata = new DefinitionTrigger
        {
            Name = createTrigger.TriggerName,
            TableName = createTrigger.TableName,
            Time = MapTriggerTiming(createTrigger.Time),
            Event = MapTriggerEvent(createTrigger.Event),
            UpdateColumns = createTrigger.UpdateColumns,
            ForEachRow = createTrigger.ForEachRow,
            When = createTrigger.WhenCondition,
            Statements = createTrigger.Body
        };

        m_context.Database.CreateTrigger(triggerMetadata);
        return new WitSqlResult();
    }

    #endregion

    #region DROP TRIGGER

    private WitSqlResult ExecuteDropTrigger(WitSqlStatementDropTrigger dropTrigger)
    {
        if (dropTrigger.IfExists && m_context.Database.GetTrigger(dropTrigger.TriggerName) == null)
            return new WitSqlResult();

        m_context.Database.DropTrigger(dropTrigger.TriggerName);
        return new WitSqlResult();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Refuses a trigger whose body contains anything but DML.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grammar admits any statement in a trigger body, because the body rule references the
    /// top-level <c>statement</c> rule. The engine cannot run most of them: DDL and transaction
    /// control take the write lock that the statement which fired the trigger is already holding,
    /// so the trigger throws <i>"Cannot acquire write lock - current thread already holds write
    /// lock"</i> when it fires - <b>and it throws part-way through</b>. Measured 2026-07-31:
    /// <c>BEGIN CREATE TABLE Z (Id INT); END</c> fired, failed, and left table Z created.
    /// </para>
    /// <para>
    /// Before 9.0.0 this was hidden: the body had to be rendered as text to be stored, and the
    /// renderer refused non-DML, so <c>CREATE TRIGGER</c> failed with
    /// <c>NotSupportedException</c> - the right outcome for the wrong reason, from a piece of code
    /// that only exists to fill a catalog column. With the body now stored as a tree that accident
    /// is gone, so the refusal is made deliberate and given a reason a caller can act on.
    /// </para>
    /// <para>
    /// SQLite's trigger bodies are DML-only for the same practical reason. PostgreSQL runs triggers
    /// through functions, which is a different model this engine does not have.
    /// </para>
    /// </remarks>
    private static void RefuseNonDmlBody(WitSqlStatementCreateTrigger createTrigger)
    {
        foreach (var statement in createTrigger.Body)
        {
            if (statement is WitSqlStatementSelect or WitSqlStatementInsert
                or WitSqlStatementUpdate or WitSqlStatementDelete or WitSqlStatementMerge)
            {
                continue;
            }

            throw new NotSupportedException(
                $"A trigger body may contain only SELECT, INSERT, UPDATE, DELETE and MERGE. " +
                $"Trigger '{createTrigger.TriggerName}' contains " +
                $"{Describe(statement)}, which cannot run inside a trigger.");
        }
    }

    private static string Describe(WitSqlStatement statement) => statement switch
    {
        WitSqlStatementCreateTable => "CREATE TABLE",
        WitSqlStatementCreateIndex => "CREATE INDEX",
        WitSqlStatementCreateView => "CREATE VIEW",
        WitSqlStatementCreateTrigger => "CREATE TRIGGER",
        WitSqlStatementAlterTable => "ALTER TABLE",
        WitSqlStatementDropTable => "DROP TABLE",
        WitSqlStatementBeginTransaction => "BEGIN TRANSACTION",
        WitSqlStatementCommit => "COMMIT",
        WitSqlStatementRollback => "ROLLBACK",
        _ => statement.GetType().Name
    };


    /// <summary>
    /// Renders the body for <c>INFORMATION_SCHEMA.TRIGGERS</c> to report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <c>null</c> for a statement the renderer cannot express, rather than throwing.
    /// Until 9.0.0 this was the only thing standing between a trigger and being created: the body
    /// was <b>stored</b> as this text, so a statement that would not render could not be stored.
    /// Now the statements themselves are stored and this text is a description of them, and
    /// <b>a description must never be able to refuse a write</b>. Measured 2026-07-31: with the
    /// storage already fixed, <c>CREATE TRIGGER … BEGIN CREATE TABLE Z (Id INT); END</c> still
    /// failed - and it failed inside the code that exists to fill a catalog column.
    /// </para>
    /// <para>
    /// Null rather than a placeholder comment. A comment reads as rendered SQL to anything that
    /// consumes the column, and "something was emitted" is exactly the mistake to avoid here.
    /// </para>
    /// </remarks>
    private static string? SerializeTriggerBody(IReadOnlyList<WitSqlStatement> statements)
    {
        return SchemaText.Render(statements);
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
