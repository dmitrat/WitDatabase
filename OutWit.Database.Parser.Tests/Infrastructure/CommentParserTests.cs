using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Infrastructure;

/// <summary>
/// Tests for SQL comment parsing (SS10).
/// Covers: single-line comments (--), multi-line comments (/* ... */).
/// </summary>
[TestFixture]
public class CommentParserTests
{
    #region Single-Line Comments (SS10)

    [Test]
    public void ParseWithLineCommentAtEndTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users -- get all users");
        Assert.That(stmt, Is.Not.Null);
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseWithLineCommentOnOwnLineTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- This is a comment
            SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithMultipleLineCommentsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- Comment 1
            -- Comment 2
            SELECT * FROM Users -- Comment 3
            -- Comment 4");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithCommentBetweenKeywordsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT -- select columns
            * 
            FROM -- from table
            Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseCommentOnlyLinesIgnoredTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- This entire line is a comment
            -- And so is this one
            SELECT 1
            -- Final comment");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseDashDashInStringNotCommentTest()
    {
        var stmt = WitSql.ParseStatement("SELECT 'text--not-a-comment' FROM dual");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select, Is.Not.Null);
    }

    #endregion

    #region Multi-Line Comments (SS10)

    [Test]
    public void ParseWithBlockCommentTest()
    {
        var stmt = WitSql.ParseStatement("SELECT /* columns */ * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithMultiLineBlockCommentTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                /* 
                 * This is a multi-line comment
                 * explaining the query
                 */
                * 
            FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithMultipleBlockCommentsTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT /* comment1 */ * /* comment2 */ FROM /* comment3 */ Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseBlockCommentAtStartTest()
    {
        var stmt = WitSql.ParseStatement("/* Header comment */ SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseBlockCommentAtEndTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users /* Footer comment */");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseBlockCommentSpanningMultipleLinesTest()
    {
        var stmt = WitSql.ParseStatement(@"
            /* 
            This query selects all users
            from the Users table
            */
            SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseEmptyBlockCommentTest()
    {
        var stmt = WitSql.ParseStatement("SELECT /**/ * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseBlockCommentWithAsterisksTest()
    {
        var stmt = WitSql.ParseStatement(@"
            /************************************
             * Important query - do not modify! *
             ************************************/
            SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    #endregion

    #region Mixed Comments

    [Test]
    public void ParseMixedCommentsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- Line comment
            SELECT /* block */ * FROM Users -- trailing");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseLineCommentInsideBlockCommentTest()
    {
        // Line comment inside block comment should be ignored
        var stmt = WitSql.ParseStatement(@"
            /* 
            -- This line comment is inside block comment
            */
            SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseBlockCommentMarkersInLineCommentTest()
    {
        // Block comment markers after -- should be ignored
        var stmt = WitSql.ParseStatement(@"
            -- /* This is not a block comment
            SELECT * FROM Users
            -- This is fine */");
        Assert.That(stmt, Is.Not.Null);
    }

    #endregion

    #region Comments in Different Statement Types

    [Test]
    public void ParseCommentInCreateTableTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INT, -- Primary key
                Name VARCHAR(100) /* User's display name */
            )");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseCommentInInsertTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- Insert new user
            INSERT INTO Users (Name) 
            VALUES ('John' /* first user */)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementInsert>());
    }

    [Test]
    public void ParseCommentInUpdateTest()
    {
        var stmt = WitSql.ParseStatement(@"
            UPDATE Users 
            SET Name = 'Jane' -- new name
            WHERE Id = 1 /* user id */");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementUpdate>());
    }

    [Test]
    public void ParseCommentInDeleteTest()
    {
        var stmt = WitSql.ParseStatement(@"
            /* Remove inactive users */
            DELETE FROM Users 
            WHERE IsActive = FALSE -- inactive flag");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDelete>());
    }

    [Test]
    public void ParseCommentInComplexQueryTest()
    {
        var stmt = WitSql.ParseStatement(@"
            /* 
             * Complex query to get user orders
             * Author: System
             * Date: 2024-01-01
             */
            SELECT 
                u.Name, -- User name
                o.Total, /* Order total */
                o.CreatedAt
            FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId -- Join condition
            WHERE o.Status = 'completed' /* Only completed orders */
            ORDER BY o.CreatedAt DESC -- Most recent first");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region Comments in Multiple Statements

    [Test]
    public void ParseMultipleStatementsWithCommentsTest()
    {
        var statements = WitSql.Parse(@"
            -- Statement 1
            SELECT 1;
            /* Statement 2 */
            SELECT 2;
            SELECT 3 -- Statement 3");
        
        Assert.That(statements, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseCommentBetweenStatementsTest()
    {
        var statements = WitSql.Parse(@"
            SELECT 1;
            -- Comment between statements
            SELECT 2");
        
        Assert.That(statements, Has.Count.EqualTo(2));
    }

    #endregion
}
