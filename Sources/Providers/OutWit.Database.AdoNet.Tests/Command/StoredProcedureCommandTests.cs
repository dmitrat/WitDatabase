using System.Data;
using System.Data.Common;
using NUnit.Framework;

namespace OutWit.Database.AdoNet.Tests.Command;

/// <summary>
/// <c>CommandType.StoredProcedure</c> - how an ADO.NET caller invokes a procedure.
/// </summary>
/// <remarks>
/// <para>
/// The last step of phase 9d, and the one without which the subsystem would exist and be
/// unreachable: setting <c>CommandText</c> to a routine name and <c>CommandType</c> to
/// <c>StoredProcedure</c> is how every ADO.NET consumer calls one, and it threw
/// <c>NotSupportedException</c> until now.
/// </para>
/// <para>
/// <b>Everything here is exercised through <see cref="DbConnection"/> and <see cref="DbCommand"/>,
/// never through the concrete types.</b> That is a standing rule in this project and it was written
/// after a shadowed <c>Save</c> passed every test on <c>WitDbTransaction</c> and threw on
/// <c>DbTransaction</c> - the type a real consumer holds. A drop-in that only works when you know
/// its own class name is not a drop-in.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StoredProcedureCommandTests
{
    #region Setup

    private DbConnection m_connection = null!;

    [SetUp]
    public void Setup()
    {
        m_connection = new WitDbConnection("Data Source=:memory:");
        m_connection.Open();

        Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT)");
        Execute("INSERT INTO T (Id, V) VALUES (1, 10)");
        Execute("INSERT INTO T (Id, V) VALUES (2, 20)");
        Execute("CREATE TABLE Log (Id INT PRIMARY KEY AUTOINCREMENT, Note VARCHAR(100))");
    }

    [TearDown]
    public void TearDown() => m_connection?.Dispose();

    #endregion

    #region It is accepted

    [Test]
    public void StoredProcedureIsAnAcceptedCommandTypeTest()
    {
        using var command = m_connection.CreateCommand();

        Assert.That(() => command.CommandType = CommandType.StoredProcedure, Throws.Nothing);
        Assert.That(command.CommandType, Is.EqualTo(CommandType.StoredProcedure));
    }

    /// <summary>
    /// And <c>TableDirect</c> is still refused, with a message that says what to use.
    /// </summary>
    [Test]
    public void TableDirectIsStillRefusedTest()
    {
        using var command = m_connection.CreateCommand();

        Assert.That(() => command.CommandType = CommandType.TableDirect,
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("StoredProcedure"));
    }

    #endregion

    #region Reading a result

    [Test]
    public void AProcedureReturningRowsIsReadableTest()
    {
        Execute("CREATE PROCEDURE GetAll AS BEGIN SELECT Id FROM T ORDER BY Id; END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "GetAll";

        var ids = new List<long>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
        }

        Assert.That(ids, Is.EqualTo(new[] { 1L, 2L }));
    }

    [Test]
    public void AProcedureReturningOneValueWorksWithExecuteScalarTest()
    {
        Execute("CREATE PROCEDURE CountRows AS BEGIN SELECT COUNT(*) FROM T; END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "CountRows";

        Assert.That(Convert.ToInt64(command.ExecuteScalar()), Is.EqualTo(2));
    }

    #endregion

    #region Parameters

    [Test]
    public void ParametersArePassedAsArgumentsTest()
    {
        Execute(@"
            CREATE PROCEDURE Write2(Note VARCHAR(100)) AS BEGIN
                INSERT INTO Log (Note) VALUES (Note);
            END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "Write2";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "Note";
        parameter.Value = "from ado";
        command.Parameters.Add(parameter);

        command.ExecuteNonQuery();

        Assert.That(Scalar("SELECT Note FROM Log"), Is.EqualTo("from ado"));
    }

    /// <summary>
    /// Several parameters keep the order the caller added them in.
    /// </summary>
    /// <remarks>
    /// A <c>CALL</c>'s arguments are positional and ADO's are named, and the collection order is what
    /// the caller wrote. Matching by name against the catalog's parameter names instead would
    /// silently reorder the arguments whenever the names differ - a wrong answer with no error.
    /// </remarks>
    [Test]
    public void SeveralParametersKeepTheirOrderTest()
    {
        Execute(@"
            CREATE PROCEDURE Sub2(A INT, B INT) AS BEGIN
                INSERT INTO Log (Note) VALUES (TOSTRING(A - B));
            END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "Sub2";

        Add(command, "A", 30);
        Add(command, "B", 4);

        command.ExecuteNonQuery();

        Assert.That(Scalar("SELECT Note FROM Log"), Is.EqualTo("26"),
            "26 is A - B; 4 - 30 would mean the arguments were reordered");
    }

    /// <summary>
    /// An argument is bound, never interpolated.
    /// </summary>
    /// <remarks>
    /// The <c>CALL</c> is built from the parameters' <b>names</b>, and the engine binds the values
    /// from the same dictionary a text command uses. So a string argument cannot become syntax - the
    /// property that makes parameters worth having in the first place.
    /// </remarks>
    [Test]
    public void AnArgumentCannotBecomeSyntaxTest()
    {
        Execute(@"
            CREATE PROCEDURE Write2(Note VARCHAR(100)) AS BEGIN
                INSERT INTO Log (Note) VALUES (Note);
            END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "Write2";

        Add(command, "Note", "'); DROP TABLE T; --");

        command.ExecuteNonQuery();

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT Note FROM Log"), Is.EqualTo("'); DROP TABLE T; --"),
                "the argument must be stored as the text it is");
            Assert.That(Convert.ToInt64(ScalarObject("SELECT COUNT(*) FROM T")), Is.EqualTo(2),
                "and T must still be there");
        });
    }

    [Test]
    public void TheWrongNumberOfArgumentsIsReportedTest()
    {
        Execute("CREATE PROCEDURE Sub2(A INT, B INT) AS BEGIN SELECT A - B; END");

        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "Sub2";
        Add(command, "A", 1);

        Assert.That(() => command.ExecuteNonQuery(), Throws.InstanceOf<DbException>(),
            "an ADO caller must get a DbException, not whatever the engine threw");
    }

    [Test]
    public void CallingSomethingThatIsNotThereIsReportedTest()
    {
        using var command = m_connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "NeverExisted";

        Assert.That(() => command.ExecuteNonQuery(), Throws.InstanceOf<DbException>());
    }

    #endregion

    #region Text commands are unaffected

    /// <summary>
    /// A command left as <c>Text</c> behaves exactly as before, including a prepared one.
    /// </summary>
    /// <remarks>
    /// The stored-procedure path builds its SQL from the parameter collection, which a caller may
    /// change between executions, so such a command is never served from the prepared statement.
    /// This asserts the other half - that adding that branch did not take preparation away from the
    /// commands that had it.
    /// </remarks>
    [Test]
    public void ATextCommandStillPreparesTest()
    {
        using var command = m_connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T";
        command.Prepare();

        Assert.That(Convert.ToInt64(command.ExecuteScalar()), Is.EqualTo(2));
        Assert.That(Convert.ToInt64(command.ExecuteScalar()), Is.EqualTo(2),
            "and a second execution of the prepared command still works");
    }

    #endregion

    #region Helpers

    private void Execute(string sql)
    {
        using var command = m_connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private string? Scalar(string sql) => ScalarObject(sql)?.ToString();

    private object? ScalarObject(string sql)
    {
        using var command = m_connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    #endregion
}
