using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for numeric literal fidelity end to end.
/// </summary>
/// <remarks>
/// Every real literal used to be parsed as a <see cref="double"/>, so an exact value inlined into
/// SQL - which is what EF Core does for a constant it does not parameterise - silently changed on the
/// way into a DECIMAL column. Integer literals used <c>long.Parse</c>, which threw a raw
/// <see cref="OverflowException"/> out of the parser above <see cref="long.MaxValue"/>.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineNumericLiteralTests : WitSqlEngineTestsBase
{
    #region Decimal Precision

    [Test]
    public void DecimalLiteralKeepsFullPrecisionThroughInsertAndSelectTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Amount DECIMAL(28,10) NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Amount) VALUES (1, 12345678901234.5678)");

        var amount = Single("SELECT Amount FROM T WHERE Id = 1");

        Assert.That(amount.AsDecimal(), Is.EqualTo(12345678901234.5678m),
            "A double round trip would return 12345678901234.6");
    }

    [Test]
    public void MoneyValuesSumExactlyTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Amount DECIMAL(18,2) NOT NULL)");
        for (var i = 1; i <= 3; i++)
            m_engine.Execute($"INSERT INTO T (Id, Amount) VALUES ({i}, 0.10)");

        var total = Single("SELECT SUM(Amount) FROM T");

        Assert.That(total.AsDecimal(), Is.EqualTo(0.30m),
            "0.10 + 0.10 + 0.10 must be exactly 0.30, not 0.30000000000000004");
    }

    [Test]
    public void DecimalEqualityMatchesTheInsertedLiteralTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Amount DECIMAL(18,4) NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Amount) VALUES (1, 1234.5678)");

        var matched = Single("SELECT COUNT(*) FROM T WHERE Amount = 1234.5678");

        Assert.That(matched.AsInt64(), Is.EqualTo(1));
    }

    #endregion

    #region Approximate Literals

    [Test]
    public void ExponentLiteralStaysApproximateTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DOUBLE NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 1.5e10)");

        Assert.That(Single("SELECT V FROM T WHERE Id = 1").AsDouble(), Is.EqualTo(1.5e10));
    }

    [Test]
    public void VeryLargeMagnitudeFallsBackToApproximateTest()
    {
        // Beyond decimal's range, so the parser must fall back to double rather than throwing.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DOUBLE NOT NULL)");

        Assert.DoesNotThrow(() => m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 1e300)"));
    }

    #endregion

    #region Integer Range

    [Test]
    public void LongMinValueLiteralRoundTripsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V BIGINT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, -9223372036854775808)");

        Assert.That(Single("SELECT V FROM T WHERE Id = 1").AsInt64(), Is.EqualTo(long.MinValue));
    }

    [Test]
    public void LongMaxValueLiteralRoundTripsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V BIGINT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 9223372036854775807)");

        Assert.That(Single("SELECT V FROM T WHERE Id = 1").AsInt64(), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void IntegerLiteralAboveLongMaxDoesNotThrowOverflowTest()
    {
        // ulong.MaxValue: UBIGINT declares room for it, and the parser must not throw a raw
        // OverflowException from long.Parse.
        Assert.DoesNotThrow(() => m_engine.Query("SELECT 18446744073709551615"));
    }

    [Test]
    public void IntegerLiteralBeyondDecimalRangeReportsAParseErrorTest()
    {
        var tooBig = new string('9', 40);

        Assert.Throws<Parser.Exceptions.WitSqlParsingException>(
            () => m_engine.Query($"SELECT {tooBig}"),
            "An out-of-range literal must be reported as a parse error, not an OverflowException");
    }

    #endregion

    #region Helper Methods

    private WitSqlValue Single(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0]).First();
    }

    #endregion
}
