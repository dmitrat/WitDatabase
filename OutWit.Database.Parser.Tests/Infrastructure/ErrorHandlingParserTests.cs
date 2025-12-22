using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Infrastructure;

/// <summary>
/// Tests for error handling in SQL parsing.
/// Covers: syntax errors, invalid tokens, missing elements, error recovery.
/// </summary>
[TestFixture]
public class ErrorHandlingParserTests
{
    #region Basic Syntax Errors

    [Test]
    public void InvalidSyntaxThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.Parse("SELECT FROM"));
    }

    [Test]
    public void EmptyInputThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.ParseStatement(""));
    }

    [Test]
    public void WhitespaceOnlyInputThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.ParseStatement("   \t\n  "));
    }

    [Test]
    public void NullInputThrowsTest()
    {
        Assert.That(() => WitSql.Parse(null!), 
            Throws.TypeOf<ArgumentNullException>().Or.TypeOf<WitSqlParsingException>());
    }

    #endregion

    #region TryParse Methods

    [Test]
    public void TryParseReturnsFalseForInvalidSqlTest()
    {
        var result = WitSql.TryParse("SELECT * FORM Users"); // typo: FORM instead of FROM
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void TryParseSucceedsForValidSqlTest()
    {
        var result = WitSql.TryParse("SELECT * FROM Users");
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Statements, Has.Count.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void TryParseReturnsErrorDetailsTest()
    {
        var result = WitSql.TryParse("SELECT * FROM WHERE");
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);

        var firstError = result.Errors.First();
        Assert.That(firstError.Line, Is.GreaterThan(0));
        Assert.That(firstError.Message, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TryParseMultipleErrorsTest()
    {
        var result = WitSql.TryParse("SELECT FROM WHERE ORDER");
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }

    #endregion

    #region Missing Elements

    [Test]
    public void MissingFromKeywordThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * Users WHERE Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MissingTableNameThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM WHERE Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MissingColumnInInsertThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("INSERT INTO Users () VALUES (1)"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MissingValuesInInsertThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("INSERT INTO Users (Id) VALUES"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MissingSetInUpdateThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("UPDATE Users Name = 'John'"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Unbalanced Elements

    [Test]
    public void UnbalancedParenthesesThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE (Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ExtraClosingParenthesisThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE Id = 1)"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void UnterminatedStringThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE Name = 'John"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Unexpected Tokens

    [Test]
    public void UnexpectedTokenThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE WHERE Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void DuplicateKeywordsThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT SELECT * FROM Users"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void InvalidOperatorThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE Id === 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Invalid DDL

    [Test]
    public void InvalidCreateTableSyntaxThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("CREATE TABLE ()"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void CreateTableWithoutColumnsThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("CREATE TABLE Users"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void InvalidDataTypeThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("CREATE TABLE T (Col INVALIDTYPE)"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void CreateIndexWithoutTableThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("CREATE INDEX IX_Test (Col)"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Multiple Statements Errors

    [Test]
    public void MultipleStatementsWhenSingleExpectedThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseStatement("SELECT 1; SELECT 2"));
        Assert.That(ex!.Message, Does.Contain("single statement"));
    }

    [Test]
    public void ErrorInSecondStatementTest()
    {
        var result = WitSql.TryParse("SELECT 1; SELECT FROM");
        Assert.That(result.IsSuccess, Is.False);
    }

    #endregion

    #region Expression Parsing Errors

    [Test]
    public void InvalidExpressionThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseExpression("+ -"));
    }

    [Test]
    public void EmptyExpressionThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseExpression(""));
    }

    [Test]
    public void IncompleteExpressionThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseExpression("1 +"));
    }

    [Test]
    public void MissingFunctionArgumentsThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseExpression("SUBSTRING("));
    }

    #endregion

    #region Error Message Quality

    [Test]
    public void ErrorMessageContainsLineNumberTest()
    {
        var result = WitSql.TryParse("SELECT\n*\nFROM\nWHERE");
        Assert.That(result.IsSuccess, Is.False);
        var error = result.Errors.First();
        Assert.That(error.Line, Is.GreaterThan(0));
    }

    [Test]
    public void ErrorMessageContainsColumnNumberTest()
    {
        var result = WitSql.TryParse("SELECT * FROM WHERE");
        Assert.That(result.IsSuccess, Is.False);
        var error = result.Errors.First();
        Assert.That(error.Column, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ErrorMessageIsDescriptiveTest()
    {
        var result = WitSql.TryParse("SELECT * FROM");
        Assert.That(result.IsSuccess, Is.False);
        var error = result.Errors.First();
        Assert.That(error.Message, Is.Not.Null.And.Not.Empty);
    }

    #endregion
}
