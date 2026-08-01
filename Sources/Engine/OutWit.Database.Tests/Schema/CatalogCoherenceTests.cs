namespace OutWit.Database.Tests.Schema;

/// <summary>
/// The catalog holds one copy of each fact, and the text a caller sees is rendered from it.
/// </summary>
/// <remarks>
/// <para>
/// The first version of phase 8 stored schema twice - as a tree, which the engine evaluates, and as
/// text, which <c>INFORMATION_SCHEMA</c> reports. That broke something within a day:
/// <c>ALTER TABLE … ALTER COLUMN … SET DEFAULT</c> wrote the text and left the tree, so the
/// statement reported success, changed what the catalog said, and <b>changed nothing about what was
/// inserted</b>. <c>DROP DEFAULT</c> did the same. All 5,600 tests across five projects were green,
/// because the suite asserts that DDL is accepted rather than what it does.
/// </para>
/// <para>
/// So the second copy is gone. Nothing writes the legacy text fields from 9.0.0; they are read only
/// for a database written earlier, and the reportable SQL is rendered from the tree when asked. Two
/// things are checked here: that the behaviour is right, and that <b>no write path has quietly
/// started filling a text field again</b> - which is the only way the class can come back.
/// </para>
/// </remarks>
[TestFixture]
[Category("Schema")]
public class CatalogCoherenceTests : WitSqlEngineTestsBase
{
    #region Behaviour

    [Test]
    public void DefaultSurvivesBeingAlteredTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, N INT DEFAULT 1)");

        m_engine.Execute("INSERT INTO T (Id) VALUES (1)");
        Assert.That(Value(1), Is.EqualTo("1"), "the declared default applies");

        m_engine.Execute("ALTER TABLE T ALTER COLUMN N SET DEFAULT 5");
        m_engine.Execute("INSERT INTO T (Id) VALUES (2)");
        Assert.That(Value(2), Is.EqualTo("5"), "SET DEFAULT must change what gets inserted");

        m_engine.Execute("ALTER TABLE T ALTER COLUMN N DROP DEFAULT");
        m_engine.Execute("INSERT INTO T (Id) VALUES (3)");
        Assert.That(Value(3), Is.Null, "DROP DEFAULT must stop the default applying");
    }

    [Test]
    public void DroppedCheckStopsBeingEnforcedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, A INT, CONSTRAINT CK CHECK (A > 0))");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, A) VALUES (1, -1)"),
            Throws.Exception, "the constraint is enforced before it is dropped");

        m_engine.Execute("ALTER TABLE T DROP CONSTRAINT CK");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, A) VALUES (2, -1)"),
            Throws.Nothing, "after DROP CONSTRAINT the check must stop applying");
    }

    #endregion

    #region One copy only

    /// <summary>
    /// After every DDL shape that used to write one, no legacy text field is populated.
    /// </summary>
    /// <remarks>
    /// A populated one means a second copy of a fact exists again, and the next rewrite of that
    /// schema can leave the two disagreeing. This is the guard that keeps the class closed; it does
    /// not depend on anyone remembering why.
    /// </remarks>
    [Test]
    public void NothingWritesTheLegacyTextFieldsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, N INT DEFAULT 1, Age INT CHECK (Age >= 0))");
        m_engine.Execute("CREATE TABLE U (Id INT PRIMARY KEY, Price INT, Qty INT, Total AS (Price * Qty) STORED)");
        m_engine.Execute("CREATE TABLE V (Id INT PRIMARY KEY, A INT, CONSTRAINT CK CHECK (A > 0), CHECK (A < 100))");
        m_engine.Execute("CREATE INDEX IX ON T (Age) WHERE Age > 18");
        m_engine.Execute("CREATE INDEX IX2 ON T ((Age + 1))");
        m_engine.Execute("CREATE VIEW W AS SELECT Id FROM T");
        m_engine.Execute("CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW WHEN (Age > 1) BEGIN SELECT 1; END");

        m_engine.Execute("ALTER TABLE T ALTER COLUMN N SET DEFAULT 5");
        m_engine.Execute("ALTER TABLE T ADD COLUMN Extra INT DEFAULT 7");
        m_engine.Execute("ALTER TABLE V ADD CONSTRAINT CK2 CHECK (A > -1)");
        m_engine.Execute("ALTER TABLE V DROP CONSTRAINT CK");

        var populated = new List<string>();

        foreach (var name in new[] { "T", "U", "V" })
        {
            var table = m_engine.GetTable(name)!;

            Text(populated, $"{name}.CheckExpressions", table.CheckExpressions?.FirstOrDefault());

            foreach (var column in table.Columns)
            {
                Text(populated, $"{name}.{column.Name}.DefaultValue", column.DefaultValue);
                Text(populated, $"{name}.{column.Name}.CheckExpression", column.CheckExpression);
                Text(populated, $"{name}.{column.Name}.ComputedExpression", column.ComputedExpression);
            }

            foreach (var constraint in table.NamedConstraints ?? [])
                Text(populated, $"{name}.{constraint.Name}.CheckExpression", constraint.CheckExpression);
        }

        foreach (var indexName in new[] { "IX", "IX2" })
        {
            var index = m_engine.GetIndex(indexName);

            if (index is null)
                continue;

            Text(populated, $"{indexName}.WhereExpression", index.WhereExpression);
            Text(populated, $"{indexName}.ExpressionColumns", index.ExpressionColumns?.FirstOrDefault());
        }

        Text(populated, "W.SelectSql", m_engine.GetView("W")?.SelectSql);

        var trigger = m_engine.GetTrigger("TR");
        Text(populated, "TR.Body", trigger?.Body);
        Text(populated, "TR.WhenCondition", trigger?.WhenCondition);

        Assert.That(populated, Is.Empty,
            $"{populated.Count} legacy text fields were written, so a second copy of a fact exists " +
            $"again:{Environment.NewLine}{string.Join(Environment.NewLine, populated)}");
    }

    #endregion

    #region Derived answers come from the schema, not from its description

    /// <summary>
    /// Every question the engine asks <i>about</i> a definition must be answered from the stored
    /// tree, never from the text rendered for humans.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class of defect appeared three times while the text was being removed, and the third was
    /// the dangerous one:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// a column <c>CHECK</c> was skipped because validation asked <c>CheckExpression == null</c>, so
    /// <c>INSERT … VALUES (3, 99)</c> against <c>CHECK (V &lt; 10)</c> was accepted;
    /// </description></item>
    /// <item><description>
    /// <c>IsComputed</c> read the text, so a computed column stopped being computed;
    /// </description></item>
    /// <item><description>
    /// <c>IsFiltered</c> read the text — and <c>OptimizerQuery</c> uses it to decide whether an
    /// index covers a query. A partial index reporting itself as unfiltered is used for a query
    /// that needs every row, and <b>rows go missing from the answer</b>.
    /// </description></item>
    /// </list>
    /// <para>
    /// None of these is visible in a catalog dump; all three are visible here.
    /// </para>
    /// </remarks>
    [Test]
    public void DerivedAnswersDoNotDependOnTheRenderedTextTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT CHECK (V < 10), Age INT)");
        m_engine.Execute("CREATE TABLE U (Id INT PRIMARY KEY, Price INT, Qty INT, Total AS (Price * Qty) STORED)");
        m_engine.Execute("CREATE INDEX IX ON T (Age) WHERE Age > 18");
        m_engine.Execute("CREATE INDEX IX2 ON T ((Age + 1))");

        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 99)"),
                Throws.Exception, "a column CHECK must still be enforced");

            Assert.That(m_engine.GetTable("U")!.GetColumn("Total")!.IsComputed, Is.True,
                "a computed column must still report itself as computed");

            Assert.That(m_engine.GetIndex("IX")!.IsFiltered, Is.True,
                "a partial index must still report itself as filtered - the query optimiser reads " +
                "this to decide whether the index covers a query");

            Assert.That(m_engine.GetIndex("IX2")!.HasExpressions, Is.True,
                "an expression index must still report that it has expressions");
        });
    }

    #endregion

    #region What the catalog still reports

    /// <summary>
    /// Dropping the stored text must not empty the catalog: it is rendered on demand instead.
    /// </summary>
    [Test]
    public void CatalogStillReportsWhatItCanRenderTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, N INT DEFAULT 1, Age INT CHECK (Age >= 0))");
        m_engine.Execute("CREATE VIEW W AS SELECT Id FROM T");
        m_engine.Execute("CREATE INDEX IX ON T (Age) WHERE Age > 18");

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS " +
                               "WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'N'"),
                Is.EqualTo("1"));

            Assert.That(Scalar("SELECT CHECK_EXPRESSION FROM INFORMATION_SCHEMA.COLUMNS " +
                               "WHERE TABLE_NAME = 'T' AND COLUMN_NAME = 'Age'"),
                Does.Contain("Age"));

            Assert.That(Scalar("SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS " +
                               "WHERE TABLE_NAME = 'W'"),
                Does.Contain("SELECT"));

            Assert.That(Scalar("SELECT FILTER_CONDITION FROM INFORMATION_SCHEMA.INDEXES " +
                               "WHERE INDEX_NAME = 'IX'"),
                Does.Contain("Age"));
        });
    }

    #endregion

    #region Helpers

    private static void Text(List<string> populated, string where, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            populated.Add($"{where} = <{value}>");
    }

    private string? Scalar(string sql) =>
        m_engine.Query(sql).Select(row => row[0].IsNull ? null : row[0].AsString()).FirstOrDefault();

    private string? Value(int id) =>
        m_engine.Query($"SELECT N FROM T WHERE Id = {id}")
            .Select(row => row[0].IsNull ? null : row[0].AsInt64().ToString())
            .FirstOrDefault();

    #endregion
}
