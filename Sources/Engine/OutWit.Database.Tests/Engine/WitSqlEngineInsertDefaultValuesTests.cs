namespace OutWit.Database.Tests;

/// <summary>
/// <c>INSERT … DEFAULT VALUES</c>, executed end to end.
/// </summary>
/// <remarks>
/// <para>
/// EF Core emits this form for an entity whose columns are all store-generated, and SQLite accepts
/// it — measured in phase 3's oracle sweep, where it was one of only three shapes SQLite accepts and
/// WitDatabase rejected. Parsing it is necessary but not sufficient: the point is that the row lands
/// with its defaults, so every test here checks stored values rather than that the statement parsed.
/// </para>
/// <para>
/// The executor needed no change. <c>BuildInsertRowWithAutoGenInfo</c> already seeds every column
/// with its default, auto-increment value or <c>ROWVERSION</c> before applying supplied values, and
/// its positional loop is bounded by the value count — so a single empty value row means "apply
/// nothing on top of the defaults", which is exactly this feature.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineInsertDefaultValuesTests : WitSqlEngineTestsBase
{
    #region An all-generated table - the EF Core case

    [Test]
    public void AllGeneratedTableAcceptsDefaultValuesTest()
    {
        m_engine.Execute("CREATE TABLE G (Id BIGINT PRIMARY KEY AUTOINCREMENT)");

        m_engine.Execute("INSERT INTO G DEFAULT VALUES");
        m_engine.Execute("INSERT INTO G DEFAULT VALUES");

        var ids = m_engine.Query("SELECT Id FROM G ORDER BY Id")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(ids, Is.EqualTo(new long[] { 1, 2 }),
            "each statement must insert one row and advance the auto-increment counter");
    }

    #endregion

    #region Column defaults are applied

    [Test]
    public void DeclaredDefaultsAreAppliedTest()
    {
        m_engine.Execute(@"
            CREATE TABLE D (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(50) DEFAULT 'unnamed',
                Count INT DEFAULT 7,
                Optional INT
            )");

        m_engine.Execute("INSERT INTO D DEFAULT VALUES");

        var row = m_engine.Query("SELECT Name, Count, Optional FROM D").Single();

        Assert.Multiple(() =>
        {
            Assert.That(row[0].AsString(), Is.EqualTo("unnamed"), "the declared string default");
            Assert.That(row[1].AsInt64(), Is.EqualTo(7), "the declared integer default");
            Assert.That(row[2].IsNull, Is.True, "a column with no default stays NULL");
        });
    }

    [Test]
    public void ExpressionDefaultIsEvaluatedTest()
    {
        m_engine.Execute(@"
            CREATE TABLE E (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Total INT DEFAULT (2 + 3)
            )");

        m_engine.Execute("INSERT INTO E DEFAULT VALUES");

        var total = m_engine.Query("SELECT Total FROM E").Single()[0].AsInt64();

        Assert.That(total, Is.EqualTo(5), "a parenthesised expression default must be evaluated");
    }

    #endregion

    #region What it must still refuse

    [Test]
    public void NotNullColumnWithoutADefaultStillRefusesTest()
    {
        // The feature must not become a way past NOT NULL. SQLite refuses this too.
        //
        // The message is asserted, not merely that something was thrown. Run against the unfixed
        // code this test PASSED - because the statement failed to parse, which is a different
        // failure that happens to look the same from a bare Throws.Exception. Checking for the
        // constraint message is what makes it evidence about NOT NULL rather than about the grammar.
        m_engine.Execute(@"
            CREATE TABLE R (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Required VARCHAR(50) NOT NULL
            )");

        Assert.That(
            () => m_engine.Execute("INSERT INTO R DEFAULT VALUES"),
            Throws.Exception.With.Message.Contains("NOT NULL"),
            "Required has no default and cannot be NULL, so the row must be rejected - and rejected " +
            "by the constraint, not by the parser");
    }

    #endregion

    #region Composition with the rest of INSERT

    [Test]
    public void DefaultValuesWorksWithReturningTest()
    {
        m_engine.Execute("CREATE TABLE G (Id BIGINT PRIMARY KEY AUTOINCREMENT)");

        var returned = m_engine.Query("INSERT INTO G DEFAULT VALUES RETURNING Id")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(returned, Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public void DefaultValuesReportsOneRowAffectedTest()
    {
        m_engine.Execute("CREATE TABLE G (Id BIGINT PRIMARY KEY AUTOINCREMENT)");

        m_engine.Execute("INSERT INTO G DEFAULT VALUES");

        var count = m_engine.Query("SELECT COUNT(*) FROM G").Single()[0].AsInt64();

        Assert.That(count, Is.EqualTo(1), "exactly one row, not zero and not two");
    }

    #endregion
}
