namespace OutWit.Database.Tests;

/// <summary>
/// Hexadecimal literals, executed. Every expected value here is SQLite's answer for the same SQL,
/// taken from phase 3's oracle rather than reasoned out.
/// </summary>
/// <remarks>
/// <para>
/// This was recorded in the audit as a <b>parse failure</b>. It was worse than that. <c>SELECT 0x1F</c>
/// did not fail — the lexer split it into the integer <c>0</c> and the identifier <c>x1F</c>, so the
/// statement <b>succeeded and returned 0</b> under the alias <c>x1F</c>. Only <c>Flags &amp; 0x0F</c>
/// failed outright. A silently wrong number is the more dangerous half, and it is why these tests
/// assert values rather than that the statement parsed.
/// </para>
/// <para>
/// The semantics were measured before being implemented, and the measurement contradicted the
/// obvious choice: an oversized <b>decimal</b> literal is widened to <c>DECIMAL</c> here to preserve
/// its value, but an oversized <b>hex</b> literal is not. SQLite reinterprets the 64 bits as signed,
/// so <c>0xFFFFFFFFFFFFFFFF</c> is <c>-1</c>, not 18446744073709551615 — which is the point of
/// writing a bit pattern in hex in the first place.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineHexLiteralTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Flags INT NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, Flags) VALUES (1, 15)");
        m_engine.Execute("INSERT INTO T (Id, Flags) VALUES (2, 16)");
    }

    #endregion

    #region Values

    [TestCase("SELECT 0x0", 0L)]
    [TestCase("SELECT 0x1F", 31L)]
    [TestCase("SELECT 0xff", 255L)]
    [TestCase("SELECT 0XFF", 255L)]
    [TestCase("SELECT 0x0000000000000010", 16L)]
    [TestCase("SELECT 0x10 + 1", 17L)]
    [TestCase("SELECT -0x10", -16L)]
    [TestCase("SELECT 0x7FFFFFFFFFFFFFFF", long.MaxValue)]
    // The one that had to be measured: 64 bits reinterpreted as signed, not widened.
    [TestCase("SELECT 0xFFFFFFFFFFFFFFFF", -1L)]
    public void HexLiteralEvaluatesToSqlitesAnswerTest(string sql, long expected)
    {
        var value = m_engine.Query(sql).Single()[0].AsInt64();

        Assert.That(value, Is.EqualTo(expected));
    }

    #endregion

    #region The bitwise use WitSQL.md documents

    [Test]
    public void HexLiteralWorksInABitwiseFilterTest()
    {
        // WitSQL.md §4.5 uses exactly this shape in its bitwise-operator examples, and it did not
        // parse at all before.
        var ids = m_engine.Query("SELECT Id FROM T WHERE Flags & 0x0F = 15 ORDER BY Id")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(ids, Is.EqualTo(new long[] { 1 }),
            "15 & 15 = 15; 16 & 15 = 0");
    }

    [Test]
    public void HexLiteralWorksAsAStoredValueTest()
    {
        m_engine.Execute("INSERT INTO T (Id, Flags) VALUES (3, 0xFF)");

        var flags = m_engine.Query("SELECT Flags FROM T WHERE Id = 3").Single()[0].AsInt64();

        Assert.That(flags, Is.EqualTo(255));
    }

    #endregion

    #region What must still be refused

    [Test]
    public void HexLiteralWiderThanSixtyFourBitsIsRefusedTest()
    {
        // SQLite raises "hex literal too big" rather than truncating. Truncating silently would be
        // the same class of defect this whole fix is about.
        Assert.That(
            () => m_engine.Query("SELECT 0x1FFFFFFFFFFFFFFFF").ToList(),
            Throws.Exception.With.Message.Contains("too big"));
    }

    [Test]
    public void BareZeroFollowedByAnIdentifierStillAliasesTest()
    {
        // The lexer change must not swallow a genuine alias. `0 x1F` with a space is still an
        // integer aliased x1F, exactly as before.
        var select = m_engine.Query("SELECT 0 x1F").Single();

        Assert.That(select[0].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void IdentifierStartingWithXIsUnaffectedTest()
    {
        m_engine.Execute("CREATE TABLE X1 (x1F INT)");
        m_engine.Execute("INSERT INTO X1 (x1F) VALUES (5)");

        var value = m_engine.Query("SELECT x1F FROM X1").Single()[0].AsInt64();

        Assert.That(value, Is.EqualTo(5), "a column named x1F must keep working");
    }

    #endregion
}
