using NSubstitute;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using DbDefinitions = OutWit.Database.Definitions;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for TRIGGER and SEQUENCE statement execution.
/// </summary>
[TestFixture]
public class StatementExecutorTriggerSequenceTests : StatementExecutorTestsBase
{
    #region CREATE TRIGGER Tests

    [Test]
    public void CreateTriggerAfterInsertTest()
    {
        m_database.GetTrigger("trg_users_insert").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER trg_users_insert 
            AFTER INSERT ON Users
            BEGIN
                SELECT 1;
            END
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTrigger(Arg.Is<DbDefinitions.DefinitionTrigger>(t =>
            t.Name == "trg_users_insert" &&
            t.TableName == "Users" &&
            t.Time == DbDefinitions.TriggerTime.After &&
            t.Event == DbDefinitions.TriggerEvent.Insert
        ));
    }

    [Test]
    public void CreateTriggerBeforeUpdateTest()
    {
        m_database.GetTrigger("trg_users_update").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER trg_users_update 
            BEFORE UPDATE ON Users
            BEGIN
                SELECT 1;
            END
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTrigger(Arg.Is<DbDefinitions.DefinitionTrigger>(t =>
            t.Time == DbDefinitions.TriggerTime.Before &&
            t.Event == DbDefinitions.TriggerEvent.Update
        ));
    }

    [Test]
    public void CreateTriggerInsteadOfDeleteTest()
    {
        m_database.GetTrigger("trg_users_delete").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER trg_users_delete 
            INSTEAD OF DELETE ON Users
            BEGIN
                SELECT 1;
            END
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTrigger(Arg.Is<DbDefinitions.DefinitionTrigger>(t =>
            t.Time == DbDefinitions.TriggerTime.InsteadOf &&
            t.Event == DbDefinitions.TriggerEvent.Delete
        ));
    }

    [Test]
    public void CreateTriggerWithWhenConditionTest()
    {
        m_database.GetTrigger("trg_conditional").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER trg_conditional 
            AFTER INSERT ON Users
            WHEN (NEW.Status = 'active')
            BEGIN
                SELECT 1;
            END
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTrigger(Arg.Is<DbDefinitions.DefinitionTrigger>(t =>
            t.WhenCondition != null
        ));
    }

    [Test]
    public void CreateTriggerForEachRowTest()
    {
        m_database.GetTrigger("trg_row").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER trg_row 
            AFTER INSERT ON Users
            FOR EACH ROW
            BEGIN
                SELECT 1;
            END
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTrigger(Arg.Is<DbDefinitions.DefinitionTrigger>(t =>
            t.ForEachRow
        ));
    }

    [Test]
    public void CreateTriggerIfNotExistsAlreadyExistsTest()
    {
        m_database.GetTrigger("existing_trigger").Returns(new DbDefinitions.DefinitionTrigger
        {
            Name = "existing_trigger",
            TableName = "Users",
            Time = DbDefinitions.TriggerTime.After,
            Event = DbDefinitions.TriggerEvent.Insert,
            Body = "SELECT 1"
        });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER IF NOT EXISTS existing_trigger 
            AFTER INSERT ON Users
            BEGIN
                SELECT 2;
            END
        ");

        executor.Execute(stmt);

        m_database.DidNotReceive().CreateTrigger(Arg.Any<DbDefinitions.DefinitionTrigger>());
    }

    #endregion

    #region DROP TRIGGER Tests

    [Test]
    public void DropTriggerTest()
    {
        m_database.GetTrigger("trg_test").Returns(new DbDefinitions.DefinitionTrigger
        {
            Name = "trg_test",
            TableName = "Users",
            Time = DbDefinitions.TriggerTime.After,
            Event = DbDefinitions.TriggerEvent.Insert,
            Body = "SELECT 1"
        });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TRIGGER trg_test");

        executor.Execute(stmt);

        m_database.Received(1).DropTrigger("trg_test");
    }

    [Test]
    public void DropTriggerIfExistsNotFoundTest()
    {
        m_database.GetTrigger("non_existent").Returns((DbDefinitions.DefinitionTrigger?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TRIGGER IF EXISTS non_existent");

        // Should not throw
        executor.Execute(stmt);

        m_database.DidNotReceive().DropTrigger(Arg.Any<string>());
    }

    #endregion

    #region CREATE SEQUENCE Tests

    [Test]
    public void CreateSequenceTest()
    {
        m_database.GetSequence("seq_order_id").Returns((DbDefinitions.DefinitionSequence?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE seq_order_id");

        executor.Execute(stmt);

        m_database.Received(1).CreateSequence("seq_order_id", Arg.Any<long>());
    }

    [Test]
    public void CreateSequenceWithStartValueTest()
    {
        m_database.GetSequence("seq_order_id").Returns((DbDefinitions.DefinitionSequence?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE seq_order_id START WITH 1000");

        executor.Execute(stmt);

        m_database.Received(1).CreateSequence("seq_order_id", 1000);
    }

    [Test]
    public void CreateSequenceIfNotExistsAlreadyExistsTest()
    {
        m_database.GetSequence("existing_seq").Returns(new DbDefinitions.DefinitionSequence { Name = "existing_seq", CurrentValue = 1 });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE IF NOT EXISTS existing_seq");

        executor.Execute(stmt);

        m_database.DidNotReceive().CreateSequence(Arg.Any<string>(), Arg.Any<long>());
    }

    #endregion

    #region DROP SEQUENCE Tests

    [Test]
    public void DropSequenceTest()
    {
        m_database.GetSequence("seq_test").Returns(new DbDefinitions.DefinitionSequence { Name = "seq_test", CurrentValue = 1 });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP SEQUENCE seq_test");

        executor.Execute(stmt);

        m_database.Received(1).DropSequence("seq_test");
    }

    [Test]
    public void DropSequenceIfExistsNotFoundTest()
    {
        m_database.GetSequence("non_existent").Returns((DbDefinitions.DefinitionSequence?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP SEQUENCE IF EXISTS non_existent");

        // Should not throw
        executor.Execute(stmt);

        m_database.DidNotReceive().DropSequence(Arg.Any<string>());
    }

    #endregion

    #region ALTER SEQUENCE Tests

    [Test]
    public void AlterSequenceRestartTest()
    {
        m_database.GetSequence("seq_test").Returns(new DbDefinitions.DefinitionSequence { Name = "seq_test", CurrentValue = 100 });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER SEQUENCE seq_test RESTART WITH 1");

        executor.Execute(stmt);

        m_database.Received(1).RestartSequence("seq_test", 1);
    }

    #endregion
}
