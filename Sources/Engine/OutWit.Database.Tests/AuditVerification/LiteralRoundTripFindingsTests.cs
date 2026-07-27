namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the engine-side <c>literal-roundtrip</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The EF-side entries of this dimension are in
/// <c>OutWit.Database.EntityFramework.Tests/AuditVerification/LiteralRoundTripEfTests</c>.
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class LiteralRoundTripFindingsTests : WitSqlEngineTestsBase
{
    // ALREADY FIXED - not merely "not reproduced". Both tests below pass, and the reason is in the
    // history rather than in the claim: commit 9556bd2, "fix(parser): numeric literals are exact,
    // and out-of-range integers no longer throw from the parser" (2026-07-26), is part of the 2.0.0
    // merge. ParseNumericLiteral now parses a non-exponent literal as decimal first and falls back
    // to double only for exponent form or a magnitude decimal cannot hold - the opposite of what the
    // finding describes. The audit's finding list was written against the pre-fix code and never
    // updated. The same commit also settles the `parser` dimension's integer-literal entry.
    //
    // These tests stay active as the regression pins for that fix.

    #region REAL_LITERAL is parsed as double

    [Test]
    public void HighPrecisionDecimalLiteralKeepsItsDigitsTest()
    {
        // Finding: WitSqlVisitor.Expressions.cs:284 - every REAL_LITERAL is parsed as double, so a
        // literal with more significant digits than a double can hold is silently rounded on its
        // way into a DECIMAL column. double carries ~15-17 significant digits; decimal carries 28-29.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(28, 20))");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 0.12345678901234567890)");

        var stored = m_engine.Query("SELECT V FROM T WHERE Id = 1")[0][0].AsDecimal();

        Assert.That(stored, Is.EqualTo(0.12345678901234567890m),
            "a decimal literal must reach a DECIMAL column with all its digits");
    }

    [Test]
    public void DecimalLiteralComparisonMatchesTheStoredValueTest()
    {
        // The consequence the finding names: `=` matches the wrong rows. The two values below differ
        // only past the 17th significant digit, which is exactly where a double stops being able to
        // tell them apart.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V DECIMAL(28, 20))");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 0.10000000000000000001)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 0.10000000000000000002)");

        var ids = m_engine.Query("SELECT Id FROM T WHERE V = 0.10000000000000000001 ORDER BY Id")
            .Select(r => r[0].AsInt64()).ToArray();

        Assert.That(ids, Is.EqualTo(new long[] { 1 }),
            "the literal must match exactly the row that holds it, not its neighbour as well");
    }

    #endregion
}
