using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.Storage;

/// <summary>
/// Tests for WitSqlGenerationHelper.
/// </summary>
[TestFixture]
public class WitSqlGenerationHelperTests
{
    #region Fields

    private WitSqlGenerationHelper m_helper = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        // Create the helper with minimal dependencies
        m_helper = new WitSqlGenerationHelper(
            new RelationalSqlGenerationHelperDependencies());
    }

    #endregion

    #region DelimitIdentifier Tests

    [Test]
    public void DelimitIdentifierWrapsWithDoubleQuotesTest()
    {
        var result = m_helper.DelimitIdentifier("TableName");

        Assert.That(result, Is.EqualTo("\"TableName\""));
    }

    [Test]
    public void DelimitIdentifierEscapesDoubleQuotesTest()
    {
        var result = m_helper.DelimitIdentifier("Table\"Name");

        Assert.That(result, Is.EqualTo("\"Table\"\"Name\""));
    }

    [Test]
    public void DelimitIdentifierHandlesEmptyStringTest()
    {
        var result = m_helper.DelimitIdentifier("");

        Assert.That(result, Is.EqualTo("\"\""));
    }

    [Test]
    public void DelimitIdentifierWithBuilderTest()
    {
        var builder = new StringBuilder();

        m_helper.DelimitIdentifier(builder, "ColumnName");

        Assert.That(builder.ToString(), Is.EqualTo("\"ColumnName\""));
    }

    [Test]
    public void DelimitIdentifierWithBuilderEscapesQuotesTest()
    {
        var builder = new StringBuilder();

        m_helper.DelimitIdentifier(builder, "Column\"Name");

        Assert.That(builder.ToString(), Is.EqualTo("\"Column\"\"Name\""));
    }

    #endregion

    #region EscapeIdentifier Tests

    [Test]
    public void EscapeIdentifierDoublesDoubleQuotesTest()
    {
        var result = m_helper.EscapeIdentifier("Table\"Name");

        Assert.That(result, Is.EqualTo("Table\"\"Name"));
    }

    [Test]
    public void EscapeIdentifierNoEscapeWhenNoQuotesTest()
    {
        var result = m_helper.EscapeIdentifier("TableName");

        Assert.That(result, Is.EqualTo("TableName"));
    }

    [Test]
    public void EscapeIdentifierHandlesMultipleQuotesTest()
    {
        var result = m_helper.EscapeIdentifier("\"Table\"\"Name\"");

        Assert.That(result, Is.EqualTo("\"\"Table\"\"\"\"Name\"\""));
    }

    [Test]
    public void EscapeIdentifierWithBuilderTest()
    {
        var builder = new StringBuilder();

        m_helper.EscapeIdentifier(builder, "Table\"Name");

        Assert.That(builder.ToString(), Is.EqualTo("Table\"\"Name"));
    }

    #endregion

    #region GenerateParameterName Tests

    [Test]
    public void GenerateParameterNameAddsAtSignTest()
    {
        var result = m_helper.GenerateParameterName("param1");

        Assert.That(result, Is.EqualTo("@param1"));
    }

    [Test]
    public void GenerateParameterNamePreservesExistingAtSignTest()
    {
        var result = m_helper.GenerateParameterName("@param1");

        Assert.That(result, Is.EqualTo("@param1"));
    }

    [Test]
    public void GenerateParameterNameWithBuilderTest()
    {
        var builder = new StringBuilder();

        m_helper.GenerateParameterName(builder, "param1");

        Assert.That(builder.ToString(), Is.EqualTo("@param1"));
    }

    [Test]
    public void GenerateParameterNameWithBuilderPreservesAtSignTest()
    {
        var builder = new StringBuilder();

        m_helper.GenerateParameterName(builder, "@param1");

        Assert.That(builder.ToString(), Is.EqualTo("@param1"));
    }

    #endregion

    #region GenerateParameterNamePlaceholder Tests

    [Test]
    public void GenerateParameterNamePlaceholderMatchesParameterNameTest()
    {
        var paramName = m_helper.GenerateParameterName("p0");
        var placeholder = m_helper.GenerateParameterNamePlaceholder("p0");

        Assert.That(placeholder, Is.EqualTo(paramName));
    }

    [Test]
    public void GenerateParameterNamePlaceholderWithBuilderTest()
    {
        var builder = new StringBuilder();

        m_helper.GenerateParameterNamePlaceholder(builder, "p0");

        Assert.That(builder.ToString(), Is.EqualTo("@p0"));
    }

    #endregion

    #region StatementTerminator Tests

    [Test]
    public void StatementTerminatorIsSemicolonTest()
    {
        Assert.That(m_helper.StatementTerminator, Is.EqualTo(";"));
    }

    #endregion

    #region BatchTerminator Tests

    [Test]
    public void BatchTerminatorIsEmptyTest()
    {
        Assert.That(m_helper.BatchTerminator, Is.EqualTo(string.Empty));
    }

    #endregion
}
