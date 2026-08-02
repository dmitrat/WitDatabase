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
    /// top-level <c>statement</c> rule.
    /// </para>
    /// <para>
    /// <b>The reason this refusal was written is gone, and this paragraph replaces it.</b> Until
    /// 2026-08-01 it said: DDL takes the write lock the firing statement is already holding, so the
    /// trigger throws part-way and leaves half its work behind. That was true and is not any more -
    /// schema writes now go through the caller's transaction, and <c>AUDIT-2026-07.md</c> finding 19
    /// is closed. A refusal resting on a defect that has been fixed is a refusal the next reader
    /// deletes, correctly, on the evidence in front of them.
    /// </para>
    /// <para>
    /// <b>The reason it is kept is different and was measured on 2026-08-01.</b> A trigger body runs
    /// <i>inside a loop over rows</i>, and DDL against the object that loop is walking is not
    /// something the engine can survive:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>DROP TABLE T</c> from a trigger on <c>T</c> <b>reports success and destroys T</b>, taking
    /// the row the statement had just written with it. No error is raised anywhere - see
    /// <c>TriggerBodyFidelityTests.DdlInsideATriggerDestroysWhatTheStatementIsWritingTest</c>, which
    /// pins it precisely so this refusal cannot be deleted on the assumption that it is obsolete.
    /// </description></item>
    /// <item><description>
    /// DDL against an <i>unrelated</i> object does work now, and a failing one rolls back cleanly.
    /// Both were measured. Allowing only that would mean deciding, per statement, whether a trigger
    /// body's DDL touches the object underneath it - a check on a moving target, for a case nobody
    /// has asked for.
    /// </description></item>
    /// </list>
    /// <para>
    /// Transaction control is refused for its own measured reason, and a stronger one: a nested
    /// <c>COMMIT</c> is stopped by nothing at all. It commits the firing statement's transaction, so
    /// the rest of that statement runs outside any transaction - a three-row <c>INSERT</c> whose
    /// third row failed left the first two behind and raised only the key violation. DDL fails
    /// loudly; this does not fail.
    /// </para>
    /// <para>
    /// <b>This is also why a trigger body may not <c>CALL</c> a procedure</b>, once procedures exist:
    /// a procedure is allowed DDL precisely because <c>CALL</c> at the top level is a statement and
    /// not a row loop. Letting a trigger reach one would put the row loop back underneath it, and
    /// would need a transitive check over the whole call graph to notice. Refusing <c>CALL</c> here
    /// is one line and needs no analysis. See <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 3.
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

            if (statement is WitSqlStatementCall)
            {
                throw new NotSupportedException(
                    $"Trigger '{createTrigger.TriggerName}' contains a CALL. A procedure body may "
                    + "contain DDL precisely because CALL at the top level is a statement and not a "
                    + "loop over rows; reaching one from a trigger would put the row loop back "
                    + "underneath it, where DROP TABLE against the table being written reports "
                    + "success and destroys it.");
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
        WitSqlStatementSavepoint => "SAVEPOINT",
        WitSqlStatementReleaseSavepoint => "RELEASE SAVEPOINT",
        WitSqlStatementSetTransaction => "SET TRANSACTION",
        WitSqlStatementCall => "CALL",
        WitSqlStatementCreateFunction => "CREATE FUNCTION",
        WitSqlStatementDropFunction => "DROP FUNCTION",
        WitSqlStatementCreateProcedure => "CREATE PROCEDURE",
        WitSqlStatementDropProcedure => "DROP PROCEDURE",
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
