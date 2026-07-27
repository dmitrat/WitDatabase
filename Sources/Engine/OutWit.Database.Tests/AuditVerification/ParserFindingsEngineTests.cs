namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// The execution half of the <c>parser</c> findings - the claims that are about what a parsed
/// statement does, not about whether it parses.
/// </summary>
/// <remarks>
/// The parse-level half lives in
/// <c>OutWit.Database.Parser.Tests/AuditVerification/ParserFindingsTests</c>.
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class ParserFindingsEngineTests : WitSqlEngineTestsBase
{
    #region SIGNAL in a trigger body

    [Test]
    [Ignore("CONFIRMED 2026-07-27: raises NotSupportedException \"Statement serialization not " +
            "supported: WitSqlStatementSignal\". Note where it fails - SIGNAL parses fine, and the " +
            "break is in statement *serialization*, not execution. parser, " +
            "Parser/Grammars/WitSqlParser.g4:80")]
    public void SignalInATriggerBodyRejectsTheRowTest()
    {
        // Finding: WitSqlParser.g4:80 - the second half of the "documented trigger bodies are
        // unusable" claim. SIGNAL parses, so the defect is not in the grammar; the claim is that
        // executing it throws NotSupportedException. Rejecting a row is the entire purpose of a
        // SIGNAL in a BEFORE trigger, and WitSQL.md documents it.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Amount INT)");
        m_engine.Execute(@"
            CREATE TRIGGER T_Guard
            BEFORE INSERT ON T
            FOR EACH ROW
            BEGIN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'amount must be positive';
            END");

        Assert.That(
            () => m_engine.Execute("INSERT INTO T (Id, Amount) VALUES (1, -5)"),
            Throws.Exception.With.Message.Contains("amount must be positive"),
            "SIGNAL must surface as the error it declares, not as an unsupported-feature failure");
    }

    #endregion

    #region MySQL-style LIMIT offset, count, executed

    [Test]
    public void MySqlStyleLimitReturnsTheRightRowsTest()
    {
        // The user-visible consequence of the reversed operands: `LIMIT 10, 5` must skip 10 rows and
        // return 5. Reversed, it returns rows 6..15 instead of 11..15.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
        for (int i = 1; i <= 20; i++)
            m_engine.Execute($"INSERT INTO T (Id) VALUES ({i})");

        var ids = m_engine.Query("SELECT Id FROM T ORDER BY Id LIMIT 10, 5")
            .Select(r => r[0].AsInt64()).ToArray();

        Assert.That(ids, Is.EqualTo(new long[] { 11, 12, 13, 14, 15 }),
            "LIMIT 10, 5 means skip 10 then take 5");
    }

    #endregion
}
