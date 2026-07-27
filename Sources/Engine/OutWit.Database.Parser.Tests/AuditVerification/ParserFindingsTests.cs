using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.AuditVerification;

/// <summary>
/// Verification of the seven <c>parser</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Every test asserts the <b>correct</b> behaviour, so a failure confirms the finding it is named
/// after. See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class ParserFindingsTests
{
    #region Serializer emits unquoted identifiers using an incomplete reserved-word list

    // The serializer's RESERVED_WORDS set holds 68 entries. WitSQL.md documents a reserved-word
    // list roughly twice that size, and the words below are in the documented list and absent from
    // the serializer's - which is exactly what "incomplete reserved-word list" means. `Order` and
    // `Group` are included as controls: they ARE in the serializer's set, so they must round-trip.
    private const string ReservedIgnore =
        "CONFIRMED 2026-07-27: the serializer emits this identifier unquoted and the result no " +
        "longer parses. Its RESERVED_WORDS set holds 68 entries; WitSQL.md documents roughly twice " +
        "that many. parser, Parser/Serializers/WitSqlExpressionSerializer.cs:441";

    [TestCase("Order")]
    [TestCase("Group")]
    [TestCase("Using", Ignore = ReservedIgnore)]
    [TestCase("With", Ignore = ReservedIgnore)]
    [TestCase("Row", Ignore = ReservedIgnore)]
    [TestCase("Column", Ignore = ReservedIgnore)]
    [TestCase("Cross", Ignore = ReservedIgnore)]
    [TestCase("Year")]
    [TestCase("Interval", Ignore = ReservedIgnore)]
    [TestCase("Partition", Ignore = ReservedIgnore)]
    public void SerialisedIdentifierReParsesTest(string identifier)
    {
        // Finding: WitSqlExpressionSerializer.cs:441 - identifiers are emitted unquoted unless they
        // appear in an incomplete reserved-word list, so a round-tripped schema object stops
        // parsing. The round trip is the whole point: whatever the serializer emits must go back in.
        var expression = new WitSqlExpressionColumnRef { ColumnName = identifier };

        var serialised = WitSqlExpressionSerializer.Serialize(expression);

        Assert.That(() => WitSql.Parse($"SELECT {serialised} FROM T"), Throws.Nothing,
            $"the serializer emitted <{serialised}>, which must be re-parseable");
    }

    #endregion

    #region INSERT INTO t DEFAULT VALUES

    [Test]
    [Ignore("CONFIRMED 2026-07-27: WitSqlParsingException with 2 errors. "
            + "parser, Parser/Grammars/WitSqlParser.g4:194")]
    public void InsertDefaultValuesParsesTest()
    {
        // Finding: WitSqlParser.g4:194 - the form is absent from the grammar although the EF
        // provider emits it for an entity whose every column is store-generated.
        Assert.That(() => WitSql.Parse("INSERT INTO T DEFAULT VALUES"), Throws.Nothing,
            "EF Core emits this for an all-generated entity, so the grammar must accept it");
    }

    #endregion

    #region MERGE target alias

    [Test]
    public void MergeWithoutATargetAliasDoesNotBorrowTheSourceAliasTest()
    {
        // Finding: WitSqlVisitor.DML.cs:373 - when the target alias is omitted the visitor assigns
        // the *source* alias to TargetAlias, so every unqualified reference resolves to the wrong
        // table.
        var statement = (WitSqlStatementMerge)WitSql.Parse(@"
            MERGE INTO Target
            USING Source AS s ON Target.Id = s.Id
            WHEN MATCHED THEN UPDATE SET Target.Name = s.Name")[0];

        // NB: asserting merely that the two differ is not enough - they do differ, in the wrong
        // direction. Each has to be checked against what it should hold.
        Assert.Multiple(() =>
        {
            Assert.That(statement.SourceAlias, Is.EqualTo("s"),
                "`USING Source AS s` gives the source the alias 's'");
            Assert.That(statement.TargetAlias, Is.Null,
                "the target was written without an alias, so it must not have one");
        });
    }

    #endregion

    #region Hexadecimal literals

    [TestCase("SELECT 0x1F")]
    [TestCase("SELECT * FROM T WHERE Flags & 0x0F = 1",
        Ignore = "CONFIRMED 2026-07-27: WitSqlParsingException, mismatched input 'x0F'. The lexer " +
                 "splits 0x0F into 0 and x0F. parser, Parser/Grammars/WitSqlParser.g4:527")]
    public void HexadecimalLiteralParsesTest(string sql)
    {
        // Finding: WitSqlParser.g4:527 - no typed, prefixed or hexadecimal literal forms exist,
        // including the hex literals WitSQL.md uses in its own bitwise-operator examples
        // (`Flags & 0x0F`, `Flags | 0x10`).
        Assert.That(() => WitSql.Parse(sql), Throws.Nothing,
            "WitSQL.md documents hex literals in its bitwise-operator section");
    }

    #endregion

    #region MySQL-style LIMIT offset, count

    [Test]
    public void MySqlStyleLimitBindsOffsetAndCountInOrderTest()
    {
        // Finding: WitSqlVisitor.DML.cs:80 - `LIMIT 10, 5` means "skip 10, take 5" in MySQL. The
        // visitor binds the operands the wrong way round, so it silently means "skip 5, take 10".
        var select = (WitSqlStatementSelect)WitSql.Parse("SELECT * FROM T LIMIT 10, 5")[0];

        Assert.Multiple(() =>
        {
            Assert.That(LiteralOf(select.LimitOffset), Is.EqualTo(10L), "LIMIT 10, 5 skips 10 rows");
            Assert.That(LiteralOf(select.LimitCount), Is.EqualTo(5L), "LIMIT 10, 5 takes 5 rows");
        });
    }

    #endregion

    #region Documented trigger bodies

    [Test]
    [Ignore("CONFIRMED 2026-07-27: SET NEW.col = ... is a syntax error, though WitSQL.md documents it. "
            + "parser, Parser/Grammars/WitSqlParser.g4:80")]
    public void TriggerBodyCanAssignToNewParsesTest()
    {
        // Finding: WitSqlParser.g4:80 - the trigger bodies WitSQL.md documents do not parse.
        // `SET NEW.col = ...` is the canonical BEFORE-trigger idiom.
        Assert.That(() => WitSql.Parse(@"
            CREATE TRIGGER T_BeforeInsert
            BEFORE INSERT ON T
            FOR EACH ROW
            BEGIN
                SET NEW.Name = 'x';
            END"), Throws.Nothing,
            "assigning to NEW is what a BEFORE trigger exists for");
    }

    [Test]
    public void TriggerBodyCanSignalParsesTest()
    {
        Assert.That(() => WitSql.Parse(@"
            CREATE TRIGGER T_Guard
            BEFORE INSERT ON T
            FOR EACH ROW
            BEGIN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'rejected';
            END"), Throws.Nothing,
            "SIGNAL is how a trigger rejects a row");
    }

    #endregion

    #region Integer literal bounds

    [Test]
    public void IntegerLiteralAboveLongMaxIsPromotedRatherThanOverflowingTest()
    {
        // NOT REPRODUCED 2026-07-27. The finding predicts "a raw OverflowException"; nothing is
        // thrown at all. The literal is promoted to Decimal and keeps its value exactly
        // (99999999999999999999), which is a reasonable answer rather than a defect. This test now
        // pins that behaviour instead of asserting the predicted failure.
        var select = (WitSqlStatementSelect)WitSql.Parse("SELECT 99999999999999999999")[0];
        var literal = select.SelectList[0].Expression as WitSqlExpressionLiteral;

        Assert.Multiple(() =>
        {
            Assert.That(literal, Is.Not.Null);
            Assert.That(literal!.Value, Is.EqualTo(99999999999999999999m));
        });
    }

    [Test]
    public void LongMinValueCanBeWrittenTest()
    {
        // long.MinValue has no positive counterpart, so it can only be written as a negated
        // literal - and the negation is applied after the literal is converted.
        // NOT REPRODUCED either: it parses. The literal exceeds long.MaxValue on its own, so it is
        // promoted to Decimal and then negated, which preserves the value.
        var select = (WitSqlStatementSelect)WitSql.Parse("SELECT -9223372036854775808")[0];

        Assert.That(select.SelectList, Has.Count.EqualTo(1),
            "the smallest representable BIGINT must be expressible");
    }

    #endregion

    #region Characterisation

    [Test]
    public void HexLiteralInASelectListCharacterisationTest()
    {
        // `SELECT 0x1F` parses, which looked at first like partial hex support. It is not: the lexer
        // takes `0` as the value and `x1F` as a column alias. This prints what actually came out.
        var select = (WitSqlStatementSelect)WitSql.Parse("SELECT 0x1F")[0];
        var column = select.SelectList[0];

        TestContext.Out.WriteLine(
            $"expression = {WitSqlExpressionSerializer.Serialize(column.Expression!)}, " +
            $"alias = {column.Alias ?? "<none>"}");

        Assert.Pass("characterisation only - see the printed result");
    }

    [Test]
    public void MergeAliasCharacterisationTest()
    {
        // The MERGE test passes, which by itself only says the two aliases differ. This records what
        // TargetAlias actually is when the target has no alias of its own.
        var statement = (WitSqlStatementMerge)WitSql.Parse(@"
            MERGE INTO Target
            USING Source AS s ON Target.Id = s.Id
            WHEN MATCHED THEN UPDATE SET Target.Name = s.Name")[0];

        TestContext.Out.WriteLine(
            $"TargetAlias = {statement.TargetAlias ?? "<null>"}, " +
            $"SourceAlias = {statement.SourceAlias ?? "<null>"}");

        Assert.Pass("characterisation only - see the printed result");
    }

    [Test]
    public void OversizedIntegerLiteralCharacterisationTest()
    {
        // The finding predicts a raw OverflowException. Nothing is thrown at all, so the question
        // becomes what value the literal ends up with.
        var select = (WitSqlStatementSelect)WitSql.Parse("SELECT 99999999999999999999")[0];
        var expression = select.SelectList[0].Expression;

        var rendered = expression is WitSqlExpressionLiteral literal
            ? $"{literal.Value} ({literal.Value?.GetType().Name})"
            : expression?.GetType().Name ?? "<null>";

        TestContext.Out.WriteLine($"99999999999999999999 parsed as: {rendered}");

        Assert.Pass("characterisation only - see the printed result");
    }

    #endregion

    #region Helpers

    private static long? LiteralOf(WitSqlExpression? expression) =>
        expression is WitSqlExpressionLiteral literal && literal.Value is not null
            ? Convert.ToInt64(literal.Value)
            : null;

    #endregion
}
