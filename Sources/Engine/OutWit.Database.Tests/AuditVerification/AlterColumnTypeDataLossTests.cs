using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// <c>ALTER COLUMN … TYPE</c> destroyed the values it could not convert, silently.
///
/// The conversion goes through <c>AsInt64</c> and its neighbours, and those answer a failed parse with
/// a default rather than a failure - <c>long.TryParse(text, out var v) ? v : 0</c>. So a VARCHAR column
/// holding 'not a number' became a column holding 0, the rows were written back, nothing was raised,
/// and changing the type again did not bring anything back. One accepted statement could empty a column
/// of its meaning.
///
/// Found on 2026-08-06 while building Studio's schema designer, which is why that designer refuses to
/// offer a type change as an edit in place. PostgreSql refuses the same statement - "invalid input
/// syntax" - and so does this now.
///
/// The narrowing conversions are NOT refused, and that is deliberate: a decimal read as an integer
/// truncates, which is a defined conversion rather than a value with nothing to become.
/// </summary>
[TestFixture]
public sealed class AlterColumnTypeDataLossTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_alter_type_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;

        if (!Directory.Exists(m_databasePath))
            return;

        try
        {
            Directory.Delete(m_databasePath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only - a locked file must not fail the run.
        }
    }

    #endregion

    #region Tests

    [Test]
    public void AValueThatIsNotANumberIsNotTurnedIntoZeroTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, '42')");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 'not a number')");

        var error = Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));

        Assert.That(error!.Message, Does.Contain("not a number"), "the value that stopped it is named");

        Assert.That(Values(), Is.EqualTo(new[] { "1=42", "2=not a number" }),
            "and nothing was changed - the refusal comes before anything is written");
    }

    [Test]
    public void ADecimalStringIsNotSilentlyTruncatedToZeroTest()
    {
        // '3.9' does not read as an integer, and answering 0 for it is the same defect wearing a
        // different value.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, '3.9')");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));

        Assert.That(Values(), Is.EqualTo(new[] { "1=3.9" }));
    }

    [Test]
    public void AConvertibleColumnIsStillConvertedTest()
    {
        // CONTROL: the refusal is about the values, not about the statement. Every value here reads
        // as an integer, so the change goes through.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, '42')");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, '7')");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));

        Assert.That(Values(), Is.EqualTo(new[] { "1=42", "2=7" }));
    }

    [Test]
    public void AnEmptyColumnConvertsToAnythingTest()
    {
        // CONTROL: with no values there is nothing that could fail to read.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));
    }

    [Test]
    public void NullsDoNotStopTheConversionTest()
    {
        // CONTROL: NULL has nothing to convert and stays NULL.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, '42')");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, NULL)");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));

        Assert.That(m_engine.Query("SELECT V FROM T WHERE Id = 2")[0]["V"].IsNull, Is.True);
    }

    [Test]
    public void ANarrowingNumericConversionIsStillAllowedTest()
    {
        // CONTROL, and the boundary of the rule: a decimal read as an integer truncates. That is a
        // defined conversion, and refusing it would be this fix going too far.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V DECIMAL(18,2))");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 12.75)");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE INT"));

        Assert.That(m_engine.Query("SELECT V FROM T")[0]["V"].AsInt64(), Is.EqualTo(12));
    }

    [Test]
    public void ATextThatIsNotADateIsRefusedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V TEXT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 'yesterday')");

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE DATETIME"),
            "an unreadable date used to become 01/01/0001");

        Assert.That(Values(), Is.EqualTo(new[] { "1=yesterday" }));
    }

    [Test]
    public void AnythingCanStillBecomeTextTest()
    {
        // CONTROL: widening to text loses nothing and must not be refused.
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, V INT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 42)");

        Assert.DoesNotThrow(() => m_engine.Execute("ALTER TABLE T ALTER COLUMN V TYPE VARCHAR(50)"));

        Assert.That(Values(), Is.EqualTo(new[] { "1=42" }));
    }

    #endregion

    #region Helpers

    private string[] Values()
    {
        return m_engine.Query("SELECT Id, V FROM T")
            .Select(row => $"{row["Id"].AsInt64()}={(row["V"].IsNull ? "NULL" : row["V"].AsString())}")
            .OrderBy(text => text)
            .ToArray();
    }

    #endregion
}
